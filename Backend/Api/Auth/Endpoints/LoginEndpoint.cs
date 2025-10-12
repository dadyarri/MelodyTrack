using Backend.Api.Auth.Models;
using Backend.Data;
using Backend.Utils;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Auth.Endpoints;

/// <summary>
///     Вход существующего пользователя
/// </summary>
/// <param name="db">БД</param>
public class LoginEndpoint(AppDbContext db)
    : Endpoint<LoginRequest, Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        LoginRequest req,
        CancellationToken ct)
    {
        var user = await db.Users.Where(e => e.Username == req.Username).FirstOrDefaultAsync(ct);

        if (user == null || !UserUtils.IsValidPassword(user, req)) return TypedResults.Unauthorized();

        return TypedResults.Ok(UserUtils.CreateAccessToken(user));
    }
}