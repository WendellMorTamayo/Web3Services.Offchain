namespace Web3Services.Data.Models.Api.Response;

public record OffsetPaginatedResponse<T>(IEnumerable<T> Items, int TotalRecords);