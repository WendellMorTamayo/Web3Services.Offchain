using Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Governance;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Extensions;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Api.Request;
using Web3Services.Data.Models.Api.Response;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Models.Enums;
using Web3Services.Data.Utils;

namespace Web3Services.API.Endpoints;

// TODO: Test staking/governance activities
public class GetTransactionHistoryEndpoint(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory,
    IConfiguration configuration
) : Endpoint<GetTransactionHistoryRequest, PaginatedResponse<TransactionHistoryItem>>
{
    private readonly NetworkType _networkType = NetworkUtils.GetNetworkType(configuration);

    public override void Configure()
    {
        Get("/transactions/addresses/{address}/history");
        AllowAnonymous();
        RequestBinder(new GetTransactionHistoryBinder());

        Summary(s =>
        {
            s.Summary = "Get transaction history for a specific address";
            s.Description = "Fetches paginated transaction history for a bech32 address. Supports cursor-based pagination in both directions.";
            s.Params["address"] = "Bech32 address to get transaction history for";
            s.Params["cursor"] = "Base64 encoded cursor for pagination. Use the nextCursor or previousCursor from the previous response";
            s.Params["direction"] = "Pagination direction: 'Next' for newer transactions, 'Previous' for older transactions. Default: 'Next'";
            s.Params["limit"] = "Number of transactions to return per page. Default: 50, Maximum: 100";
        });

        Description(d => d
            .WithTags("Transactions")
            .Produces<PaginatedResponse<TransactionHistoryItem>>(200)
            .ProducesProblem(400)
            .ProducesProblem(500)
        );
    }

    public override async Task HandleAsync(GetTransactionHistoryRequest req, CancellationToken ct)
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

        if (!ReducerUtils.TryGetBech32AddressParts(req.Address, out string paymentKeyHash, out string? stakeKeyHash))
        {
            AddError("Invalid bech32 address format");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        IQueryable<TransactionByAddress> query = BuildBaseQuery(dbContext, paymentKeyHash, stakeKeyHash, req.Cursor, req.Direction);

        List<TransactionByAddress> transactions = await query
            .Take(req.Limit + 1)
            .ToListAsync(ct);

        bool actualHasMore = transactions.Count > req.Limit;
        if (actualHasMore)
            transactions.RemoveAt(req.Limit);

        if (req.Direction == PaginationDirection.Previous)
            transactions.Reverse();

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
                Activities: ClassifyTransactionActivities(tx, paymentKeyHash, stakeKeyHash, inputUtxoLookup),
                Slot: tx.Slot,
                Timestamp: ReducerUtils.SlotToTimestamp((long)tx.Slot, _networkType),
                Raw: Convert.ToHexStringLower(tx.Raw)
            ));

        Pagination pagination = BuildPagination(transactions, req.Cursor, req.Direction, actualHasMore);
        PaginatedResponse<TransactionHistoryItem> response = new(historyItems, pagination);

        await Send.OkAsync(response, ct);
    }

    private static IEnumerable<ActivityDetails> ClassifyTransactionActivities(
        TransactionByAddress tx,
        string paymentKeyHash,
        string? stakeKeyHash,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        List<ActivityDetails> activities = [];

        try
        {
            TransactionBody txBody = CborSerializer.Deserialize<TransactionBody>(tx.Raw);

            // Analyze separate transfer activities
            activities.AddRange(AnalyzeTransferActivities(txBody, paymentKeyHash, stakeKeyHash, tx, inputUtxoLookup).SelectMany(g => g.Details));

            // Add other activity types
            activities.AddRange(AnalyzeStakingActivities(txBody, stakeKeyHash).SelectMany(g => g.Details));
            activities.AddRange(AnalyzeWithdrawalActivities(txBody, stakeKeyHash).SelectMany(g => g.Details));
            activities.AddRange(AnalyzeVotingActivities(txBody, paymentKeyHash).SelectMany(g => g.Details));

            return activities.AsEnumerable();
        }
        catch
        {
            return [new ActivityDetails(TransactionType.Other, 0, null, null, null, null)];
        }
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeTransferActivities(
        TransactionBody txBody,
        string paymentKeyHash,
        string? stakeKeyHash,
        TransactionByAddress tx,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        List<TransactionActivityGroup> activities = [];

        try
        {
            IEnumerable<ActivityDetails> receivedDetails = ExtractReceivedTransfers(txBody, paymentKeyHash, stakeKeyHash);
            IEnumerable<ActivityDetails> sentDetails = ExtractSentTransfers(tx, paymentKeyHash, stakeKeyHash, inputUtxoLookup);
            IEnumerable<ActivityDetails> selfDetails = ExtractSelfTransfers(txBody, paymentKeyHash, stakeKeyHash, tx, inputUtxoLookup);

            if (receivedDetails.Any()) activities.Add(new TransactionActivityGroup(receivedDetails));
            if (sentDetails.Any()) activities.Add(new TransactionActivityGroup(sentDetails));
            if (selfDetails.Any()) activities.Add(new TransactionActivityGroup(selfDetails));
        }
        catch
        {
            // Fallback - return empty
        }

        return activities.AsEnumerable();
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
            allReceivedDetails.AddRange(ExtractActivityDetailsFromOutput(output, TransactionType.Received));
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

        List<ActivityDetails> allSentDetails = [];

        inputUtxos
            .Where(utxo => utxo.PaymentKeyHash == paymentKeyHash && utxo.StakeKeyHash == stakeKeyHash)
            .ToList()
            .ForEach(utxo =>
            {
                TransactionOutput output = TransactionOutput.Read(utxo.Raw);
                allSentDetails.AddRange(ExtractActivityDetailsFromOutput(output, TransactionType.Sent));
            });

        return allSentDetails;
    }

    private static List<ActivityDetails> ExtractSelfTransfers(
        TransactionBody txBody,
        string paymentKeyHash,
        string? stakeKeyHash,
        TransactionByAddress tx,
        Dictionary<string, List<OutputBySlot>> inputUtxoLookup)
    {
        List<ActivityDetails> selfDetails = [];

        List<TransactionOutput> selfOutputs = [.. txBody.Outputs()
            .Where(output =>
            {
                if (!ReducerUtils.TryGetBech32AddressParts(output, out string outputPaymentHash, out string? outputStakeHash)) return false;
                return outputPaymentHash == paymentKeyHash && outputStakeHash == stakeKeyHash;
            })];

        // Only consider it a self-transfer if we have inputs from the same address
        bool hasInputsFromSameAddress = inputUtxoLookup.ContainsKey(tx.Hash) &&
            inputUtxoLookup[tx.Hash].Any(utxo => utxo.PaymentKeyHash == paymentKeyHash && utxo.StakeKeyHash == stakeKeyHash);

        if (hasInputsFromSameAddress)
        {
            selfOutputs.ForEach(output =>
            {
                selfDetails.AddRange(ExtractActivityDetailsFromOutput(output, TransactionType.Self));
            });
        }

        return selfDetails;
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeStakingActivities(
        TransactionBody txBody,
        string? stakeKeyHash)
    {
        if (string.IsNullOrEmpty(stakeKeyHash) || txBody.Certificates()?.Count() == 0)
            return [];

        List<ActivityDetails> stakeDetails = [];

        // Add stake-related certificates
        IEnumerable<ActivityDetails> stakeCerts = txBody.Certificates()?
            .Where(cert => cert.IsStakeRelated())
            .Select(cert => new ActivityDetails(
                Type: TransactionType.Stake,
                Amount: cert.Coin() ?? 0UL,
                Subject: string.Empty,
                Address: null,
                PoolId: cert.GetPoolId(),
                TypeId: (int)cert.GetCertificateType()
            )) ?? [];

        stakeDetails.AddRange(stakeCerts);

        if (stakeDetails.Any())
        {
            return [new TransactionActivityGroup(stakeDetails)];
        }

        return [];
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeWithdrawalActivities(
        TransactionBody txBody,
        string? stakeKeyHash)
    {
        if (string.IsNullOrEmpty(stakeKeyHash) || txBody.Withdrawals()?.Count == 0) return [];

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
            return [new TransactionActivityGroup(withdrawalDetails)];
        }

        return [];
    }

    private static IEnumerable<TransactionActivityGroup> AnalyzeVotingActivities(
        TransactionBody txBody,
        string paymentKeyHash)
    {
        if (txBody.VotingProcedures()?.Any() == false) return [];

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
            return [new TransactionActivityGroup(votingDetails)];
        }

        return [];
    }

    private static IQueryable<TransactionByAddress> BuildBaseQuery(
        Web3ServicesDbContext dbContext,
        string paymentKeyHash,
        string? stakeKeyHash,
        string? cursor,
        PaginationDirection direction)
    {
        IQueryable<TransactionByAddress> baseQuery = dbContext.TransactionsByAddress
            .AsNoTracking()
            .Where(tx => tx.PaymentKeyHash == paymentKeyHash);

        if (!string.IsNullOrEmpty(stakeKeyHash))
        {
            baseQuery = baseQuery.Where(tx => tx.StakeKeyHash == stakeKeyHash);
        }

        return ApplyTransactionHistoryCursorPagination(baseQuery, cursor, direction);
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
            Cursor nextCursor = new($"{transactions.Last().Slot}:{transactions.Last().Hash}");
            Cursor previousCursor = new($"{transactions.First().Slot}:{transactions.First().Hash}");
            pagination.NextCursor = pagination.HasNext ? nextCursor.EncodeCursor() : null;
            pagination.PreviousCursor = pagination.HasPrevious ? previousCursor.EncodeCursor() : null;
        }

        return pagination;
    }

    private static List<ActivityDetails> ExtractActivityDetailsFromOutput(
        TransactionOutput output,
        TransactionType type)
    {
        List<ActivityDetails> details = [];
        string address = new Address(output.Address()).ToBech32();

        // Add ADA amount
        if (output.Amount().Lovelace() > 0)
        {
            details.Add(new ActivityDetails(
                Type: type,
                Amount: output.Amount().Lovelace(),
                Subject: string.Empty,
                Address: address,
                PoolId: null,
                TypeId: null
            ));
        }

        // Add multi-asset amounts
        if (output.Amount().MultiAsset().Any())
        {
            details.AddRange(
                output.Amount().MultiAsset().Keys
                    .Select(keyBytes => Convert.ToHexString(keyBytes).ToLower())
                    .Select(subject => new { subject, amount = output.Amount().QuantityOf(subject) })
                    .Where(x => x.amount.HasValue && x.amount.Value > 0)
                    .Select(x => new ActivityDetails(
                        Type: type,
                        Amount: x.amount ?? 0,
                        Subject: x.subject,
                        Address: address,
                        PoolId: null,
                        TypeId: null
                    ))
            );
        }

        return details;
    }

    public static IQueryable<TransactionByAddress> ApplyTransactionHistoryCursorPagination(
        IQueryable<TransactionByAddress> query,
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

class GetTransactionHistoryBinder : IRequestBinder<GetTransactionHistoryRequest>
{
    public ValueTask<GetTransactionHistoryRequest> BindAsync(BinderContext ctx, CancellationToken ct)
    {
        return ValueTask.FromResult(new GetTransactionHistoryRequest
        {
            Address = ctx.HttpContext.Request.RouteValues["address"]?.ToString()!,
            Cursor = ctx.HttpContext.Request.Query["cursor"].FirstOrDefault(),
            Limit = int.TryParse(ctx.HttpContext.Request.Query["limit"].FirstOrDefault(), out int limit) ? limit : 50,
            Direction = Enum.TryParse<PaginationDirection>(ctx.HttpContext.Request.Query["direction"].FirstOrDefault(), out var dir) ? dir : PaginationDirection.Next
        });
    }
}

