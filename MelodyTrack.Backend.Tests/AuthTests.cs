using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Auth.Endpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class AuthTests(MelodyTrackFixture app) : TestBase<MelodyTrackFixture>
{
    [Fact]
    public async Task RegisterNewSuperUser_WithValidRequest_Success()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var superuserRole = await db.Roles.FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser)
            .ShouldNotBeNull("Superuser role should exist in migrations.");
        var inviteCodeEntity = await db.InviteCodes
            .AsNoTracking()
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Role == superuserRole && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow)
            .ShouldNotBeNull("Superuser invite code should be created on startup (or by migrations).");

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, Results<Created<RegisterResponse>, ForbidHttpResult>>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "test@example.com",
            Password = "test",
            InviteCode = inviteCodeEntity!.Code.ToString()
        });
    }
}