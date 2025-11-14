using System.Net;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Auth.Endpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
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
            Email = email,
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

    [Fact]
    public async Task CheckIf2FaEnabled_ReturnsTrue_WhenTotpSecretSet()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Two",
            LastName = "FA",
            Role = userRole,
            Password = "hash",
            TotpSecret = "JBSWY3DPEHPK3PXP" // any base32
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await app.Client.GETAsync<CheckIf2FaEnabledEndpoint, CheckIf2FaEnabledRequest, CheckIf2FaEnabledResponse>(new CheckIf2FaEnabledRequest
        {
            Email = user.Email
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateInvite_WithInvalidRole_Fails()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);

        // create an admin user to call invite endpoint (endpoint requires auth)
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Inv",
            LastName = "Caller",
            Role = role,
            Password = "hash"
        };
        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(caller);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await app.Client.POSTAsync<CreateInviteEndpoint, CreateInviteRequest, ProblemDetails>(new CreateInviteRequest
        {
            Email = "a@b.com",
            Role = Ulid.NewUlid()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldBeNull();
    }

    [Fact]
    public async Task CreateInvite_WithValidRole_Succeeds()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);

        // create an admin user to call invite endpoint (endpoint requires auth)
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Inv",
            LastName = "Caller",
            Role = role,
            Password = "hash"
        };
        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(caller);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await app.Client.POSTAsync<CreateInviteEndpoint, CreateInviteRequest, CreateInviteResponse>(new CreateInviteRequest
        {
            Email = "invitee@example.com",
            Role = role.Id
        });

        // clear auth header to avoid leaking to other tests
        app.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.Url.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ForgotPassword_CreatesRestorationRequest()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = Fake.Internet.Email();

        var (rsp, _) = await app.Client.POSTAsync<ForgotPasswordEndpoint, ForgotPasswordRequest, object?>(new ForgotPasswordRequest
        {
            Email = email
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var req = await db.PasswordRestorationRequests.FirstOrDefaultAsync(r => r.Email == email, TestContext.Current.CancellationToken);
        req.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetInviteCodeInformation_ReturnsForValidCode()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);

        var invite = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = role,
            ValidUntil = DateTime.UtcNow.AddDays(1),
            Email = "preset@example.com"
        };

        await db.InviteCodes.AddAsync(invite, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await app.Client.GETAsync<GetInviteCodeInformationEndpoint, GetInviteCodeInformationRequest, GetInviteCodeInformationResponse>(new GetInviteCodeInformationRequest
        {
            InviteCode = invite.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Email.ShouldBe(invite.Email);
    }

    [Fact]
    public async Task GetInviteCodeInformation_InvalidCode_Forbids()
    {
        var (rsp, res) = await app.Client.GETAsync<GetInviteCodeInformationEndpoint, GetInviteCodeInformationRequest, ProblemDetails>(new GetInviteCodeInformationRequest
        {
            InviteCode = "invalid"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldBeNull();
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_Succeeds()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Ref",
            LastName = "Resh",
            Role = role,
            Password = "hash"
        };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = "refresh-token-123",
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = ""
        };
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // include User-Agent header
        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (rsp, res) = await app.Client.POSTAsync<RefreshEndpoint, RefreshRequest, LoginResponse>(new RefreshRequest
        {
            RefreshToken = session.RefreshToken
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.AccessToken.ShouldNotBeNullOrEmpty();
        res.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Unauthorized()
    {
        var (rsp, res) = await app.Client.POSTAsync<RefreshEndpoint, RefreshRequest, ProblemDetails>(new RefreshRequest
        {
            RefreshToken = "nope"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldBeNull();
    }

    [Fact]
    public async Task RecoveryCodes_GeneratesCodes_ForAuthenticatedUser()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Rec",
            LastName = "User",
            Role = role,
            Password = "hash"
        };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // set auth header using access token
        var token = UserUtils.CreateAccessToken(user);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await app.Client.POSTAsync<RecoveryCodesEndpoint, EmptyRequest, RecoveryCodesResponse>(new EmptyRequest());

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Codes.ShouldNotBeEmpty();

        var codesInDb = await db.RecoveryCodes.Where(rc => rc.User == user && !rc.WasUsed).ToListAsync(TestContext.Current.CancellationToken);
        codesInDb.ShouldNotBeNull();
        codesInDb.Count.ShouldBe(res.Codes.Count);
    }

    [Fact]
    public async Task Recover2Fa_WithValidRecoveryCode_Succeeds()
    {
        var db = app.Services.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "R2",
            LastName = "Fa",
            Role = role,
            Password = "hash"
        };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);

        var recovery = new RecoveryCode { Id = Ulid.NewUlid(), Code = "RECOVERCODE123", User = user };
        await db.RecoveryCodes.AddAsync(recovery, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        app.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (rsp, res) = await app.Client.POSTAsync<Recover2FaEndpoint, Recover2FaRequest, Recover2FaResponse>(new Recover2FaRequest
        {
            Email = user.Email,
            RecoveryCode = recovery.Code
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.AccessToken.ShouldNotBeNullOrEmpty();
        res.RefreshToken.ShouldNotBeNullOrEmpty();
        res.Secret.ShouldNotBeNull();

        using var scope = app.Services.CreateScope();
        var assertionDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var used = await assertionDb.RecoveryCodes.FirstOrDefaultAsync(rc => rc.Id == recovery.Id, TestContext.Current.CancellationToken);
        used.ShouldNotBeNull();
        used.WasUsed.ShouldBeTrue();
    }

    [Fact]
    public async Task Setup2Fa_WithValidPassword_ReturnsSecret()
    {
        var db = app.Services.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var email = Fake.Internet.Email().ToLowerInvariant();
        var password = "PlainPass1!";

        // create user with hashed password using utils
        UserUtils.HashPassword(password, out var hash);
        var user = new User { Id = Ulid.NewUlid(), Email = email, FirstName = "S2", LastName = "Fa", Role = role, Password = hash };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await app.Client.POSTAsync<Setup2FaEndpoint, Setup2FaRequest, Setup2FaResponse>(new Setup2FaRequest
        {
            Password = password
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Secret.ShouldNotBeNull();
        res.OtpUrl.ShouldNotBeNull();
    }

    [Fact]
    public async Task Remove2Fa_ForRegularUser_RemovesSecret()
    {
        var db = app.Services.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var email = Fake.Internet.Email().ToLowerInvariant();
        var user = new User { Id = Ulid.NewUlid(), Email = email, FirstName = "Rem", LastName = "Fa", Role = role, Password = "hash", TotpSecret = "secret" };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (rsp, _) = await app.Client.DELETEAsync<Remove2FaEndpoint, EmptyRequest, NoContent>(new EmptyRequest());
        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = app.Services.CreateScope();
        var assertionDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await assertionDb.Users.FirstOrDefaultAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);
        updated.ShouldNotBeNull();
        updated.TotpSecret.ShouldBeNull();
    }

    [Fact]
    public async Task Remove2Fa_ForAdmin_Forbidden()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken);
        var email = Fake.Internet.Email().ToLowerInvariant();
        var user = new User { Id = Ulid.NewUlid(), Email = email, FirstName = "Rem", LastName = "Fa", Role = adminRole, Password = "hash", TotpSecret = "secret" };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await app.Client.DELETEAsync<Remove2FaEndpoint, EmptyRequest, ProblemDetails>(new EmptyRequest());
        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldBeNull();
    }

    [Fact]
    public async Task Logout_And_LogoutAll_RevokeSessions()
    {
        var dbContextForSetup = app.Services.GetRequiredService<AppDbContext>();

        var role = await dbContextForSetup.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "L", LastName = "O", Role = role, Password = "hash" };
        await dbContextForSetup.Users.AddAsync(user, TestContext.Current.CancellationToken);

        var session1 = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = "r1",
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = ""
        };
        var session2 = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = "r2",
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = ""
        };
        await dbContextForSetup.Sessions.AddRangeAsync(session1, session2);
        await dbContextForSetup.SaveChangesAsync(TestContext.Current.CancellationToken); // DB has s1, s2, user, role

        var token = UserUtils.CreateAccessToken(user);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // logout single session r1
        var (rspLogout, _) = await app.Client.POSTAsync<LogoutEndpoint, LogoutRequest, EmptyResponse>(new LogoutRequest { RefreshToken = session1.RefreshToken });
        rspLogout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var assertionScope = app.Services.CreateScope())
        {
            var dbContextForAssertion = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s1 = await dbContextForAssertion.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session1.Id, TestContext.Current.CancellationToken);
            s1.ShouldNotBeNull();
            s1.WasRevoked.ShouldBeTrue();
        }

        var (rspAll, _) = await app.Client.POSTAsync<LogoutAllEndpoint, EmptyRequest, NoContent>(new EmptyRequest());
        rspAll.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var assertionScope = app.Services.CreateScope())
        {
            var dbContextForAssertion = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s2 = await dbContextForAssertion.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session2.Id, TestContext.Current.CancellationToken);
            s2.ShouldNotBeNull();
            s2.WasRevoked.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task GetSessions_ReturnsActiveSessions_ForAuthenticatedUser()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "G", LastName = "S", Role = role, Password = "hash" };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);

        var session = new Session { Id = Ulid.NewUlid(), User = user, RefreshToken = "r-active", ValidUntil = DateTime.UtcNow.AddDays(1), DeviceInfo = "dev" };
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user);
        app.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await app.Client.GETAsync<GetSessionsEndpoint, EmptyRequest, GetSessionsResponse>(new EmptyRequest());
        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Data.ShouldContain(d => d.Id == session.Id && d.DeviceInfo == session.DeviceInfo);
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