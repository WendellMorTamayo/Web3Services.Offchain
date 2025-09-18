using System.Text.Json;

namespace Web3Services.Data.Models.Api.Response;

public record PaginatedResponse<T>(IEnumerable<T> Items, Pagination Pagination);

public enum PaginationDirection
{
    Previous,
    Next
}

public record Pagination
{
    public bool HasPrevious { get; set; }
    public bool HasNext { get; set; }
    public string? PreviousCursor { get; set; }
    public string? NextCursor { get; set; }
}

public record OffsetPaginatedResponse<T>(IEnumerable<T> Items, int TotalRecords);

public record Cursor(string Key)
{
    public string EncodeCursor()
    {
        string json = JsonSerializer.Serialize(this);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    public static Cursor? DecodeCursor(string? cursorStr)
    {
        if (string.IsNullOrEmpty(cursorStr)) return null;

        try
        {
            byte[] jsonBytes = Convert.FromBase64String(cursorStr);
            string jsonString = System.Text.Encoding.UTF8.GetString(jsonBytes);
            return JsonSerializer.Deserialize<Cursor>(jsonString);
        }
        catch
        {
            return null;
        }
    }
}