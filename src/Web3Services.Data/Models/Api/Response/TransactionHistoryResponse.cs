using Web3Services.Data.Models.Enums;

namespace Web3Services.Data.Models.Api.Response;

public record TransactionActivityGroup(
    IEnumerable<ActivityDetails> Details
);

public record TransactionHistoryItem(
    string Hash,
    IEnumerable<ActivityDetails> Activities,
    ulong Slot,
    string Timestamp,
    string Raw
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

public record SubjectActivityDetails(
    TransactionType Type,
    ulong Amount,
    string? Address
);

public record SubjectTransactionActivityGroup(
    IEnumerable<SubjectActivityDetails> Details
);