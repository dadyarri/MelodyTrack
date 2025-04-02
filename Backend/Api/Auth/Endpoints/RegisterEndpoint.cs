using Backend.Api.Auth.Models;
using Backend.Data;
using Backend.Data.Entities;
using Backend.Utils;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = FastEndpoints.ProblemDetails;
using RegisterRequest = Backend.Api.Auth.Models.RegisterRequest;

namespace Backend.Api.Auth.Endpoints;

public class RegisterEndpoint(AppDbContext db) : Endpoint<RegisterRequest, Results<Ok<LoginResponse>, Conflict, ProblemDetails>>
{
    public override void Configure()
    {
        Post("/api/register");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LoginResponse>, Conflict, ProblemDetails>> ExecuteAsync(RegisterRequest req,
        CancellationToken ct)
    {
        UserUtils.CreatePasswordHash(req.Password, out var passwordSalt, out var passwordHash);

        var user = new User
        {
            Username = req.Username,
            FirstName = req.FirstName,
            LastName = req.LastName,
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
        };

        var hasUser = await db.Users.Where(e => e.Username == user.Username).AnyAsync(ct);

        if (hasUser)
        {
            return TypedResults.Conflict();
        }

        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(UserUtils.CreateAccessToken(user));
    }
}