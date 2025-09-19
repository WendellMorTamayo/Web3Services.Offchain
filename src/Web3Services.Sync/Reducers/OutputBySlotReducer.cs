using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Utils;
using Block = Chrysalis.Cbor.Types.Cardano.Core.Block;

namespace Web3Services.Sync.Reducers;

public class OutputBySlotReducer(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory
) : IReducer<OutputBySlot>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        IQueryable<OutputBySlot> outputsToDelete = dbContext.OutputsBySlot.Where(e => e.Slot >= slot).AsNoTracking();
        dbContext.OutputsBySlot.RemoveRange(outputsToDelete);

        IEnumerable<OutputBySlot> outputsToUnSpend = await dbContext.OutputsBySlot
            .Where(e => e.Slot < slot && e.SpentSlot >= slot)
            .ToListAsync();

        outputsToUnSpend.ToList().ForEach(spentOutput =>
        {
            OutputBySlot unspentOutput = spentOutput with
            {
                SpentTxHash = string.Empty,
                SpentSlot = null
            };

            dbContext.OutputsBySlot.Remove(spentOutput);
            dbContext.OutputsBySlot.Add(unspentOutput);
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();
        if (!transactions.Any()) return;

        IEnumerable<(string PaymentHash, string StakeHash)> blockAddresses =
            ReducerUtils.ExtractAddressHashesFromBlock(transactions);
        Expression<Func<TrackedAddress, bool>> predicate = PredicateBuilder.False<TrackedAddress>();
        blockAddresses.ToList().ForEach(address =>
        {
            predicate = predicate.Or(ta => ta.PaymentKeyHash == address.PaymentHash && ta.StakeKeyHash == address.StakeHash);
        });

        IEnumerable<TrackedAddress> trackedAddressesInBlock = await dbContext.TrackedAddresses
            .Where(predicate)
            .ToListAsync();

        if (!trackedAddressesInBlock.Any()) return;

        ulong currentSlot = block.Header().HeaderBody().Slot();
        IEnumerable<(string txHash, IEnumerable<TransactionOutput> outputs)> outputsByTx = transactions
            .Select(tx => (tx.Hash(), tx.Outputs()));

        ProcessOutputs(outputsByTx, currentSlot, dbContext, trackedAddressesInBlock);

        IEnumerable<string> inputTxHashes = transactions
            .SelectMany(tx => tx.Inputs())
            .Select(input => Convert.ToHexStringLower(input.TransactionId))
            .Distinct();

        IEnumerable<OutputBySlot> resolvedInputs = await dbContext.OutputsBySlot
            .Where(obs => inputTxHashes.Contains(obs.SpentTxHash))
            .ToListAsync();

        ProcessInputs(resolvedInputs, transactions, currentSlot, dbContext, trackedAddressesInBlock);
        await dbContext.SaveChangesAsync();
    }

    private static void ProcessOutputs(
        IEnumerable<(string txHash, IEnumerable<TransactionOutput> outputs)> outputsByTx,
        ulong currentSlot,
        Web3ServicesDbContext dbContext,
        IEnumerable<TrackedAddress> trackedAddresses
    )
    {
        IEnumerable<OutputBySlot?> allPotentialOutputs = outputsByTx
            .SelectMany(obtx =>
                obtx.outputs
                .Select((output, index) =>
                {
                    if (!ReducerUtils.TryGetBech32AddressParts(output, out string paymentKeyHash, out string? stakeKeyHash))
                        return null;

                    OutputBySlot newEntry = new(
                        Slot: currentSlot,
                        OutRef: obtx.txHash + "#" + index,
                        SpentTxHash: string.Empty,
                        SpentSlot: null,
                        PaymentKeyHash: paymentKeyHash,
                        StakeKeyHash: stakeKeyHash ?? string.Empty,
                        Raw: output.Raw.HasValue ? output.Raw.Value.ToArray() : CborSerializer.Serialize(output)
                    );

                    return newEntry;
                })
            );

        IEnumerable<OutputBySlot> allNewOutputs = allPotentialOutputs.Where(e => e != null)!;

        if (!allNewOutputs.Any()) return;

        IEnumerable<OutputBySlot> trackedOutputs = allNewOutputs
            .Where(output => trackedAddresses.Any(ta =>
                ta.PaymentKeyHash == output.PaymentKeyHash &&
                ta.StakeKeyHash == output.StakeKeyHash));

        dbContext.AddRange(trackedOutputs);
    }

    private static void ProcessInputs(
        IEnumerable<OutputBySlot> resolvedInputs,
        IEnumerable<TransactionBody> transactions,
        ulong currentSlot,
        Web3ServicesDbContext dbContext,
        IEnumerable<TrackedAddress> trackedAddresses
    )
    {
        IEnumerable<(string spentTxHash, OutputBySlot resolvedInput)> resolvedInputsByTx = transactions
            .SelectMany(tx =>
            {
                IEnumerable<string> txInputs = tx.Inputs().Select(input => $"{Convert.ToHexStringLower(input.TransactionId)}#{input.Index}");
                IEnumerable<OutputBySlot> resolvedInputsByTx = resolvedInputs.Where(ri => txInputs.Contains(ri.OutRef));

                return resolvedInputsByTx.Select(ribtx => (tx.Hash(), ribtx));
            });

        resolvedInputsByTx.ToList().ForEach(resolvedInputByTx =>
        {
            OutputBySlot? existingOutput = dbContext.OutputsBySlot.Local
                .FirstOrDefault(e => e.OutRef == resolvedInputByTx.resolvedInput.OutRef);

            OutputBySlot updatedOutput =
                resolvedInputByTx.resolvedInput with { SpentTxHash = resolvedInputByTx.spentTxHash, SpentSlot = currentSlot };

            if (existingOutput != null)
            {
                dbContext.Remove(existingOutput);
                dbContext.Add(updatedOutput);
                return;
            }

            dbContext.Update(updatedOutput);
        });
    }
}