using System.Net;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Auth.Endpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
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

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "test@example.com",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCodeEntity!.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.TotpRequired.ShouldBeTrue();
        res.OtpUrl.ShouldNotBeNull();
        res.Secret.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("1234", 3)]
    [InlineData("qwerty", 3)]
    [InlineData("rhbetnrf", 1)]
    [InlineData("1wetpussy", 2)]
    public async Task RegisterNewSuperUser_WithSimplePassword_FailsValidation(string password, int expectedAmountOfErrors)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1),
        };
        
        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();
        
        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "test@example.com",
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });
        
        rsp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        res.ShouldNotBeNull();
        res.Errors.Count().ShouldBe(expectedAmountOfErrors);
        res.Errors.ShouldAllBe(e => e.Name == "password");
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("")]
    public async Task RegisterNewSuperUser_WithInvalidInviteCode_Fails(string inviteCode)
    {
        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "test@example.com",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode
        });
        
        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldBeNull();
    }

    [Fact]
    public async Task RegisterNewSuperUser_WithUsedInviteCode_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1),
            WasUsed = true
        };
        
        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();
        
        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "test@example.com",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });
        
        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldBeNull();
    }
    
    [Fact]
    public async Task RegisterNewSuperUser_WithDuplicatedEmail_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCode1 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };
        
        await db.InviteCodes.AddAsync(inviteCode1);
        await db.SaveChangesAsync();
        
        var inviteCode2 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };
        
        await db.InviteCodes.AddAsync(inviteCode2);
        await db.SaveChangesAsync();
        
        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "test1@example.com",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode1.Code.ToString()
        });
        
        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.TotpRequired.ShouldBeTrue();
        res.OtpUrl.ShouldNotBeNull();
        res.Secret.ShouldNotBeNull();
        
        var (rsp2, res2) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "test1@example.com",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode2.Code.ToString()
        });
        
        rsp2.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res2.ShouldBeNull();
    }
}