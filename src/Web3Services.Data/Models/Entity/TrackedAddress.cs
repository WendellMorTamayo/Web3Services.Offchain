using Argus.Sync.Data.Models;

namespace Web3Services.Data.Models.Entity;

public record TrackedAddress(
    string PaymentKeyHash,
    string StakeKeyHash,
    DateTime CreatedAt
) : IReducerModel;