namespace SmartSchool.Shared.Common;

/// <summary>
/// Uniform envelope for EVERY API response (spec):
/// { "success": true/false, "message": "...", "data": {...}, "errors": [] }
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();

    public static ApiResponse Ok(string message = "OK")
        => new() { Success = true, Message = message };

    public static ApiResponse<T> Ok<T>(T data, string message = "OK")
        => ApiResponse<T>.Ok(data, message);

    public static ApiResponse Fail(string message, IEnumerable<string>? errors = null)
        => new() { Success = false, Message = message, Errors = errors?.ToList() ?? new List<string>() };
}

public sealed class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "OK")
        => new() { Success = true, Message = message, Data = data };

    public static new ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null)
        => new() { Success = false, Message = message, Errors = errors?.ToList() ?? new List<string>() };
}

/// <summary>Paged payload carried inside ApiResponse.Data.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
        => new(items, page, pageSize, totalCount);
}
