using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Utils;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Api.Request;
using Web3Services.Data.Models.Api.Response;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Models.Enums;
using Web3Services.Data.Utils;

namespace Web3Services.API.Endpoints.Addresses;

public class GetTransactionsBySubjectEndpoint(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory
) : Endpoint<GetTransactionsBySubjectRequest, OffsetPaginatedResponse<TransactionBySubjectItem>>
{

    public override void Configure()
    {
        Get("/transactions");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetTransactionsBySubjectRequest req, CancellationToken ct)
    {
        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<TransactionByAddress> query = dbContext.TransactionsByAddress
            .AsNoTracking()
            .Where(tx => tx.Subjects.Contains(req.Subject));

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

        IEnumerable<TransactionBySubjectItem> items = transactions.Select(tx => new TransactionBySubjectItem(
            Hash: tx.Hash,
            PaymentKeyHash: tx.PaymentKeyHash,
            StakeKeyHash: tx.StakeKeyHash,
            Slot: tx.Slot,
            Timestamp: SlotToTimestamp((long)tx.Slot),
            Subjects: [.. tx.Subjects],
            Activities: ClassifyTransactionActivities(tx, tx.PaymentKeyHash, tx.StakeKeyHash, req.Subject, inputUtxoLookup)
        ));

        OffsetPaginatedResponse<TransactionBySubjectItem> response = new(
            Items: items,
            TotalRecords: total
        );

        await Send.OkAsync(response, ct);
    }

    private static List<TransactionActivityGroup> ClassifyTransactionActivities(
        TransactionByAddress tx,
        string paymentKeyHash,
        string? stakeKeyHash,
        string subject,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        try
        {
            TransactionBody transactionBody = TransactionBody.Read(tx.Raw);
            List<TransactionActivityGroup> activities = [];

            IEnumerable<ActivityDetails> receivedDetails = ExtractReceivedActivities(transactionBody, paymentKeyHash, stakeKeyHash, subject);
            IEnumerable<ActivityDetails> sentDetails = ExtractSentActivities(tx, paymentKeyHash, stakeKeyHash, subject, inputUtxoLookup);

            if (receivedDetails.Any())
            {
                activities.Add(new TransactionActivityGroup(TransactionType.Received, receivedDetails));
            }

            if (sentDetails.Any())
            {
                activities.Add(new TransactionActivityGroup(TransactionType.Sent, sentDetails));
            }

            return activities.Count > 0 ? activities : [new TransactionActivityGroup(TransactionType.Other, [new ActivityDetails(TransactionType.Other, 0, null, null, null, null)])];
        }
        catch
        {
            return [new TransactionActivityGroup(TransactionType.Other, [new ActivityDetails(TransactionType.Other, 0, null, null, null, null)])];
        }
    }

    //
    private static string SlotToTimestamp(long slot)
    {
        DateTime utcTime = SlotUtil.GetUTCTimeFromSlot(SlotUtil.Mainnet, slot);
        return utcTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private static IEnumerable<ActivityDetails> ExtractReceivedActivities(
        TransactionBody transactionBody,
        string paymentKeyHash,
        string? stakeKeyHash,
        string subject)
    {
        return [.. transactionBody.Outputs()
            .Where(output => ReducerUtils.TryGetBech32AddressParts(output, out string outputPayment, out string? outputStake) &&
                            outputPayment == paymentKeyHash && outputStake == stakeKeyHash)
            .Select(output => new { output, amount = output.Amount().QuantityOf(subject) })
            .Where(x => x.amount.HasValue && x.amount.Value > 0)
            .Select(x => new ActivityDetails(
                Type: TransactionType.Received,
                Amount: x.amount!.Value,
                Subject: subject,
                Address: paymentKeyHash,
                PoolId: null,
                TypeId: null
            ))];
    }

    private static IEnumerable<ActivityDetails> ExtractSentActivities(
        TransactionByAddress tx,
        string paymentKeyHash,
        string? stakeKeyHash,
        string subject,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        if (!inputUtxoLookup.TryGetValue(tx.Hash, out List<OutputBySlot>? inputUtxos))
            return [];

        return [.. inputUtxos
            .Where(utxo => utxo.PaymentKeyHash == paymentKeyHash && utxo.StakeKeyHash == stakeKeyHash)
            .Select(utxo => new { utxo, output = TransactionOutput.Read(utxo.Raw) })
            .Select(x => new { x.utxo, x.output, amount = x.output.Amount().QuantityOf(subject) })
            .Where(x => x.amount.HasValue && x.amount.Value > 0)
            .Select(x => new ActivityDetails(
                Type: TransactionType.Sent,
                Amount: x.amount!.Value,
                Subject: subject,
                Address: paymentKeyHash,
                PoolId: null,
                TypeId: null
            ))];
    }
}