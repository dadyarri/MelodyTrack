using System.Linq.Expressions;
using System.Reflection;
using MelodyTrack.Backend.Api.Common.Requests;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<TEntity> ApplyPagination<TEntity>(this IQueryable<TEntity> queryable,
        PaginatedRequest req)
    {
        return queryable.Skip(req.PageSize * (req.Page - 1)).Take(req.PageSize);
    }

    public static IQueryable<TEntity> ApplyFilters<TEntity>(this IQueryable<TEntity> queryable, PaginatedRequest req,
        int maxDistance = 3)
    {
        var entityType = typeof(TEntity);
        var parameter = Expression.Parameter(entityType, "e");

        foreach (var reqProperty in req.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (reqProperty.PropertyType != typeof(string))
            {
                continue;
            }

            var filterValue = (string?)reqProperty.GetValue(req);

            if (string.IsNullOrWhiteSpace(filterValue))
            {
                continue;
            }

            var entityProperty = entityType.GetProperty(
                reqProperty.Name,
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.IgnoreCase
            );

            if (entityProperty == null)
            {
                continue;
            }

            if (entityProperty.PropertyType != typeof(string))
            {
                continue;
            }

            // e => EF.Functions.FuzzyStringMatchLevenshtein(e.PropertyName, filterValue) <= maxDistance
            var entityPropertyAccess = Expression.Property(parameter, entityProperty);
            var filterValueConstant = Expression.Constant(filterValue, typeof(string));
            var maxDistanceConstant = Expression.Constant(maxDistance, typeof(int));

            var levenshteinMethod = typeof(NpgsqlFuzzyStringMatchDbFunctionsExtensions)
                .GetMethod(nameof(NpgsqlFuzzyStringMatchDbFunctionsExtensions.FuzzyStringMatchLevenshtein),
                    [typeof(DbFunctions), typeof(string), typeof(string)]);

            if (levenshteinMethod == null)
            {
                throw new InvalidOperationException(
                    "Could not find EF Core's FuzzyStringMatchLevenshtein method. Ensure Npgsql.EntityFrameworkCore.PostgreSQL is installed and the method is accessible.");
            }

            var callLevenshtein = Expression.Call(
                null,
                levenshteinMethod,
                Expression.Constant(EF.Functions),
                entityPropertyAccess,
                filterValueConstant
            );

            var predicateBody = Expression.LessThanOrEqual(callLevenshtein, maxDistanceConstant);

            var lambda = Expression.Lambda<Func<TEntity, bool>>(predicateBody, parameter);
            queryable = queryable.Where(lambda);
        }

        return queryable;
    }
}