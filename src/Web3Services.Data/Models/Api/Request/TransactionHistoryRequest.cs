using Web3Services.Data.Models.Enums;

namespace Web3Services.Data.Models.Api.Request;

public record GetTransactionHistoryRequest(
    string PaymentKeyHash,
    string? StakeKeyHash = null,
    int Offset = 0,
    int Limit = 50,
    SortDirection SortDirection = SortDirection.Descending
);