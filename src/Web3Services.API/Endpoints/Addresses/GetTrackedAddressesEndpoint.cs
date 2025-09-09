using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Api.Response;

namespace Web3Services.API.Endpoints.Addresses;

public record GetTrackedAddressesRequest(int Offset = 0, int Limit = 100);

public record TrackedAddressResponse(string PaymentKeyHash, string? StakeKeyHash, DateTime CreatedAt);

public class GetTrackedAddressesEndpoint : Endpoint<GetTrackedAddressesRequest, OffsetPaginatedResponse<TrackedAddressResponse>>
{
    public Web3ServicesDbContext DbContext { get; set; } = null!;

    public override void Configure()
    {
        Get("/tracked_addresses");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetTrackedAddressesRequest req, CancellationToken ct)
    {
        int total = await DbContext.TrackedAddresses.CountAsync(ct);
        
        IEnumerable<TrackedAddressResponse> addresses = await DbContext.TrackedAddresses
            .OrderBy(ta => ta.CreatedAt)
            .Skip(req.Offset)
            .Take(req.Limit)
            .Select(ta => new TrackedAddressResponse(ta.PaymentKeyHash, ta.StakeKeyHash, ta.CreatedAt))
            .ToListAsync(ct);

        OffsetPaginatedResponse<TrackedAddressResponse> response = new(
            Items: addresses,
            TotalRecords: total
        );
        
        await Send.OkAsync(response, cancellation: ct);
    }
}