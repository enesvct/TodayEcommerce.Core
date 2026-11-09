namespace TodayEcommerce.Core.Application.Common;

/// <summary>One page of a larger result, plus what a client needs to show "page 2 of 7".</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);
