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
using OtpNet;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class AuthTests(MelodyTrackFixture app) : TestBase<MelodyTrackFixture>
{
    [Fact]
    public async Task RegisterNewSuperUser_WithValidRequest_Success()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles.FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCodeEntity = await db.InviteCodes
            .AsNoTracking()
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Role == superuserRole && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow, TestContext.Current.CancellationToken)
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

    [Fact]
    public async Task Login_NewSuperuser_WithOtp_Succeeds()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (regRsp, regRes) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "super",
            LastName = "user",
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        regRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        regRes.ShouldNotBeNull();
        regRes.TotpRequired.ShouldBeTrue();
        regRes.Secret.ShouldNotBeNull();

        var createdUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), TestContext.Current.CancellationToken);
        createdUser.ShouldNotBeNull();

        var secretBytes = Base32Encoding.ToBytes(regRes.Secret);
        var totp = new Totp(secretBytes, mode: OtpHashMode.Sha512);
        var otp = totp.ComputeTotp();

        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await app.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password,
            Otp = otp
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        loginRes.ShouldNotBeNull();
        loginRes.AccessToken.ShouldNotBeNullOrEmpty();
        loginRes.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_NewAdmin_WithOtp_Succeeds()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Admin role should exist in migrations.");

        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = adminRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (regRsp, regRes) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "admin",
            LastName = "user",
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        regRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        regRes.ShouldNotBeNull();
        regRes.TotpRequired.ShouldBeTrue();
        regRes.Secret.ShouldNotBeNull();

        var createdUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), TestContext.Current.CancellationToken);
        createdUser.ShouldNotBeNull();

        var secretBytes = Base32Encoding.ToBytes(regRes.Secret);
        var totp = new Totp(secretBytes, mode: OtpHashMode.Sha512);
        var otp = totp.ComputeTotp();

        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await app.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password,
            Otp = otp
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        loginRes.ShouldNotBeNull();
        loginRes.AccessToken.ShouldNotBeNullOrEmpty();
        loginRes.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_NewUser_WithoutOtp_Succeeds()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (regRsp, regRes) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "regular",
            LastName = "user",
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        regRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        regRes.ShouldNotBeNull();
        regRes.TotpRequired.ShouldBeFalse();

        // set valid User-Agent header required by LoginEndpoint for device info
        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await app.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        loginRes.ShouldNotBeNull();
        loginRes.AccessToken.ShouldNotBeNullOrEmpty();
        loginRes.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_Admin_WithoutOtp_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Admin role should exist in migrations.");

        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = adminRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (regRsp, regRes) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "admin",
            LastName = "nootp",
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        regRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        regRes.ShouldNotBeNull();
        regRes.TotpRequired.ShouldBeTrue();

        // set valid User-Agent header required by LoginEndpoint for device info
        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.IntegrationTests/1.0 (CI)");

        var (loginRsp, loginRes) = await app.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        loginRes.ShouldBeNull();
    }

    [Fact]
    public async Task Login_Superuser_WithoutOtp_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (regRsp, regRes) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "super",
            LastName = "nootp",
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        regRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        regRes.ShouldNotBeNull();
        regRes.TotpRequired.ShouldBeTrue();

        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await app.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        loginRes.ShouldBeNull();
    }

    [Fact]
    public async Task Login_WrongEmail_And_WrongPassword_Fail_WithSameError()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";

        var (regRsp, regRes) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "user",
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        regRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        regRes.ShouldNotBeNull();

        // set valid User-Agent header required by LoginEndpoint for device info
        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.IntegrationTests/1.0 (CI)");

        var wrongEmail = $"no-user-{Ulid.NewUlid()}@example.com";

        var (rspWrongEmail, resWrongEmail) = await app.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = wrongEmail,
            Password = password
        });

        var (rspWrongPassword, resWrongPassword) = await app.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = "incorrect-password"
        });

        rspWrongEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        rspWrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        resWrongEmail.ShouldBeNull();
        resWrongPassword.ShouldBeNull();
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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken).ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1),
            WasUsed = true
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken).ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCode1 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode1, TestContext.Current.CancellationToken);

        var inviteCode2 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode2, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken).ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken).ShouldNotBeNull("Admin role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = adminRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddSeconds(-1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(u => u.Email == "test.user@example.com", TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User should be created with lowercase email");
    }

    [Fact]
    public async Task RegisterNewUser_WithDuplicateEmailDifferentCase_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode1 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode1, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var inviteCode2 = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode2, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
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

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            .FirstOrDefaultAsync(u => u.Email == presetEmail, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User should be created with preset email from invite code");
    }

    [Fact]
    public async Task RegisterNewUser_ShouldCreateUserInDatabase()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = userRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var firstName = "John";
        var lastName = "Doe";
        var email = Fake.Internet.Email();
        var password = "cOmp1exP@ssw0rd";

        var (rsp, _) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
            .FirstAsync(u => u.Email == email.ToLowerInvariant(), TestContext.Current.CancellationToken)
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
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken).ShouldNotBeNull("Admin role should exist in migrations.");

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = adminRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1)
        };

        await db.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var email = Fake.Internet.Email();
        var (rsp, _) = await app.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
            .FirstAsync(u => u.Email == email.ToLowerInvariant(), TestContext.Current.CancellationToken).ShouldNotBeNull();

        createdUser.Email.ShouldBe(email.ToLowerInvariant());
        createdUser.Role.RoleName.ShouldBe(UserRoles.Admin);
        createdUser.Password.ShouldNotBeNullOrEmpty("Password should be hashed");
    }
}