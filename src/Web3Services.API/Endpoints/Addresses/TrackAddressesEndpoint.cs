using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Entity;
using Web3Services.Data.Utils;

namespace Web3Services.API.Endpoints.Addresses;

public record TrackAddressesRequest(IEnumerable<string> Addresses);

public record TrackAddressesResponse(
    int Added,
    string Message
);

public class TrackAddressesEndpoint(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory
) : Endpoint<TrackAddressesRequest, TrackAddressesResponse>
{
    public override void Configure()
    {
        Post("/track_address");
        AllowAnonymous();
    }

    public override async Task HandleAsync(TrackAddressesRequest req, CancellationToken ct)
    {
        try
        {
            await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync(ct);

            IEnumerable<TrackedAddress> trackedAddresses = [.. req.Addresses
                .Distinct()
                .Where(addr => ReducerUtils.TryGetBech32AddressParts(addr, out _, out _))
                .Select(addr => {
                    ReducerUtils.TryGetBech32AddressParts(addr, out string payment, out string? stake);
                    return new TrackedAddress(payment, stake ?? string.Empty, DateTime.UtcNow);
                })];

            if (!trackedAddresses.Any())
            {
                TrackAddressesResponse noNewResponse = new(0, "No new addresses to track");
                await Send.OkAsync(noNewResponse, cancellation: ct);
                return;
            }

            dbContext.TrackedAddresses.AddRange(trackedAddresses);
            await dbContext.SaveChangesAsync(ct);
            
            TrackAddressesResponse response = new(trackedAddresses.Count(), "Addresses tracked successfully");
            await Send.OkAsync(response, cancellation: ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while processing addresses: {ex.Message}");
            await Send.ErrorsAsync(500, ct);
        }
    }
}