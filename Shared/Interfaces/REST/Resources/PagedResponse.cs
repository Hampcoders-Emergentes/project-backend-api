using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Shared.Interfaces.REST.Resources;

/// <summary>
///     Pagination query of list endpoints: <c>?page=1&amp;pageSize=20</c>. Out-of-range values are clamped
///     (page &gt;= 1, 1 &lt;= pageSize &lt;= <see cref="MaxPageSize"/>) rather than rejected.
/// </summary>
public sealed class PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    [FromQuery(Name = "page")]
    public int Page { get => _page; set => _page = Math.Max(1, value); }

    [FromQuery(Name = "pageSize")]
    public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, MaxPageSize); }

    public int Skip => (Page - 1) * PageSize;

    public static PageRequest Of(int page, int pageSize) => new() { Page = page, PageSize = pageSize };
}

/// <summary>
///     Envelope of every paginated list: <c>{ items, page, pageSize, totalCount, totalPages, hasMore }</c>.
///     <c>totalPages</c> is 0 when there are no items; <c>hasMore</c> is <c>page &lt; totalPages</c>.
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount, int TotalPages)
{
    public bool HasMore => Page < TotalPages;

    /// <summary>A page whose items were already selected by the data source, with the total of the whole result.</summary>
    public static PagedResponse<T> Create(IReadOnlyList<T> items, PageRequest request, long totalCount) =>
        new(items, request.Page, request.PageSize, totalCount, (int)((totalCount + request.PageSize - 1) / request.PageSize));
}

public static class PagedResponse
{
    /// <summary>Pages a complete, already ordered sequence (used where the query returns a bounded set).</summary>
    public static PagedResponse<T> From<T>(IEnumerable<T> source, PageRequest request)
    {
        var all = source as IReadOnlyCollection<T> ?? source.ToList();
        var items = all.Skip(request.Skip).Take(request.PageSize).ToList();
        return PagedResponse<T>.Create(items, request, all.Count);
    }
}
