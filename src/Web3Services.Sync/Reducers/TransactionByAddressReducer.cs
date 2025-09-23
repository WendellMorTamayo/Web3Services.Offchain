using System.Linq.Expressions;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Utils;

namespace Web3Services.Sync.Reducers;

[DependsOn(typeof(OutputBySlotReducer))]
public class TransactionByAddressReducer(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory
) : IReducer<TransactionByAddress>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        await dbContext.TransactionsByAddress
            .Where(e => e.Slot >= slot)
            .ExecuteDeleteAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();
        if (!transactions.Any()) return;

        IEnumerable<TrackedAddress> trackedAddresses = await GetTrackedAddressesInBlock(dbContext, transactions);
        if (!trackedAddresses.Any()) return;

        ulong slot = block.Header().HeaderBody().Slot();
        Dictionary<string, List<OutputBySlot>> resolvedInputsLookup = await BuildResolvedInputsLookup(dbContext, transactions, trackedAddresses, slot);

        ProcessTransactions(slot, transactions, resolvedInputsLookup, trackedAddresses, dbContext);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<IEnumerable<TrackedAddress>> GetTrackedAddressesInBlock(
        Web3ServicesDbContext dbContext,
        IEnumerable<TransactionBody> transactions)
    {
        IEnumerable<(string PaymentHash, string StakeHash)> blockAddresses = ReducerUtils.ExtractAddressHashesFromBlock(transactions);

        Expression<Func<TrackedAddress, bool>> predicate = PredicateBuilder.False<TrackedAddress>();
        blockAddresses.ToList().ForEach(address =>
        {
            predicate = predicate.Or(ta => ta.PaymentKeyHash == address.PaymentHash && ta.StakeKeyHash == address.StakeHash);
        });

        return await dbContext.TrackedAddresses.Where(predicate).ToListAsync();
    }

    private static async Task<Dictionary<string, List<OutputBySlot>>> BuildResolvedInputsLookup(
        Web3ServicesDbContext dbContext,
        IEnumerable<TransactionBody> transactions,
        IEnumerable<TrackedAddress> trackedAddresses,
        ulong slot)
    {
        IEnumerable<string> inputTxHashes = [.. transactions
            .SelectMany(tx => tx.Inputs())
            .Select(input => Convert.ToHexStringLower(input.TransactionId))
            .Distinct()];

        IEnumerable<OutputBySlot> resolvedInputs = await dbContext.OutputsBySlot
            .Where(obs => inputTxHashes.Contains(obs.SpentTxHash))
            .ToListAsync();

        IEnumerable<OutputBySlot> updatedResolvedInputs = UpdateResolvedInputsWithSpendingTx(resolvedInputs, transactions, slot);
        IEnumerable<OutputBySlot> sameBlockResolvedInputs = ResolveSameBlockInputs(transactions, inputTxHashes, trackedAddresses, slot);

        return updatedResolvedInputs.Concat(sameBlockResolvedInputs)
            .Where(input => !string.IsNullOrEmpty(input.SpentTxHash))
            .GroupBy(input => input.SpentTxHash)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static IEnumerable<OutputBySlot> UpdateResolvedInputsWithSpendingTx(
        IEnumerable<OutputBySlot> resolvedInputs,
        IEnumerable<TransactionBody> transactions,
        ulong slot)
    {
        Dictionary<string, string> transactionInputMap = transactions
            .SelectMany(tx => tx.Inputs().Select(input => new
            {
                InputRef = $"{Convert.ToHexStringLower(input.TransactionId)}#{input.Index}",
                SpendingTxHash = tx.Hash()
            }))
            .ToDictionary(x => x.InputRef, x => x.SpendingTxHash);

        return resolvedInputs.Select(input =>
            transactionInputMap.TryGetValue(input.OutRef, out string? spendingTxHash)
                ? input with { SpentTxHash = spendingTxHash, SpentSlot = slot }
                : input);
    }

    private static List<OutputBySlot> ResolveSameBlockInputs(
        IEnumerable<TransactionBody> transactions,
        IEnumerable<string> inputTxHashes,
        IEnumerable<TrackedAddress> trackedAddresses,
        ulong slot)
    {
        IEnumerable<TransactionBody> referencedTxs = [.. transactions.Where(tx => inputTxHashes.Contains(tx.Hash()))];

        if (!referencedTxs.Any()) return [];

        return [.. transactions
            .SelectMany(spendingTx => spendingTx.Inputs().Select(input => new { spendingTx, input }))
            .Where(x => inputTxHashes.Contains(Convert.ToHexStringLower(x.input.TransactionId)))
            .SelectMany(x =>
            {
                string inputTxHash = Convert.ToHexStringLower(x.input.TransactionId);
                TransactionBody? referencedTx = referencedTxs.FirstOrDefault(tx => tx.Hash() == inputTxHash);

                if (referencedTx == null) return [];

                TransactionOutput spentOutput = referencedTx.Outputs().ElementAt((int)x.input.Index);
                (string PaymentHash, string StakeHash)? addressParts = ExtractAddressParts(spentOutput);

                if (addressParts != null && IsTrackedAddress(addressParts.Value, trackedAddresses))
                {
                    return
                    [
                        new OutputBySlot(
                            OutRef: $"{inputTxHash}#{x.input.Index}",
                            Slot: slot,
                            SpentTxHash: x.spendingTx.Hash(),
                            SpentSlot: slot,
                            PaymentKeyHash: addressParts.Value.PaymentHash,
                            StakeKeyHash: addressParts.Value.StakeHash,
                            Raw: spentOutput.Raw.HasValue ? spentOutput.Raw.Value.ToArray() : CborSerializer.Serialize(spentOutput)
                        )
                    ];
                }

                return Enumerable.Empty<OutputBySlot>();
            })];
    }

    private static (string PaymentHash, string StakeHash)? ExtractAddressParts(TransactionOutput output)
    {
        if (ReducerUtils.TryGetBech32AddressParts(output, out string paymentHash, out string? stakeHash))
        {
            return (paymentHash, stakeHash ?? string.Empty);
        }
        return null;
    }

    private static bool IsTrackedAddress((string PaymentHash, string StakeHash) addressParts, IEnumerable<TrackedAddress> trackedAddresses)
        => trackedAddresses.Any(ta =>
                ta.PaymentKeyHash == addressParts.PaymentHash && ta.StakeKeyHash == addressParts.StakeHash);

    private static void ProcessTransactions(
        ulong slot,
        IEnumerable<TransactionBody> transactions,
        Dictionary<string, List<OutputBySlot>> resolvedInputsLookup,
        IEnumerable<TrackedAddress> trackedAddresses,
        Web3ServicesDbContext dbContext
    )
    {
        HashSet<(string PaymentKeyHash, string StakeKeyHash, string Hash)> addedEntries = [];

        transactions.ToList().ForEach(tx =>
        {
            string txHash = tx.Hash();

            IEnumerable<(string PaymentKeyHash, string StakeKeyHash)> trackedOutputs =
                ExtractTrackedAddressesFromOutputs(tx.Outputs(), trackedAddresses);

            IEnumerable<(string PaymentKeyHash, string StakeKeyHash)> trackedInputs =
                resolvedInputsLookup.TryGetValue(txHash, out List<OutputBySlot>? txInputs)
                    ? ExtractTrackedAddressesFromInputs(txInputs, trackedAddresses)
                    : [];

            if (!trackedOutputs.Any() && !trackedInputs.Any()) return;

            IEnumerable<string> subjects = [.. tx.Outputs().SelectMany(ReducerUtils.ExtractSubjectsFromOutput)];

            HashSet<(string PaymentKeyHash, string StakeKeyHash)> uniqueAddresses = [.. trackedOutputs, .. trackedInputs];

            uniqueAddresses.ToList().ForEach(address =>
            {
                (string PaymentKeyHash, string StakeKeyHash, string txHash) key = (address.PaymentKeyHash, address.StakeKeyHash, txHash);

                if (!addedEntries.Contains(key))
                {
                    TransactionByAddress entry = new(
                        StakeKeyHash: address.StakeKeyHash,
                        PaymentKeyHash: address.PaymentKeyHash,
                        Subjects: subjects,
                        Hash: txHash,
                        Slot: slot,
                        Raw: tx.Raw.HasValue ? tx.Raw.Value.ToArray() : CborSerializer.Serialize(tx)
                    );

                    dbContext.TransactionsByAddress.Add(entry);
                    addedEntries.Add(key);
                }
            });
        });
    }

    private static IEnumerable<(string PaymentKeyHash, string StakeKeyHash)> ExtractTrackedAddressesFromOutputs(
        IEnumerable<TransactionOutput> outputs,
        IEnumerable<TrackedAddress> trackedAddresses)
    {
        return outputs
            .Select(output =>
            {
                if (ReducerUtils.TryGetBech32AddressParts(output, out string paymentHash, out string stakeHash))
                    return (PaymentKeyHash: paymentHash, StakeKeyHash: stakeHash);
                return (PaymentKeyHash: string.Empty, StakeKeyHash: string.Empty);
            })
            .Where(addr => !string.IsNullOrEmpty(addr.PaymentKeyHash))
            .Where(addr => trackedAddresses.Any(ta =>
                ta.PaymentKeyHash == addr.PaymentKeyHash &&
                ta.StakeKeyHash == addr.StakeKeyHash));
    }

    private static IEnumerable<(string PaymentKeyHash, string StakeKeyHash)> ExtractTrackedAddressesFromInputs(
        IEnumerable<OutputBySlot> resolvedInputs,
        IEnumerable<TrackedAddress> trackedAddresses)
    {
        return resolvedInputs
            .Where(input => trackedAddresses.Any(ta =>
                ta.PaymentKeyHash == input.PaymentKeyHash &&
                ta.StakeKeyHash == input.StakeKeyHash))
            .Select(input => (input.PaymentKeyHash, input.StakeKeyHash));
    }
}