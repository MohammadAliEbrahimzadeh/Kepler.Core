using Ardalis.GuardClauses;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Kepler.Core.Pagination
{
    /// <summary>
    /// Provides extension methods for applying pagination to IQueryable<T> queries.
    /// </summary>
    public static class PaginationExtensions
    {
        /// <summary>
        /// Applies simple pagination to the query.
        /// </summary>
        /// <typeparam name="T">The type of the query elements.</typeparam>
        /// <param name="query">The IQueryable to paginate.</param>
        /// <param name="page">The page number (1-based). Default is 1.</param>
        /// <param name="pageSize">The number of items per page. Default is 10.</param>
        /// <returns>An IQueryable containing only the items for the specified page.</returns>
        public static IQueryable<T> ApplyKeplerPagination<T>(this IQueryable<T> query, int page = 1, int pageSize = 10) where T : class
        {
            Guard.Against.NegativeOrZero(page, nameof(page), "Page must be greater than 0");
            Guard.Against.NegativeOrZero(pageSize, nameof(pageSize), "Page size must be greater than 0");

            var skip = (page - 1) * pageSize;
            return query.Skip(skip).Take(pageSize);
        }

        /// <summary>
        /// Applies pagination with default page and page size, and returns the total count of items.
        /// </summary>
        /// <typeparam name="T">The type of the query elements.</typeparam>
        /// <param name="query">The IQueryable to paginate.</param>
        /// <param name="totalCount">Outputs the total number of items in the original query.</param>
        /// <returns>An IQueryable containing only the items for the default page (page 1, 10 items per page).</returns>
        public static IQueryable<T> ApplyKeplerPaginationWithCount<T>(this IQueryable<T> query, out int totalCount) where T : class
        {
            return ApplyKeplerPaginationWithCount(query, 1, 10, out totalCount);
        }

        /// <summary>
        /// Applies pagination to the query and returns the total count of items.
        /// </summary>
        /// <typeparam name="T">The type of the query elements.</typeparam>
        /// <param name="query">The IQueryable to paginate.</param>
        /// <param name="page">The page number (1-based).</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <param name="totalCount">Outputs the total number of items in the original query.</param>
        /// <returns>An IQueryable containing only the items for the specified page.</returns>
        public static IQueryable<T> ApplyKeplerPaginationWithCount<T>(this IQueryable<T> query, int page, int pageSize, out int totalCount) where T : class
        {
            Guard.Against.NegativeOrZero(page, nameof(page), "Page must be greater than 0");
            Guard.Against.NegativeOrZero(pageSize, nameof(pageSize), "Page size must be greater than 0");

            totalCount = query.Count();
            var skip = (page - 1) * pageSize;
            return query.Skip(skip).Take(pageSize);
        }
    }
}
