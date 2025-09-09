using Argus.Sync.Data.Models;

namespace Web3Services.Data.Models.Entity;

public record TransactionByAddress(
    string StakeKeyHash,
    string PaymentKeyHash,
    IEnumerable<string> Subjects,
    string Hash,
    ulong Slot,
    byte[] Raw
) : IReducerModel;