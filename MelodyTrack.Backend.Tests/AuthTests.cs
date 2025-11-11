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
            Email = Fake.Internet.Email(),
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
            Email = Fake.Internet.Email(),
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
            Email = Fake.Internet.Email(),
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
            Email = Fake.Internet.Email(),
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

        var inviteCode2 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode2);
        await db.SaveChangesAsync();

        var email = Fake.Internet.Email();
        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = email,
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
            Email = email,
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode2.Code.ToString()
        });

        rsp2.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res2.ShouldBeNull();
    }

    [Fact]
    public async Task RegisterNewRegularUser_WithValidRequest_Success()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "john",
            LastName = "doe",
            Email = Fake.Internet.Email(),
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.TotpRequired.ShouldBeFalse("Regular users should not require TOTP");
        res.Secret.ShouldBeNull();
        res.OtpUrl.ShouldBeNull();
    }

    [Fact]
    public async Task RegisterNewAdmin_WithValidRequest_Success()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Admin)
            .ShouldNotBeNull("Admin role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = adminRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "admin",
            LastName = "user",
            Email = Fake.Internet.Email(),
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.TotpRequired.ShouldBeTrue("Admin users should require TOTP");
        res.OtpUrl.ShouldNotBeNull();
        res.Secret.ShouldNotBeNull();
    }

    [Fact]
    public async Task RegisterNewUser_WithExpiredInviteCode_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddSeconds(-1)
        };

        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = Fake.Internet.Email(),
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldBeNull();
    }

    [Fact]
    public async Task RegisterNewUser_WithCaseInsensitiveEmail_Success()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "TeSt.UsEr@ExAmPlE.CoM",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        await db.Users
            .FirstOrDefaultAsync(u => u.Email == "test.user@example.com")
            .ShouldNotBeNull("User should be created with lowercase email");
    }

    [Fact]
    public async Task RegisterNewUser_WithDuplicateEmailDifferentCase_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode1 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode1);
        await db.SaveChangesAsync();

        var inviteCode2 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode2);
        await db.SaveChangesAsync();

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "iyfcyckin@example.com",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode1.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        var (rsp2, res2) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "IyFcyCkiN@ExAmPlE.CoM",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode2.Code.ToString()
        });

        rsp2.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res2.ShouldBeNull();
    }

    [Fact]
    public async Task RegisterNewUser_WithInviteCodePresetEmail_UsesPresetEmail()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User)
            .ShouldNotBeNull("User role should exist in migrations.");

        var presetEmail = Fake.Internet.Email();
        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1),
            Email = presetEmail
        };

        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();

        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = Fake.Internet.Email(),
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        await db.Users
            .FirstOrDefaultAsync(u => u.Email == presetEmail)
            .ShouldNotBeNull("User should be created with preset email from invite code");
    }

    [Fact]
    public async Task RegisterNewUser_ShouldCreateUserInDatabase()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();

        var firstName = "John";
        var lastName = "Doe";
        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";
        
        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createdUser = await db.Users
            .Include(u => u.Role)
            .FirstAsync(u => u.Email == email.ToLowerInvariant())
            .ShouldNotBeNull();

        createdUser.FirstName.ShouldBe(firstName);
        createdUser.LastName.ShouldBe(lastName);
        createdUser.Email.ShouldBe(email.ToLowerInvariant());
        createdUser.Role.RoleName.ShouldBe(UserRoles.User);
        createdUser.Password.ShouldNotBe(password);
        createdUser.Password.ShouldNotBeNullOrEmpty("Password should be hashed");
    }

    [Fact]
    public async Task RegisterNewUser_WithValidRequest_ShouldCreateUserWithCorrectRole()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Admin)
            .ShouldNotBeNull("Admin role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = adminRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode);
        await db.SaveChangesAsync();

        var email = Fake.Internet.Email();
        var (rsp, res) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = email,
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createdUser = await db.Users
            .Include(u => u.Role)
            .FirstAsync(u => u.Email == email.ToLowerInvariant())
            .ShouldNotBeNull();

        createdUser.Email.ShouldBe(email.ToLowerInvariant());
        createdUser.Role.RoleName.ShouldBe(UserRoles.Admin);
        createdUser.Password.ShouldNotBeNullOrEmpty("Password should be hashed");
    }
}