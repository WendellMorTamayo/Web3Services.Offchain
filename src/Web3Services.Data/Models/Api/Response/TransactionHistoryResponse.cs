using Web3Services.Data.Models.Enums;

namespace Web3Services.Data.Models.Api.Response;

public record TransactionActivityGroup(
    TransactionType Type,
    IEnumerable<ActivityDetails> Details
);

public record TransactionHistoryItem(
    string Hash,
    IEnumerable<TransactionActivityGroup> Activities,
    ulong Slot,
    string Timestamp,
    IEnumerable<string> Subjects,
    byte[] Raw
);

public record TransactionHistoryResponse(
    string PaymentKeyHash,
    string StakeKeyHash,
    IEnumerable<TransactionHistoryItem> Transactions
);

public record ActivityDetails(
    TransactionType Type,
    ulong Amount,
    string? Subject,
    string? Address,
    string? PoolId,
    int? TypeId = null
);