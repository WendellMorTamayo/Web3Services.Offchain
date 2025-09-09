namespace Web3Services.Data.Models.Api.Response;

public record TransactionBySubjectItem(
    string Hash,
    string PaymentKeyHash,
    string StakeKeyHash,
    ulong Slot,
    string Timestamp,
    IEnumerable<string> Subjects,
    IEnumerable<TransactionActivityGroup> Activities
);