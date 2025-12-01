using Ardalis.GuardClauses;
using Microsoft.EntityFrameworkCore;

namespace Kepler.Core.Pagination;


/// <summary>
/// Pagination extension for IQueryable<T>
/// </summary>
public static class PaginationExtensions
{
    /// <summary>
    /// Apply pagination to a query (simple version)
    /// </summary>
    /// <param name="query">The IQueryable to paginate</param>
    /// <param name="page">Page number (1-based, default = 1)</param>
    /// <param name="pageSize">Items per page (default = 10)</param>
    /// <returns>Paginated query</returns>
    public static IQueryable<T> ApplyKeplerPagination<T>(this IQueryable<T> query, int page = 1, int pageSize = 10) where T : class
    {
        Guard.Against.NegativeOrZero(page, nameof(page), "Page must be greater than 0");
        Guard.Against.NegativeOrZero(pageSize, nameof(pageSize), "Page size must be greater than 0");

        var skip = (page - 1) * pageSize;

        return query.Skip(skip).Take(pageSize);
    }
}