using Chrysalis.Wallet.Models.Enums;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Api.Request;
using Web3Services.Data.Models.Api.Response;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Utils;

namespace Web3Services.API.Endpoints;


public class GetTrackedAddressesEndpoint(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory,
    IConfiguration configuration
) : Endpoint<GetTrackedAddressesRequest, PaginatedResponse<TrackedAddressResponse>>
{
    private readonly NetworkType _networkType = NetworkUtils.GetNetworkType(configuration);

    public override void Configure()
    {
        Get("/addresses/tracked");
        AllowAnonymous();

        RequestBinder(new GetTrackedAddressesBinder());

        Summary(s =>
        {
            s.Summary = "Get tracked addresses with pagination";
            s.Description = "Fetches a paginated list of tracked addresses. Supports cursor-based pagination in both directions.";
            s.Params["cursor"] = "Base64 encoded cursor for pagination. Use the nextCursor or previousCursor from the previous response.";
            s.Params["direction"] = "Pagination direction: 'Next' for forward pagination, 'Previous' for backward pagination. Default: 'Next'";
            s.Params["limit"] = "Number of addresses to return per page. Default: 50, Maximum: 100";
            s.RequestParam(r => r.Cursor, "Base64 encoded cursor for pagination");
        });

        Description(d => d
            .WithTags("Addresses")
            .Produces<PaginatedResponse<TrackedAddressResponse>>(200)
            .ProducesProblem(400)
            .ProducesProblem(500)
        );
    }

    public override async Task HandleAsync(GetTrackedAddressesRequest req, CancellationToken ct)
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

        IQueryable<TrackedAddress> query = BuildBaseQuery(dbContext, req.Cursor, req.Direction);

        List<TrackedAddress> addresses = await query
            .Take(req.Limit + 1)
            .ToListAsync(ct);

        bool actualHasMore = addresses.Count > req.Limit;
        if (actualHasMore)
        {
            addresses.RemoveAt(req.Limit);
        }

        if (req.Direction == PaginationDirection.Previous)
        {
            addresses.Reverse();
        }

        IEnumerable<TrackedAddressResponse> items = addresses.Select(ta => new TrackedAddressResponse(
            Address: ReducerUtils.ConstructBech32Address(ta.PaymentKeyHash, ta.StakeKeyHash ?? string.Empty, _networkType),
            CreatedAt: ta.CreatedAt
        ));

        Pagination pagination = BuildPagination(addresses, req.Cursor, req.Direction, actualHasMore);
        PaginatedResponse<TrackedAddressResponse> response = new(items, pagination);
        await Send.OkAsync(response, ct);
    }

    private static IQueryable<TrackedAddress> BuildBaseQuery(Web3ServicesDbContext dbContext, string? cursor, PaginationDirection direction)
    {
        IQueryable<TrackedAddress> baseQuery = dbContext.TrackedAddresses.AsNoTracking();
        return ApplyTrackedAddressCursorPagination(baseQuery, cursor, direction);
    }

    private static Pagination BuildPagination(List<TrackedAddress> addresses, string? cursor, PaginationDirection direction, bool actualHasMore)
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
            pagination.HasNext = decodedCursor != null || addresses.Count > 0;
        }

        if (decodedCursor == null && direction == PaginationDirection.Next)
        {
            pagination.HasPrevious = false;
            if (addresses.Count == 0 && !actualHasMore)
            {
                pagination.HasNext = false;
            }
        }

        if (addresses.Count > 0)
        {
            // Use PaymentKeyHash:StakeKeyHash as cursor (stored in OutRef field)
            Cursor nextCursor = new($"{addresses.Last().PaymentKeyHash}:{addresses.Last().StakeKeyHash}");
            Cursor previousCursor = new($"{addresses.First().PaymentKeyHash}:{addresses.First().StakeKeyHash}");
            pagination.NextCursor = pagination.HasNext ? nextCursor.EncodeCursor() : null;
            pagination.PreviousCursor = pagination.HasPrevious ? previousCursor.EncodeCursor() : null;
        }

        return pagination;
    }

    public static IQueryable<TrackedAddress> ApplyTrackedAddressCursorPagination(
        IQueryable<TrackedAddress> query,
        string? cursor,
        PaginationDirection direction)
    {
        Cursor? decodedCursor = Cursor.DecodeCursor(cursor);
        if (decodedCursor is not null)
        {
            if (string.IsNullOrEmpty(decodedCursor.Key))
            {
                return query.OrderBy(ta => ta.PaymentKeyHash).ThenBy(ta => ta.StakeKeyHash);
            }

            string[] parts = decodedCursor.Key.Split(':');
            if (parts.Length != 2)
            {
                return query.OrderBy(ta => ta.PaymentKeyHash).ThenBy(ta => ta.StakeKeyHash);
            }

            string cursorPayment = parts[0];
            string cursorStake = parts[1];

            return direction switch
            {
                PaginationDirection.Next => query.Where(ta =>
                        ta.PaymentKeyHash.CompareTo(cursorPayment) > 0 ||
                        (ta.PaymentKeyHash == cursorPayment && ta.StakeKeyHash.CompareTo(cursorStake) > 0))
                    .OrderBy(ta => ta.PaymentKeyHash)
                    .ThenBy(ta => ta.StakeKeyHash),

                PaginationDirection.Previous => query
                    .Where(ta =>
                        ta.PaymentKeyHash.CompareTo(cursorPayment) < 0 ||
                        (ta.PaymentKeyHash == cursorPayment && ta.StakeKeyHash.CompareTo(cursorStake) < 0))
                    .OrderByDescending(ta => ta.PaymentKeyHash)
                    .ThenByDescending(ta => ta.StakeKeyHash),
                _ => query.OrderBy(ta => ta.PaymentKeyHash).ThenBy(ta => ta.StakeKeyHash)
            };
        }
        else
        {
            return query
                .OrderBy(ta => ta.PaymentKeyHash)
                .ThenBy(ta => ta.StakeKeyHash);
        }
    }
}

class GetTrackedAddressesBinder : IRequestBinder<GetTrackedAddressesRequest>
{
    public ValueTask<GetTrackedAddressesRequest> BindAsync(BinderContext ctx, CancellationToken ct)
    {
        return ValueTask.FromResult(new GetTrackedAddressesRequest
        {
            Cursor = ctx.HttpContext.Request.Query["cursor"].FirstOrDefault(),
            Limit = int.TryParse(ctx.HttpContext.Request.Query["limit"].FirstOrDefault(), out int limit) ? limit : 50,
            Direction = Enum.TryParse(ctx.HttpContext.Request.Query["direction"].FirstOrDefault(), out PaginationDirection dir) ? dir : PaginationDirection.Next
        });
    }
}