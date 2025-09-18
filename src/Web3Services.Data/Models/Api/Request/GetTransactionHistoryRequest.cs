using Web3Services.Data.Models.Api.Response;

namespace Web3Services.Data.Models.Api.Request;

public class GetTransactionHistoryRequest
{
    public string PaymentKeyHash { get; set; } = string.Empty;
    public string? StakeKeyHash { get; set; }
    public string? Cursor { get; set; }
    public int Limit { get; set; } = 50;
    public PaginationDirection Direction { get; set; } = PaginationDirection.Next;
}