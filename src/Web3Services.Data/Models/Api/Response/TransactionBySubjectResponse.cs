namespace Web3Services.Data.Models.Api.Response;

public record TransactionBySubjectItem(
    string Hash,
    string Address,
    ulong Slot,
    string Timestamp,
    IEnumerable<SubjectTransactionActivityGroup> Activities
);