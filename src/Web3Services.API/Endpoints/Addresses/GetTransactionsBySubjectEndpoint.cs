using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Api.Request;
using Web3Services.Data.Models.Api.Response;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Models.Enums;
using Web3Services.Data.Utils;

namespace Web3Services.API.Endpoints.Addresses;

public class GetTransactionsBySubjectBinder : IRequestBinder<GetTransactionsBySubjectRequest>
{
    public ValueTask<GetTransactionsBySubjectRequest> BindAsync(BinderContext ctx, CancellationToken ct)
    {
        return ValueTask.FromResult(new GetTransactionsBySubjectRequest
        {
            Subject = ctx.HttpContext.Request.RouteValues["subject"]?.ToString()!,
            Cursor = ctx.HttpContext.Request.Query["cursor"].FirstOrDefault(),
            Limit = int.TryParse(ctx.HttpContext.Request.Query["limit"].FirstOrDefault(), out int limit) ? limit : 50,
            Direction = Enum.TryParse<PaginationDirection>(ctx.HttpContext.Request.Query["direction"].FirstOrDefault(), out var dir) ? dir : PaginationDirection.Next
        });
    }
}

public class GetTransactionsBySubjectEndpoint(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory,
    IConfiguration configuration
) : Endpoint<GetTransactionsBySubjectRequest, PaginatedResponse<TransactionBySubjectItem>>
{
    private readonly NetworkType _networkType = NetworkUtils.GetNetworkType(configuration);

    public override void Configure()
    {
        Get("/transactions/subjects/{subject}/history");
        AllowAnonymous();
        RequestBinder(new GetTransactionsBySubjectBinder());

        Summary(s =>
        {
            s.Summary = "Get transactions by subject with pagination";
            s.Description = "Fetches paginated transactions containing a specific subject (asset). Supports cursor-based pagination in both directions.";
            s.Params["subject"] = "The subject (asset) to filter transactions by. Can be a policy ID or policy ID + asset name.";
            s.Params["cursor"] = "Base64 encoded cursor for pagination. Use the nextCursor or previousCursor from the previous response.";
            s.Params["direction"] = "Pagination direction: 'Next' for newer transactions, 'Previous' for older transactions. Default: 'Next'";
            s.Params["limit"] = "Number of transactions to return per page. Default: 50, Maximum: 100";
        });

        Description(d => d
            .WithTags("Transactions")
            .Produces<PaginatedResponse<TransactionBySubjectItem>>(200)
            .ProducesProblem(400)
            .ProducesProblem(500)
        );
    }

    public override async Task HandleAsync(GetTransactionsBySubjectRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Cursor) && req.Direction == PaginationDirection.Previous)
        {
            AddError("Invalid pagination action: Cannot fetch previous page without a cursor.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (req.Limit <= 0)
        {
            AddError("Limit must be greater than 0");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<TransactionByAddress> query = BuildBaseQuery(dbContext, req.Subject, req.Cursor, req.Direction);

        List<TransactionByAddress> transactions = await query
            .Take(req.Limit + 1)
            .ToListAsync(ct);

        bool actualHasMore = transactions.Count > req.Limit;
        if (actualHasMore)
        {
            transactions.RemoveAt(req.Limit);
        }

        if (req.Direction == PaginationDirection.Previous)
        {
            transactions.Reverse();
        }

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
            Address: ReducerUtils.ConstructBech32Address(tx.PaymentKeyHash, tx.StakeKeyHash, _networkType),
            Slot: tx.Slot,
            Timestamp: ReducerUtils.SlotToTimestamp((long)tx.Slot, _networkType),
            Activities: ClassifyTransactionActivities(tx, tx.PaymentKeyHash, tx.StakeKeyHash, req.Subject, inputUtxoLookup)
        ));

        Pagination pagination = BuildPagination(transactions, req.Cursor, req.Direction, actualHasMore);
        PaginatedResponse<TransactionBySubjectItem> response = new(items, pagination);

        await Send.OkAsync(response, ct);
    }

    private static IEnumerable<SubjectTransactionActivityGroup> ClassifyTransactionActivities(
        TransactionByAddress tx,
        string paymentKeyHash,
        string? stakeKeyHash,
        string subject,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        try
        {
            TransactionBody transactionBody = TransactionBody.Read(tx.Raw);
            List<SubjectTransactionActivityGroup> activities = [];

            IEnumerable<SubjectActivityDetails> receivedDetails = ExtractReceivedActivities(transactionBody, paymentKeyHash, stakeKeyHash, subject);
            IEnumerable<SubjectActivityDetails> sentDetails = ExtractSentActivities(tx, paymentKeyHash, stakeKeyHash, subject, inputUtxoLookup);

            if (receivedDetails.Any())
            {
                activities.Add(new SubjectTransactionActivityGroup(receivedDetails));
            }

            if (sentDetails.Any())
            {
                activities.Add(new SubjectTransactionActivityGroup(sentDetails));
            }

            return activities.Count > 0 ? activities.AsEnumerable() : [new SubjectTransactionActivityGroup([new SubjectActivityDetails(TransactionType.Other, 0, null)])];
        }
        catch
        {
            return [new SubjectTransactionActivityGroup([new SubjectActivityDetails(TransactionType.Other, 0, null)])];
        }
    }

    private static IEnumerable<SubjectActivityDetails> ExtractReceivedActivities(
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
            .Select(x => new SubjectActivityDetails(
                Type: TransactionType.Received,
                Amount: x.amount!.Value,
                Address: paymentKeyHash
            ))];
    }

    private static IEnumerable<SubjectActivityDetails> ExtractSentActivities(
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
            .Select(x => new SubjectActivityDetails(
                Type: TransactionType.Sent,
                Amount: x.amount!.Value,
                Address: paymentKeyHash
            ))];
    }

    private static IQueryable<TransactionByAddress> BuildBaseQuery(
        Web3ServicesDbContext dbContext,
        string subject,
        string? cursor,
        PaginationDirection direction)
    {
        IQueryable<TransactionByAddress> baseQuery = dbContext.TransactionsByAddress
            .AsNoTracking()
            .Where(tx => tx.Subjects.Contains(subject));

        return baseQuery.ApplyTransactionsBySubjectCursorPagination(cursor, direction);
    }

    private static Pagination BuildPagination(List<TransactionByAddress> transactions, string? cursor, PaginationDirection direction, bool actualHasMore)
    {
        Cursor? decodedCursor = Cursor.DecodeCursor(cursor);

        Pagination pagination = new();
        if (direction == PaginationDirection.Next)
        {
            pagination.HasNext = actualHasMore;
            pagination.HasPrevious = decodedCursor != null;
        }
        else
        {
            pagination.HasPrevious = actualHasMore;
            pagination.HasNext = decodedCursor != null || transactions.Count > 0;
        }

        if (decodedCursor == null && direction == PaginationDirection.Next)
        {
            pagination.HasPrevious = false;
            if (transactions.Count == 0 && !actualHasMore)
            {
                pagination.HasNext = false;
            }
        }

        if (transactions.Count > 0)
        {
            // Include both hash and slot for stable pagination - encode as "slot:hash"
            Cursor nextCursor = new($"{transactions.Last().Slot}:{transactions.Last().Hash}");
            Cursor previousCursor = new($"{transactions.First().Slot}:{transactions.First().Hash}");
            pagination.NextCursor = pagination.HasNext ? nextCursor.EncodeCursor() : null;
            pagination.PreviousCursor = pagination.HasPrevious ? previousCursor.EncodeCursor() : null;
        }

        return pagination;
    }
}

