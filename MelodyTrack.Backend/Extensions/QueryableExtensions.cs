using System.Linq.Expressions;
using System.Reflection;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Attributes;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Extensions;

public static class QueryableExtensions
{

    private static readonly MethodInfo LevenshteinMethod = typeof(NpgsqlFuzzyStringMatchDbFunctionsExtensions)
                                                               .GetMethod(
                                                                   nameof(NpgsqlFuzzyStringMatchDbFunctionsExtensions.FuzzyStringMatchLevenshtein),
                                                                   [typeof(DbFunctions), typeof(string), typeof(string)])
                                                           ?? throw new InvalidOperationException(
                                                               "NpgsqlFuzzyStringMatchDbFunctionsExtensions.FuzzyStringMatchLevenshtein not found.");

    extension<TEntity>(IQueryable<TEntity> queryable)
    {
        public IQueryable<TEntity> ApplyPagination(PaginatedRequest req)
        {
            return queryable.Skip(req.PageSize * (req.Page - 1)).Take(req.PageSize);
        }

        public IQueryable<TEntity> ApplyFuzzySearchFilters(PaginatedRequest req,
            int maxDistance = 3)
        {
            var entityType = typeof(TEntity);
            var parameter = Expression.Parameter(entityType, "e");

            foreach (var reqProp in req.GetType()
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (reqProp.PropertyType != typeof(string))
                {
                    continue;
                }

                var filterValue = (string?)reqProp.GetValue(req);
                if (string.IsNullOrWhiteSpace(filterValue))
                {
                    continue;
                }

                var directProp = entityType.GetProperty(
                    reqProp.Name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                Expression? memberAccess = null;

                if (directProp?.PropertyType == typeof(string))
                {
                    memberAccess = Expression.Property(parameter, directProp);
                }
                else
                {
                    var attr = reqProp.GetCustomAttribute<FuzzyPathAttribute>();
                    if (attr == null)
                    {
                        continue; // no fuzzy info → ignore
                    }

                    if (attr.EntityType != entityType)
                    {
                        throw new InvalidOperationException(
                            $"[FuzzyPath] on {reqProp.DeclaringType?.Name}.{reqProp.Name} " +
                            $"references root type {attr.EntityType} but query is on {entityType}.");
                    }

                    memberAccess = BuildNestedMemberAccess(parameter, attr.Path);
                }

                var callLevenshtein = Expression.Call(
                    null,
                    LevenshteinMethod,
                    Expression.Constant(EF.Functions),
                    memberAccess!,
                    Expression.Constant(filterValue, typeof(string)));

                var predicateBody = Expression.LessThanOrEqual(
                    callLevenshtein,
                    Expression.Constant(maxDistance, typeof(int)));

                var lambda = Expression.Lambda<Func<TEntity, bool>>(predicateBody, parameter);
                queryable = queryable.Where(lambda);
            }

            return queryable;
        }

        public IQueryable<TEntity> ApplyDateRangeFilter(Expression<Func<TEntity, DateTime?>> dateSelector,
            DateTime? from = null,
            DateTime? to = null)
        {
            if (!from.HasValue && !to.HasValue)
            {
                return queryable;
            }

            var param = dateSelector.Parameters[0];
            var member = dateSelector.Body;

            Expression? predicate = null;

            if (from.HasValue)
            {
                var constFrom = Expression.Constant(from.Value.AddDays(-1).AddTicks(1).ToUniversalTime(), typeof(DateTime?));
                var ge = Expression.GreaterThanOrEqual(member, constFrom);
                predicate = ge;
            }

            if (to.HasValue)
            {
                var constTo = Expression.Constant(to.Value.AddDays(1).AddTicks(-1).ToUniversalTime(), typeof(DateTime?));
                var le = Expression.LessThanOrEqual(member, constTo);
                predicate = predicate == null ? le : Expression.AndAlso(predicate, le);
            }

            var lambda = Expression.Lambda<Func<TEntity, bool>>(predicate!, param);
            return queryable.Where(lambda);
        }
    }

    private static Expression BuildNestedMemberAccess(Expression root, string memberPath)
    {
        if (string.IsNullOrWhiteSpace(memberPath))
        {
            throw new ArgumentException("Member path cannot be empty.", nameof(memberPath));
        }

        var parts = memberPath.Split('.');
        var current = root;

        foreach (var part in parts)
        {
            var prop = current.Type.GetProperty(
                           part,
                           BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? throw new InvalidOperationException(
                           $"Property '{part}' not found on type '{current.Type}' while building path '{memberPath}'.");

            current = Expression.Property(current, prop);
        }

        if (current.Type != typeof(string))
        {
            throw new InvalidOperationException(
                $"The final member in path '{memberPath}' resolves to type '{current.Type}' – it must be string for fuzzy search.");
        }

        return current;
    }
}