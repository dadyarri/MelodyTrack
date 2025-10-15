using Backend.Api.Base.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Payments.Endpoints;

/// <summary>
///     Получить платежи
/// </summary>
/// <param name="db">БД</param>
public class GetPaymentsEndpoint(AppDbContext db)
    : Endpoint<PaginationRequest, Results<Ok<PaginatedResponse<Payment>>, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/payments");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<PaginatedResponse<Payment>>, ProblemDetails>> ExecuteAsync(
        PaginationRequest req, CancellationToken ct)
    {
        var skipped = req.PageSize * (req.Page - 1);
        var payments = await db.Payments
            .Skip(skipped)
            .Take(req.PageSize)
            .Include(e => e.Service)
            .Include(e => e.Client)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Client.LastName)
            .ThenBy(e => e.Client.FirstName)
            .ToListAsync(ct);

        var paymentsCount = await db.Payments.CountAsync(ct);

        return TypedResults.Ok(PaginatedResponse<Payment>.Create(payments, paymentsCount, req.Page, req.PageSize,
            skipped));
    }
}