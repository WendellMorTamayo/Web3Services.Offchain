using Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Governance;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Addresses;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Extensions;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Api.Request;
using Web3Services.Data.Models.Api.Response;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Models.Enums;
using Web3Services.Data.Utils;

namespace Web3Services.API.Endpoints.Addresses;

public class GetTransactionHistoryEndpoint(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory
) : Endpoint<GetTransactionHistoryRequest, OffsetPaginatedResponse<TransactionHistoryItem>>
{
    public override void Configure()
    {
        Get("/transactions/history");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetTransactionHistoryRequest req, CancellationToken ct)
    {
        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<TransactionByAddress> query = dbContext.TransactionsByAddress
            .Where(tx => tx.PaymentKeyHash == req.PaymentKeyHash);

        if (!string.IsNullOrEmpty(req.StakeKeyHash))
        {
            query = query.Where(tx => tx.StakeKeyHash == req.StakeKeyHash);
        }

        int total = await query.CountAsync(ct);

        query = req.SortDirection switch
        {
            SortDirection.Ascending => query.OrderBy(tx => tx.Slot).ThenBy(tx => tx.Hash),
            _ => query.OrderByDescending(tx => tx.Slot).ThenByDescending(tx => tx.Hash)
        };

        IEnumerable<TransactionByAddress> transactions = await query
            .Skip(req.Offset)
            .Take(req.Limit)
            .ToListAsync(ct);

        IEnumerable<TransactionBody> transactionBodies = [.. transactions.Select(tx => TransactionBody.Read(tx.Raw))];

        IEnumerable<string> allInputTxHashes = [.. transactionBodies
            .SelectMany(txBody => txBody.Inputs()
                .Select(input => Convert.ToHexStringLower(input.TransactionId)))
            .Distinct()];

        Dictionary<string, List<OutputBySlot>> inputUtxoLookup = allInputTxHashes.Any()
            ? (await dbContext.OutputsBySlot
                .Where(obs => allInputTxHashes.Contains(obs.SpentTxHash))
                .ToListAsync(ct))
                .GroupBy(obs => obs.SpentTxHash)
                .ToDictionary(g => g.Key, g => g.ToList())
            : [];

        IEnumerable<TransactionHistoryItem> historyItems = transactions
            .Select(tx => new TransactionHistoryItem(
                Hash: tx.Hash,
                Activities: ClassifyTransactionActivities(tx, req.PaymentKeyHash, req.StakeKeyHash, inputUtxoLookup),
                Slot: tx.Slot,
                Timestamp: SlotToTimestamp((long)tx.Slot),
                Subjects: tx.Subjects,
                Raw: tx.Raw
            ));

        OffsetPaginatedResponse<TransactionHistoryItem> response = new(
            Items: historyItems,
            TotalRecords: total
        );

        await Send.OkAsync(response, cancellation: ct);
    }

    private static IEnumerable<TransactionActivityGroup> ClassifyTransactionActivities(
        TransactionByAddress tx,
        string paymentKeyHash,
        string? stakeKeyHash,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        List<TransactionActivityGroup> activities = [];

        try
        {
            TransactionBody txBody = CborSerializer.Deserialize<TransactionBody>(tx.Raw);

            IEnumerable<TransactionActivityGroup> stakeActivities = AnalyzeStakingActivities(txBody, stakeKeyHash);
            activities.AddRange(stakeActivities);

            IEnumerable<TransactionActivityGroup> withdrawalActivities = AnalyzeWithdrawalActivities(txBody, stakeKeyHash);
            activities.AddRange(withdrawalActivities);

            IEnumerable<TransactionActivityGroup> votingActivities = AnalyzeVotingActivities(txBody, paymentKeyHash);
            activities.AddRange(votingActivities);

            IEnumerable<TransactionActivityGroup> transferActivities = AnalyzeTransferActivities(txBody, paymentKeyHash, stakeKeyHash, tx, inputUtxoLookup);
            activities.AddRange(transferActivities);

            return activities.AsEnumerable();
        }
        catch
        {
            return [new TransactionActivityGroup(TransactionType.Other, [new ActivityDetails(TransactionType.Other, 0, null, null, null, null)])];
        }
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeStakingActivities(
        TransactionBody txBody,
        string? stakeKeyHash)
    {
        if (string.IsNullOrEmpty(stakeKeyHash) || txBody.Certificates()?.Count() == 0)
            return [];

        List<ActivityDetails> stakeDetails = [];

        // Add stake-related certificates
        List<ActivityDetails> stakeCerts = txBody.Certificates()?
            .Where(cert => cert.IsStakeRelated())
            .Select(cert => new ActivityDetails(
                Type: TransactionType.Stake,
                Amount: cert.Coin() ?? 0UL,
                Subject: string.Empty,
                Address: null,
                PoolId: cert.GetPoolId(),
                TypeId: (int)cert.GetCertificateType()
            ))
            .ToList() ?? [];

        stakeDetails.AddRange(stakeCerts);

        if (stakeDetails.Any())
        {
            return [new TransactionActivityGroup(TransactionType.Stake, stakeDetails)];
        }

        return [];
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeWithdrawalActivities(
        TransactionBody txBody,
        string? stakeKeyHash)
    {
        if (string.IsNullOrEmpty(stakeKeyHash) || txBody.Withdrawals()?.Count == 0)
            return [];

        List<ActivityDetails> withdrawalDetails = [];
        txBody.Withdrawals()?.ToList().ForEach(withdrawal =>
        {
            try
            {
                withdrawalDetails.Add(new ActivityDetails(
                    Type: TransactionType.Withdraw,
                    Amount: withdrawal.Value,
                    Subject: string.Empty,
                    Address: withdrawal.Key != null // TODO: verify this is correct
                        ? new Address(withdrawal.Key.Value()).ToBech32()
                        : null,
                    PoolId: null,
                    TypeId: null
                ));
            }
            catch
            {
                return;
            }
        });

        if (withdrawalDetails.Any())
        {
            return [new TransactionActivityGroup(TransactionType.Withdraw, withdrawalDetails)];
        }

        return [];
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeVotingActivities(
        TransactionBody txBody,
        string paymentKeyHash)
    {
        if (txBody.VotingProcedures()?.Any() == false)
            return [];

        List<ActivityDetails> votingDetails = [];
        txBody.VotingProcedures()?.ToList().ForEach(vote =>
        {
            try
            {
                int voterType = vote.Key.Tag();
                byte[] voterHash = vote.Key.Hash();
                if (voterHash != null)
                {
                    string voterHashHex = Convert.ToHexStringLower(voterHash);

                    bool isOurVote = voterType switch
                    {
                        0 or 2 or 4 => voterHashHex == paymentKeyHash,  // Addr Key-based voters
                        _ => false  // Script-based voters or unknown types
                    };

                    if (isOurVote)
                    {
                        votingDetails.Add(new ActivityDetails(
                            Type: TransactionType.Vote,
                            Amount: 0,
                            Subject: null,
                            Address: null,
                            PoolId: null,
                            TypeId: voterType
                        ));
                    }
                }
            }
            catch
            {
                return;
            }
        });

        if (votingDetails.Any())
        {
            return [new TransactionActivityGroup(TransactionType.Vote, votingDetails)];
        }

        return [];
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeTransferActivities(
        TransactionBody txBody,
        string paymentKeyHash,
        string? stakeKeyHash,
        TransactionByAddress tx,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        IEnumerable<TransactionActivityGroup> activities = [];

        try
        {
            IEnumerable<ActivityDetails> receivedDetails = ExtractReceivedTransfers(txBody, paymentKeyHash, stakeKeyHash);
            IEnumerable<ActivityDetails> sentDetails = ExtractSentTransfers(tx, paymentKeyHash, stakeKeyHash, inputUtxoLookup);

            if (receivedDetails.Any())
            {
                activities = [new TransactionActivityGroup(TransactionType.Received, receivedDetails)];
            }

            if (sentDetails.Any())
            {
                activities = [new TransactionActivityGroup(TransactionType.Sent, sentDetails)];
            }
        }
        catch
        {
            // Fallback - return empty
        }

        return activities;
    }

    private static string SlotToTimestamp(long slot)
    {
        DateTime utcTime = SlotUtil.GetUTCTimeFromSlot(SlotUtil.Mainnet, slot);
        return utcTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private static List<ActivityDetails> ExtractReceivedTransfers(
        TransactionBody txBody,
        string paymentKeyHash,
        string? stakeKeyHash)
    {
        List<ActivityDetails> allReceivedDetails = [];

        List<TransactionOutput> addressOutputs = [.. txBody.Outputs()
            .Where(output => ReducerUtils.TryGetBech32AddressParts(output, out string paymentHash, out string? stakeHash) &&
                            paymentHash == paymentKeyHash && stakeHash == stakeKeyHash)];
        
        addressOutputs.ForEach(output =>
        {
            string address = new Address(output.Address()).ToBech32();

            // Add ADA amount
            if (output.Amount().Lovelace() > 0)
            {
                allReceivedDetails.Add(new ActivityDetails(
                    Type: TransactionType.Received,
                    Amount: output.Amount().Lovelace(),
                    Subject: string.Empty,
                    Address: address,
                    PoolId: null,
                    TypeId: null
                ));
            }

            // Add multi-asset amounts
            if (output.Amount().MultiAsset() != null)
            {
                allReceivedDetails.AddRange(
                    output.Amount().MultiAsset().Keys
                        .Select(keyBytes => Convert.ToHexString(keyBytes).ToLower())
                        .Select(subject => new { subject, amount = output.Amount().QuantityOf(subject) })
                        .Where(x => x.amount.HasValue && x.amount.Value > 0)
                        .Select(x => new ActivityDetails(
                            Type: TransactionType.Received,
                            Amount: x.amount ?? 0,
                            Subject: x.subject,
                            Address: address,
                            PoolId: null,
                            TypeId: null
                        ))
                );
            }
        });

        return allReceivedDetails;
    }

    private static List<ActivityDetails> ExtractSentTransfers(
        TransactionByAddress tx,
        string paymentKeyHash,
        string? stakeKeyHash,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        if (!inputUtxoLookup.TryGetValue(tx.Hash, out List<OutputBySlot>? inputUtxos))
            return [];

        return inputUtxos
            .Where(utxo => utxo.PaymentKeyHash == paymentKeyHash && utxo.StakeKeyHash == stakeKeyHash)
            .Select(utxo => new { utxo, output = TransactionOutput.Read(utxo.Raw) })
            .Where(x => x.output.Amount().Lovelace() > 0)
            .Select(x => new ActivityDetails(
                Type: TransactionType.Sent,
                Amount: x.output.Amount().Lovelace(),
                Subject: string.Empty,
                Address: paymentKeyHash,
                PoolId: null,
                TypeId: null
            ))
            .ToList();
    }
}