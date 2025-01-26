using Backend.Api.Base.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Payments.Endpoints;

public class GetPaymentsEndpoint(AppDbContext db)
    : Endpoint<PaginationRequest, Results<Ok<PaginatedResponse<Payment>>, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/api/payments");
    }

    public override async Task<Results<Ok<PaginatedResponse<Payment>>, ProblemDetails>> ExecuteAsync(
        PaginationRequest req, CancellationToken ct)
    {
        var skipped = req.PageSize * (req.Page - 1);
        var payments = await db.Payments
            .Skip(skipped)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var paymentsCount = await db.Payments.CountAsync(cancellationToken: ct);

        return TypedResults.Ok(PaginatedResponse<Payment>.Create(payments, paymentsCount, req.Page, req.PageSize,
            skipped));
    }
}