static class TransactionsBySubjectQueryExtensions
{
    public static IQueryable<TransactionByAddress> ApplyTransactionsBySubjectCursorPagination(
        this IQueryable<TransactionByAddress> query,
        string? cursor,
        PaginationDirection direction)
    {
        Cursor? decodedCursor = Cursor.DecodeCursor(cursor);
        if (decodedCursor is not null)
        {
            if (string.IsNullOrEmpty(decodedCursor.Key))
            {
                return query.OrderByDescending(tx => tx.Slot).ThenByDescending(tx => tx.Hash);
            }

            // Parse "slot:hash" format
            string[] parts = decodedCursor.Key.Split(':');
            if (parts.Length != 2 || !ulong.TryParse(parts[0], out ulong cursorSlot))
            {
                return query.OrderByDescending(tx => tx.Slot).ThenByDescending(tx => tx.Hash);
            }
            
            string cursorHash = parts[1];

            return direction switch
            {
                PaginationDirection.Next => query
                    .Where(tx => tx.Slot < cursorSlot || (tx.Slot == cursorSlot && tx.Hash.CompareTo(cursorHash) > 0))
                    .OrderByDescending(tx => tx.Slot)
                    .ThenByDescending(tx => tx.Hash),

                PaginationDirection.Previous => query
                    .Where(tx => tx.Slot > cursorSlot || (tx.Slot == cursorSlot && tx.Hash.CompareTo(cursorHash) < 0))
                    .OrderBy(tx => tx.Slot)
                    .ThenBy(tx => tx.Hash),
                _ => query.OrderByDescending(tx => tx.Slot).ThenByDescending(tx => tx.Hash)
            };
        }
        else
        {
            return query
                .OrderByDescending(tx => tx.Slot)
                .ThenByDescending(tx => tx.Hash);
        }
    }
}