using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Auth.Endpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.Clients.Endpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Endpoints;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Api.Expenses.Responses;
using MelodyTrack.Backend.Api.Payments.Endpoints;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Api.Payments.Responses;
using MelodyTrack.Backend.Api.Roles.Endpoints;
using MelodyTrack.Backend.Api.Roles.Responses;
using MelodyTrack.Backend.Api.Schedule.Endpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Api.Users.Endpoints;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class AuthTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    private static string HashRefreshToken(string token) => UserUtils.HashOpaqueToken(token);

    [Fact]
    public async Task RegisterNewSuperUser_WithValidRequest_Success()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles.FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var inviteCodeEntity = await db.InviteCodes
            .AsNoTracking()
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Role == superuserRole && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Superuser invite code should be created on startup (or by migrations).");

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
        using var scope = App.Services.CreateScope();
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

        var (regRsp, regRes) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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

        var createdUser = await db.Users.WhereEmailMatches(email).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        createdUser.ShouldNotBeNull();

        var secretBytes = Base32Encoding.ToBytes(regRes.Secret);
        var totp = new Totp(secretBytes, mode: OtpHashMode.Sha512);
        var otp = totp.ComputeTotp();

        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
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
        using var scope = App.Services.CreateScope();
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

        var (regRsp, regRes) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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

        var createdUser = await db.Users.WhereEmailMatches(email).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        createdUser.ShouldNotBeNull();

        var secretBytes = Base32Encoding.ToBytes(regRes.Secret);
        var totp = new Totp(secretBytes, mode: OtpHashMode.Sha512);
        var otp = totp.ComputeTotp();

        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
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
        using var scope = App.Services.CreateScope();
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

        var (regRsp, regRes) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
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
    public async Task Login_Admin_WithoutOtp_ReturnsSecondFactorChallenge()
    {
        using var scope = App.Services.CreateScope();
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

        var (regRsp, regRes) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.IntegrationTests/1.0 (CI)");

        var (loginRsp, loginRes) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginChallengeResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        loginRes.ShouldNotBeNull();
        loginRes.RequiresTwoFactor.ShouldBeTrue();
        loginRes.CanUseOtp.ShouldBeTrue();
        loginRes.CanUseRecoveryCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Login_Superuser_WithoutOtp_ReturnsSecondFactorChallenge()
    {
        using var scope = App.Services.CreateScope();
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

        var (regRsp, regRes) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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

        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginChallengeResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        loginRes.ShouldNotBeNull();
        loginRes.RequiresTwoFactor.ShouldBeTrue();
        loginRes.CanUseOtp.ShouldBeTrue();
        loginRes.CanUseRecoveryCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Login_WrongEmail_And_WrongPassword_Fail_WithSameError()
    {
        using var scope = App.Services.CreateScope();
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

        var (regRsp, regRes) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.IntegrationTests/1.0 (CI)");

        var wrongEmail = $"no-user-{Ulid.NewUlid()}@example.com";

        var (rspWrongEmail, resWrongEmail) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = wrongEmail,
            Password = password
        });

        var (rspWrongPassword, resWrongPassword) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = "incorrect-password"
        });

        rspWrongEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        rspWrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        resWrongEmail.ShouldNotBeNull();
        resWrongPassword.ShouldNotBeNull();
        resWrongEmail.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        resWrongPassword.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        resWrongEmail.Detail.ShouldBe("Для выполнения этого запроса нужно войти в систему.");
        resWrongPassword.Detail.ShouldBe("Для выполнения этого запроса нужно войти в систему.");
    }

    [Fact]
    public async Task Login_TooManyAttempts_ReturnsTooManyRequests()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        UserUtils.HashPassword("cOmp1exP@ssw0rd", out var passwordHash);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Rate",
            LastName = "Limited",
            Password = passwordHash,
            Role = userRole!
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var rateLimitedClient = App.CreateClient();
        rateLimitedClient.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");
        rateLimitedClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await rateLimitedClient.PostAsJsonAsync("/auth/login", new LoginRequest
            {
                Email = user.Email,
                Password = "wrong-password"
            }, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var throttledResponse = await rateLimitedClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Email = user.Email,
            Password = "wrong-password"
        }, TestContext.Current.CancellationToken);

        throttledResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_WithBothOtpAndRecoveryCode_ReturnsBadRequest()
    {
        var (rsp, res) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = "admin@example.com",
            Password = "Password1!",
            Otp = "123456",
            RecoveryCode = "ABCDEFGHIJ"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        res.ShouldNotBeNull();
        res.Detail.ShouldNotBeNull();
        res.Detail.ShouldContain("либо код 2FA, либо код восстановления");
    }

    [Fact]
    public async Task Refresh_WithMalformedToken_ReturnsBadRequest()
    {
        var (rsp, res) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, ProblemDetails>(new RefreshRequest
        {
            RefreshToken = ""
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        res.ShouldNotBeNull();
        res.Detail.ShouldNotBeNull();
        res.Detail.ShouldContain("Refresh token");
    }

    [Fact]
    public async Task ResetPassword_WithMalformedToken_ReturnsBadRequest()
    {
        var (rsp, res) = await App.Client.POSTAsync<ResetPasswordEndpoint, ResetPasswordRequest, ProblemDetails>(new ResetPasswordRequest
        {
            Token = "",
            NewPassword = "Password1!"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        res.ShouldNotBeNull();
        res.Detail.ShouldNotBeNull();
        res.Detail.ShouldContain("Токен восстановления");
    }

    [Fact]
    public async Task Login_WithUsedRecoveryCode_Unauthorized()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        UserUtils.HashPassword("cOmp1exP@ssw0rd", out var hash);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Reuse",
            LastName = "Blocked",
            Password = hash,
            Role = userRole!,
            TotpSecret = UserUtils.GenerateTotp(Fake.Internet.Email()).Secret
        };

        var recoveryCode = new RecoveryCode
        {
            Id = Ulid.NewUlid(),
            User = user,
            Code = "USEDLOGINRC",
            WasUsed = true
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.RecoveryCodes.AddAsync(recoveryCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, ProblemDetails>(new LoginRequest
        {
            Email = user.Email,
            Password = "cOmp1exP@ssw0rd",
            RecoveryCode = recoveryCode.Code
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        res.Detail.ShouldBe("Для выполнения этого запроса нужно войти в систему.");
    }

    [Fact]
    public async Task Verify2Fa_AnonymousCannotBindNewSecretToExistingUser()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Victim",
            LastName = "User",
            Password = "hash",
            Role = userRole!
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = null;

        var (attackerSecret, _) = UserUtils.GenerateTotp(user.Email);
        var attackerOtp = new Totp(Base32Encoding.ToBytes(attackerSecret), mode: OtpHashMode.Sha1).ComputeTotp();

        var (rsp, res) = await App.Client.POSTAsync<Verify2FaEndpoint, Verify2FaRequest, ProblemDetails>(new Verify2FaRequest
        {
            Email = user.Email,
            Otp = attackerOtp,
            OtpSecret = attackerSecret
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        res.Detail.ShouldBe("Для выполнения этого запроса нужно войти в систему.");

        using var assertionScope = App.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unchangedUser = await assertionDb.Users.FirstAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);
        unchangedUser.TotpSecret.ShouldBeNull();
    }

    [Fact]
    public async Task CreateInvite_WithInvalidRole_Fails()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken);

        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Inv",
            LastName = "Caller",
            Role = adminRole,
            Password = "hash"
        };
        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(caller);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await App.Client.POSTAsync<CreateInviteEndpoint, CreateInviteRequest, ProblemDetails>(new CreateInviteRequest
        {
            Email = "a@b.com",
            Role = Ulid.NewUlid()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("У вас нет прав для выполнения этого действия.");
    }

    [Fact]
    public async Task CreateInvite_WithValidRole_Succeeds()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var adminRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken);

        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Inv",
            LastName = "Caller",
            Role = adminRole,
            Password = "hash"
        };
        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(caller);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await App.Client.POSTAsync<CreateInviteEndpoint, CreateInviteRequest, CreateInviteResponse>(new CreateInviteRequest
        {
            Email = "invitee@example.com",
            Role = role.Id
        });

        // clear auth header to avoid leaking to other tests
        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.Url.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateInvite_AsRegularUser_Forbids()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Regular",
            LastName = "Caller",
            Role = userRole,
            Password = "hash"
        };

        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.POSTAsync<CreateInviteEndpoint, CreateInviteRequest, ProblemDetails>(new CreateInviteRequest
        {
            Email = "invitee@example.com",
            Role = userRole.Id
        });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("У вас нет прав для выполнения этого действия.");
    }

    [Fact]
    public async Task CreateInvite_AdminCannotCreateSuperuserInvite()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken);
        var superuserRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken);
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Admin",
            LastName = "Caller",
            Role = adminRole,
            Password = "hash"
        };

        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.POSTAsync<CreateInviteEndpoint, CreateInviteRequest, ProblemDetails>(new CreateInviteRequest
        {
            Email = "super@example.com",
            Role = superuserRole.Id
        });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("У вас нет прав для выполнения этого действия.");
    }

    [Fact]
    public async Task CreateInvite_SuperuserCanCreateSuperuserInvite()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken);
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Super",
            LastName = "Caller",
            Role = superuserRole,
            Password = "hash"
        };

        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.POSTAsync<CreateInviteEndpoint, CreateInviteRequest, CreateInviteResponse>(new CreateInviteRequest
        {
            Email = "super@example.com",
            Role = superuserRole.Id
        });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.Url.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task LookupRoles_AsRegularUser_Forbids()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Regular",
            LastName = "Lookup",
            Role = userRole,
            Password = "hash"
        };

        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.GETAsync<LookupRolesEndpoint, EmptyRequest, ProblemDetails>(EmptyRequest.Instance);

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("У вас нет прав для выполнения этого действия.");
    }

    [Fact]
    public async Task LookupRoles_AsAdmin_DoesNotExposeSuperuserRole()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken);
        var superuserRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken);
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Admin",
            LastName = "Lookup",
            Role = adminRole,
            Password = "hash"
        };

        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.GETAsync<LookupRolesEndpoint, EmptyRequest, LookupRolesResponse>(EmptyRequest.Instance);

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Roles.ShouldNotContain(role => role.Id == superuserRole.Id);
    }

    [Fact]
    public async Task LookupRoles_AsSuperuser_ExposesSuperuserRole()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken);
        var caller = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Super",
            LastName = "Lookup",
            Role = superuserRole,
            Password = "hash"
        };

        await db.Users.AddAsync(caller, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.GETAsync<LookupRolesEndpoint, EmptyRequest, LookupRolesResponse>(EmptyRequest.Instance);

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Roles.ShouldContain(role => role.Id == superuserRole.Id);
    }

    [Fact]
    public async Task CreatePasswordResetLink_WithAdminAccess_CreatesHashedResetRequest()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken);
        var userRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var admin = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Reset",
            LastName = "Admin",
            Role = adminRole,
            Password = "hash"
        };
        var target = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Reset",
            LastName = "Target",
            Role = userRole,
            Password = "hash"
        };

        await db.Users.AddRangeAsync([admin, target], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var (rsp, res) = await App.Client.POSTAsync<CreatePasswordResetLinkEndpoint, GetEntityRequest, CreatePasswordResetLinkResponse>(
            new GetEntityRequest
            {
                Id = target.Id
            });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();
        res.Url.ShouldContain("/restore?code=");

        var rawToken = res.Url.Split("code=").Last();
        var request = await db.PasswordRestorationRequests
            .OrderByDescending(r => r.ValidUntil)
            .FirstOrDefaultAsync(r => r.Email == target.Email && !r.WasUsed, TestContext.Current.CancellationToken);

        request.ShouldNotBeNull();
        request.Token.ShouldBe(UserUtils.HashOpaqueToken(rawToken));
        request.Token.ShouldNotBe(rawToken);
    }

    [Fact]
    public async Task GetInviteCodeInformation_ReturnsForValidCode()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.GETAsync<GetInviteCodeInformationEndpoint, GetInviteCodeInformationRequest, GetInviteCodeInformationResponse>(new GetInviteCodeInformationRequest
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
        var (rsp, res) = await App.Client.GETAsync<GetInviteCodeInformationEndpoint, GetInviteCodeInformationRequest, ProblemDetails>(new GetInviteCodeInformationRequest
        {
            InviteCode = "invalid"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("Ссылка приглашения недействительна. Попросите администратора создать новую.");
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_Succeeds()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string rawRefreshToken = "refresh-token-123";

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
            RefreshToken = HashRefreshToken(rawRefreshToken),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = ""
        };
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // include User-Agent header
        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (rsp, res) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, LoginResponse>(new RefreshRequest
        {
            RefreshToken = rawRefreshToken
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.AccessToken.ShouldNotBeNullOrEmpty();
        res.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Unauthorized()
    {
        var (rsp, res) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, ProblemDetails>(new RefreshRequest
        {
            RefreshToken = "nope"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        res.Detail.ShouldBe("Для выполнения этого запроса нужно войти в систему.");
    }

    [Fact]
    public async Task Refresh_WithPlaintextTokenStoredInDatabase_Unauthorized()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string rawRefreshToken = "legacy-plaintext-token";

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Legacy",
            LastName = "Session",
            Role = role,
            Password = "hash"
        };

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = rawRefreshToken,
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = "legacy-device"
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, ProblemDetails>(new RefreshRequest
        {
            RefreshToken = rawRefreshToken
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_Unauthorized_AndRevokesExpiredSession()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string rawRefreshToken = "expired-refresh-token";

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Expired",
            LastName = "Refresh",
            Role = role,
            Password = "hash"
        };

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = HashRefreshToken(rawRefreshToken),
            ValidUntil = DateTime.UtcNow.AddMinutes(-5),
            DeviceInfo = "old-device"
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, ProblemDetails>(new RefreshRequest
        {
            RefreshToken = rawRefreshToken
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldNotBeNull();

        using var assertionScope = App.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expiredSession = await assertionDb.Sessions.FirstAsync(s => s.Id == session.Id, TestContext.Current.CancellationToken);
        expiredSession.WasRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_WithReplayedRevokedToken_RevokesAllUserSessions()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string revokedRefreshToken = "replayed-token";
        const string activeRefreshToken = "still-active-token";

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Replay",
            LastName = "Victim",
            Role = role,
            Password = "hash"
        };

        var revokedSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = HashRefreshToken(revokedRefreshToken),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            WasRevoked = true,
            DeviceInfo = "device-a"
        };

        var activeSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = HashRefreshToken(activeRefreshToken),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = "device-b"
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddRangeAsync([revokedSession, activeSession], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, ProblemDetails>(new RefreshRequest
        {
            RefreshToken = revokedRefreshToken
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldNotBeNull();

        using var assertionScope = App.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessions = await assertionDb.Sessions
            .Where(s => s.User.Id == user.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        sessions.ShouldAllBe(s => s.WasRevoked);
    }

    [Fact]
    public async Task Refresh_WithoutUserAgent_UsesFallbackDeviceInfo()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string rawRefreshToken = "refresh-without-ua";

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "No",
            LastName = "Agent",
            Role = role,
            Password = "hash"
        };

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = HashRefreshToken(rawRefreshToken),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = "old-device"
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.UserAgent.Clear();

        var (rsp, res) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, LoginResponse>(new RefreshRequest
        {
            RefreshToken = rawRefreshToken
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();

        using var assertionScope = App.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var newSession = await assertionDb.Sessions
            .FirstAsync(s => s.RefreshToken == UserUtils.HashOpaqueToken(res.RefreshToken), TestContext.Current.CancellationToken);

        newSession.DeviceInfo.ShouldBe("Неизвестное устройство");
    }

    [Fact]
    public async Task RecoveryCodes_GeneratesCodes_ForAuthenticatedUser()
    {
        using var scope = App.Services.CreateScope();
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
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await App.Client.POSTAsync<RecoveryCodesEndpoint, EmptyRequest, RecoveryCodesResponse>(EmptyRequest.Instance);

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.AllCodes.ShouldNotBeEmpty();
        res.AllCodes.ShouldAllBe(code => !code.WasUsed);

        var codesInDb = await db.RecoveryCodes.Where(rc => rc.User == user && !rc.WasUsed).ToListAsync(TestContext.Current.CancellationToken);
        codesInDb.ShouldNotBeNull();
        codesInDb.Count.ShouldBe(res.AllCodes.Count);
    }

    [Fact]
    public async Task Recover2Fa_WithValidRecoveryCode_Succeeds()
    {
        var db = App.Services.GetRequiredService<AppDbContext>();

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

        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (rsp, res) = await App.Client.POSTAsync<Recover2FaEndpoint, Recover2FaRequest, Recover2FaResponse>(new Recover2FaRequest
        {
            Email = user.Email,
            RecoveryCode = recovery.Code
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.AccessToken.ShouldNotBeNullOrEmpty();
        res.RefreshToken.ShouldNotBeNullOrEmpty();
        res.Secret.ShouldNotBeNull();
        res.AllCodes.ShouldNotBeEmpty();
        res.AllCodes.ShouldAllBe(code => !code.WasUsed);

        using var scope = App.Services.CreateScope();
        var assertionDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var used = await assertionDb.RecoveryCodes.FirstOrDefaultAsync(rc => rc.Id == recovery.Id, TestContext.Current.CancellationToken);
        used.ShouldNotBeNull();
        used.WasUsed.ShouldBeTrue();
        var replacementCodes = await assertionDb.RecoveryCodes
            .Where(rc => rc.User.Id == user.Id && !rc.WasUsed)
            .ToListAsync(TestContext.Current.CancellationToken);
        replacementCodes.Count.ShouldBe(res.AllCodes.Count);
    }

    [Fact]
    public async Task Recover2Fa_WithUsedRecoveryCode_Forbids()
    {
        var db = App.Services.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Used",
            LastName = "Recovery",
            Role = role,
            Password = "hash"
        };

        var recovery = new RecoveryCode
        {
            Id = Ulid.NewUlid(),
            Code = "USED-RECOVERY-CODE",
            User = user,
            WasUsed = true
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.RecoveryCodes.AddAsync(recovery, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await App.Client.POSTAsync<Recover2FaEndpoint, Recover2FaRequest, ProblemDetails>(new Recover2FaRequest
        {
            Email = user.Email,
            RecoveryCode = recovery.Code
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("У вас нет прав для выполнения этого действия.");
    }

    [Fact]
    public async Task Setup2Fa_WithValidPassword_ReturnsSecret()
    {
        var db = App.Services.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var email = Fake.Internet.Email().ToLowerInvariant();
        var password = "PlainPass1!";

        // create user with hashed password using utils
        UserUtils.HashPassword(password, out var hash);
        var user = new User { Id = Ulid.NewUlid(), Email = email, FirstName = "S2", LastName = "Fa", Role = role, Password = hash };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await App.Client.POSTAsync<Setup2FaEndpoint, Setup2FaRequest, Setup2FaResponse>(new Setup2FaRequest
        {
            Password = password
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Secret.ShouldNotBeNull();
        res.OtpUrl.ShouldNotBeNull();
    }

    [Fact]
    public async Task Remove2Fa_ForRegularUser_RemovesSecret_RevokesSessions_And_InvalidatesRecoveryCodes()
    {
        var db = App.Services.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var email = Fake.Internet.Email().ToLowerInvariant();
        var user = new User { Id = Ulid.NewUlid(), Email = email, FirstName = "Rem", LastName = "Fa", Role = role, Password = "hash", TotpSecret = "secret" };
        var activeSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = HashRefreshToken("remove-2fa-session"),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = "test-device"
        };
        var recoveryCode = new RecoveryCode
        {
            Id = Ulid.NewUlid(),
            User = user,
            Code = "REMOVE2FA1",
            WasUsed = false
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(activeSession, TestContext.Current.CancellationToken);
        await db.RecoveryCodes.AddAsync(recoveryCode, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user, activeSession.Id);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (rsp, _) = await App.Client.DELETEAsync<Remove2FaEndpoint, EmptyRequest, NoContent>(EmptyRequest.Instance);
        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = App.Services.CreateScope();
        var assertionDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await assertionDb.Users.FirstOrDefaultAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);
        updated.ShouldNotBeNull();
        updated.TotpSecret.ShouldBeNull();

        var updatedSession = await assertionDb.Sessions.FirstOrDefaultAsync(s => s.Id == activeSession.Id, TestContext.Current.CancellationToken);
        updatedSession.ShouldNotBeNull();
        updatedSession.WasRevoked.ShouldBeTrue();

        var updatedRecoveryCode = await assertionDb.RecoveryCodes.FirstOrDefaultAsync(rc => rc.Id == recoveryCode.Id, TestContext.Current.CancellationToken);
        updatedRecoveryCode.ShouldNotBeNull();
        updatedRecoveryCode.WasUsed.ShouldBeTrue();
    }

    [Fact]
    public async Task Remove2Fa_ForAdmin_Forbidden()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRole = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.Admin, TestContext.Current.CancellationToken);
        var email = Fake.Internet.Email().ToLowerInvariant();
        var user = new User { Id = Ulid.NewUlid(), Email = email, FirstName = "Rem", LastName = "Fa", Role = adminRole, Password = "hash", TotpSecret = "secret" };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await App.Client.DELETEAsync<Remove2FaEndpoint, EmptyRequest, ProblemDetails>(EmptyRequest.Instance);
        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("У вас нет прав для выполнения этого действия.");
    }

    [Fact]
    public async Task Logout_And_LogoutAll_RevokeSessions()
    {
        var dbContextForSetup = App.Services.GetRequiredService<AppDbContext>();
        const string rawRefreshToken1 = "r1";
        const string rawRefreshToken2 = "r2";

        var role = await dbContextForSetup.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "L", LastName = "O", Role = role, Password = "hash" };
        await dbContextForSetup.Users.AddAsync(user, TestContext.Current.CancellationToken);

        var session1 = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = HashRefreshToken(rawRefreshToken1),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = ""
        };
        var session2 = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = HashRefreshToken(rawRefreshToken2),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = ""
        };
        await dbContextForSetup.Sessions.AddRangeAsync(session1, session2);
        await dbContextForSetup.SaveChangesAsync(TestContext.Current.CancellationToken); // DB has s1, s2, user, role

        var token = UserUtils.CreateAccessToken(user);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // logout single session r1
        var (rspLogout, _) = await App.Client.POSTAsync<LogoutEndpoint, LogoutRequest, EmptyResponse>(new LogoutRequest { RefreshToken = rawRefreshToken1 });
        rspLogout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var assertionScope = App.Services.CreateScope())
        {
            var dbContextForAssertion = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s1 = await dbContextForAssertion.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session1.Id, TestContext.Current.CancellationToken);
            s1.ShouldNotBeNull();
            s1.WasRevoked.ShouldBeTrue();
        }

        var (rspAll, _) = await App.Client.POSTAsync<LogoutAllEndpoint, EmptyRequest, NoContent>(EmptyRequest.Instance);
        rspAll.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var assertionScope = App.Services.CreateScope())
        {
            var dbContextForAssertion = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s2 = await dbContextForAssertion.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session2.Id, TestContext.Current.CancellationToken);
            s2.ShouldNotBeNull();
            s2.WasRevoked.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Logout_CannotRevokeAnotherUsersSession()
    {
        var dbContextForSetup = App.Services.GetRequiredService<AppDbContext>();
        const string rawTargetRefreshToken = "foreign-refresh-token";

        var role = await dbContextForSetup.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var caller = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "Caller", LastName = "User", Role = role, Password = "hash" };
        var target = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "Target", LastName = "User", Role = role, Password = "hash" };

        var targetSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = target,
            RefreshToken = HashRefreshToken(rawTargetRefreshToken),
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = "foreign-device"
        };

        await dbContextForSetup.Users.AddRangeAsync([caller, target], TestContext.Current.CancellationToken);
        await dbContextForSetup.Sessions.AddAsync(targetSession, TestContext.Current.CancellationToken);
        await dbContextForSetup.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.POSTAsync<LogoutEndpoint, LogoutRequest, ProblemDetails>(new LogoutRequest
        {
            RefreshToken = rawTargetRefreshToken
        });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        res.Detail.ShouldBe("Для выполнения этого запроса нужно войти в систему.");

        using var assertionScope = App.Services.CreateScope();
        var dbContextForAssertion = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unchangedSession = await dbContextForAssertion.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == targetSession.Id, TestContext.Current.CancellationToken);
        unchangedSession.ShouldNotBeNull();
        unchangedSession.WasRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task GetSessions_ReturnsActiveSessions_ForAuthenticatedUser()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "G", LastName = "S", Role = role, Password = "hash" };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);

        var currentSession = new Session { Id = Ulid.NewUlid(), User = user, RefreshToken = HashRefreshToken("r-current"), ValidUntil = DateTime.UtcNow.AddDays(1), DeviceInfo = "dev-current" };
        var otherSession = new Session { Id = Ulid.NewUlid(), User = user, RefreshToken = HashRefreshToken("r-other"), ValidUntil = DateTime.UtcNow.AddDays(1), DeviceInfo = "dev-other" };

        await db.Sessions.AddRangeAsync([currentSession, otherSession], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var token = UserUtils.CreateAccessToken(user, currentSession.Id);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (rsp, res) = await App.Client.GETAsync<GetSessionsEndpoint, EmptyRequest, GetSessionsResponse>(EmptyRequest.Instance);
        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();

        res.Data.ShouldContain(d => d.Id == currentSession.Id);
        var currentSessionDto = res.Data.Single(d => d.Id == currentSession.Id);
        currentSessionDto.DeviceInfo.ShouldBe(currentSession.DeviceInfo);
        currentSessionDto.IsCurrent.ShouldBeTrue();
        currentSessionDto.LastSeenAtUtc.ShouldBe(currentSession.Id.Time.UtcDateTime, TimeSpan.FromSeconds(1));

        res.Data.ShouldContain(d => d.Id == otherSession.Id);
        var otherSessionDto = res.Data.Single(d => d.Id == otherSession.Id);
        otherSessionDto.IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task RevokeSession_RevokesOwnedSession()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "R", LastName = "S", Role = role, Password = "hash" };
        var session = new Session { Id = Ulid.NewUlid(), User = user, RefreshToken = HashRefreshToken("r-owned"), ValidUntil = DateTime.UtcNow.AddDays(1), DeviceInfo = "dev" };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user, session.Id));

        var (rsp, _) = await App.Client.DELETEAsync<RevokeSessionEndpoint, GetEntityRequest, NoContent>(new GetEntityRequest { Id = session.Id });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var assertionScope = App.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revokedSession = await assertionDb.Sessions.AsNoTracking().FirstAsync(s => s.Id == session.Id, TestContext.Current.CancellationToken);
        revokedSession.WasRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task GetMe_WithRevokedCurrentSession_Unauthorized()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "Me", LastName = "Gone", Role = role, Password = "hash" };
        var session = new Session { Id = Ulid.NewUlid(), User = user, RefreshToken = HashRefreshToken("r-me"), ValidUntil = DateTime.UtcNow.AddDays(1), DeviceInfo = "dev" };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user, session.Id));

        var (revokeRsp, _) = await App.Client.DELETEAsync<RevokeSessionEndpoint, GetEntityRequest, NoContent>(new GetEntityRequest { Id = session.Id });
        revokeRsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var meRsp = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = null;

        meRsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSessions_WithRevokedCurrentSession_Unauthorized()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var user = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "Sessions", LastName = "Gone", Role = role, Password = "hash" };
        var session = new Session { Id = Ulid.NewUlid(), User = user, RefreshToken = HashRefreshToken("r-sessions"), ValidUntil = DateTime.UtcNow.AddDays(1), DeviceInfo = "dev" };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user, session.Id));

        var (revokeRsp, _) = await App.Client.DELETEAsync<RevokeSessionEndpoint, GetEntityRequest, NoContent>(new GetEntityRequest { Id = session.Id });
        revokeRsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var sessionsRsp = await App.Client.GetAsync("/auth/sessions", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = null;

        sessionsRsp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeSession_CannotRevokeAnotherUsersSession()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.FirstAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        var caller = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "Caller", LastName = "User", Role = role, Password = "hash" };
        var target = new User { Id = Ulid.NewUlid(), Email = Fake.Internet.Email().ToLowerInvariant(), FirstName = "Target", LastName = "User", Role = role, Password = "hash" };
        var targetSession = new Session { Id = Ulid.NewUlid(), User = target, RefreshToken = HashRefreshToken("r-foreign"), ValidUntil = DateTime.UtcNow.AddDays(1), DeviceInfo = "foreign" };

        await db.Users.AddRangeAsync([caller, target], TestContext.Current.CancellationToken);
        await db.Sessions.AddAsync(targetSession, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(caller));

        var (rsp, res) = await App.Client.DELETEAsync<RevokeSessionEndpoint, GetEntityRequest, ProblemDetails>(new GetEntityRequest { Id = targetSession.Id });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.NotFound);
        res.Detail.ShouldBe("Сессия не найдена");
    }

    [Theory]
    [InlineData("1234", 3)]
    [InlineData("qwerty", 3)]
    [InlineData("rhbetnrf", 1)]
    [InlineData("1wetpussy", 2)]
    public async Task RegisterNewSuperUser_WithSimplePassword_FailsValidation(string password, int expectedAmountOfErrors)
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
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
        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = Fake.Internet.Email(),
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("Ссылка приглашения недействительна. Используйте новую ссылку от администратора.");
    }

    [Fact]
    public async Task RegisterNewSuperUser_WithUsedInviteCode_Fails()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = Fake.Internet.Email(),
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("Ссылка приглашения уже использована или просрочена. Попросите администратора создать новую.");
    }

    [Fact]
    public async Task RegisterNewSuperUser_WithDuplicatedEmail_Fails()
    {
        using var scope = App.Services.CreateScope();
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
        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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

        var (rsp2, res2) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = email,
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode2.Code.ToString()
        });

        rsp2.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res2.ShouldNotBeNull();
        res2.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res2.Detail.ShouldBe("Пользователь с таким email уже зарегистрирован. Войдите в существующий аккаунт или попросите новую ссылку.");
    }

    [Fact]
    public async Task RegisterNewRegularUser_WithValidRequest_Success()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = Fake.Internet.Email(),
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("Ссылка приглашения уже использована или просрочена. Попросите администратора создать новую.");
    }

    [Fact]
    public async Task RegisterNewUser_WithCaseInsensitiveEmail_Success()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
            .WhereEmailMatches("test.user@example.com")
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User should be created with lowercase email");
    }

    [Fact]
    public async Task RegisterNewUser_WithTrimmedAndCaseInsensitiveEmail_Success()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, _) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "  TeSt.TrimMe@ExAmPlE.CoM  ",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createdUser = await db.Users
            .WhereEmailMatches("test.trimme@example.com")
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        createdUser.ShouldNotBeNull("User should be created with trimmed lowercase email");
        createdUser.Email.ShouldBe("test.trimme@example.com");
    }

    [Fact]
    public async Task RegisterNewUser_WithDuplicateEmailDifferentCase_Fails()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "iyfcyckin@example.com",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode1.Code.ToString()
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        var (rsp2, res2) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, ProblemDetails>(new RegisterRequest
        {
            FirstName = "test",
            LastName = "test",
            Email = "IyFcyCkiN@ExAmPlE.CoM",
            Password = "cOmp1exP@ssw0rd",
            InviteCode = inviteCode2.Code.ToString()
        });

        rsp2.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res2.ShouldNotBeNull();
        res2.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res2.Detail.ShouldBe("Пользователь с таким email уже зарегистрирован. Войдите в существующий аккаунт или попросите новую ссылку.");
    }

    [Fact]
    public async Task RegisterNewUser_WithInviteCodePresetEmail_UsesPresetEmail()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, res) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
            .WhereEmailMatches(presetEmail)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User should be created with preset email from invite code");
    }

    [Fact]
    public async Task RegisterNewUser_ShouldCreateUserInDatabase()
    {
        using var scope = App.Services.CreateScope();
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

        var (rsp, _) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
            .WhereEmailMatches(email)
            .FirstAsync(TestContext.Current.CancellationToken)
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
        using var scope = App.Services.CreateScope();
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
        var (rsp, _) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
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
            .WhereEmailMatches(email)
            .FirstAsync(TestContext.Current.CancellationToken).ShouldNotBeNull();

        createdUser.Email.ShouldBe(email.ToLowerInvariant());
        createdUser.Role.RoleName.ShouldBe(UserRoles.Admin);
        createdUser.Password.ShouldNotBeNullOrEmpty("Password should be hashed");
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_Forbids_AndDoesNotChangePassword()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string rawResetToken = "EXPIREDTOKEN123";

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        UserUtils.HashPassword("old-password", out var originalHash);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Expired",
            LastName = "Reset",
            Password = originalHash,
            Role = userRole!
        };

        var resetRequest = new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = user.Email,
            Token = UserUtils.HashOpaqueToken(rawResetToken),
            ValidUntil = DateTime.UtcNow.AddMinutes(-10)
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.PasswordRestorationRequests.AddAsync(resetRequest, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await App.Client.POSTAsync<ResetPasswordEndpoint, ResetPasswordRequest, ProblemDetails>(new ResetPasswordRequest
        {
            Token = rawResetToken,
            NewPassword = "N3w-password!"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("Ссылка восстановления больше не действует. Запросите новую ссылку.");

        var unchangedUser = await db.Users.FirstAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);
        unchangedUser.Password.ShouldBe(originalHash);

        var unchangedResetRequest = await db.PasswordRestorationRequests
            .FirstAsync(r => r.Id == resetRequest.Id, TestContext.Current.CancellationToken);
        unchangedResetRequest.WasUsed.ShouldBeFalse();
    }

    [Fact]
    public async Task ResetPassword_WithPlaintextTokenStoredInDatabase_Forbids()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string rawResetToken = "LEGACY-PLAINTEXT-RESET";

        var userRole = await db.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.User, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("User role should exist in migrations.");

        UserUtils.HashPassword("old-password", out var originalHash);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = Fake.Internet.Email().ToLowerInvariant(),
            FirstName = "Legacy",
            LastName = "Reset",
            Password = originalHash,
            Role = userRole!
        };

        var resetRequest = new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = user.Email,
            Token = rawResetToken,
            ValidUntil = DateTime.UtcNow.AddHours(2)
        };

        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.PasswordRestorationRequests.AddAsync(resetRequest, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (rsp, res) = await App.Client.POSTAsync<ResetPasswordEndpoint, ResetPasswordRequest, ProblemDetails>(new ResetPasswordRequest
        {
            Token = rawResetToken,
            NewPassword = "N3w-password!"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.Forbidden);
        res.Detail.ShouldBe("Ссылка восстановления больше не действует. Запросите новую ссылку.");
    }

    [Fact]
    public async Task SuperuserFullWorkflow_CompleteJourney()
    {
        // Setup: Create invite code for superuser
        using var setupScope = App.Services.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var superuserRole = await setupDb.Roles
            .FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken)
            .ShouldNotBeNull("Superuser role should exist in migrations.");

        var email = Fake.Internet.Email();

        var inviteCode = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole!,
            ValidUntil = DateTime.UtcNow.AddDays(1),
            Email = email
        };

        await setupDb.InviteCodes.AddAsync(inviteCode, TestContext.Current.CancellationToken);
        await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        var password = "cOmp1exP@ssw0rd";
        var newPassword = "N3wP@ssw0rd!Secure";

        // Step 1: Get invite code information
        var (inviteInfoRsp, inviteInfoRes) = await App.Client.GETAsync<GetInviteCodeInformationEndpoint, GetInviteCodeInformationRequest, GetInviteCodeInformationResponse>(
            new GetInviteCodeInformationRequest
            {
                InviteCode = inviteCode.Code.ToString()
            });

        inviteInfoRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        inviteInfoRes.ShouldNotBeNull();
        inviteInfoRes.Email.ShouldBe(inviteCode.Email);

        // Step 2: Register with invite code
        var (regRsp, regRes) = await App.Client.POSTAsync<RegisterEndpoint, RegisterRequest, RegisterResponse>(new RegisterRequest
        {
            FirstName = "Super",
            LastName = "User",
            Email = email,
            Password = password,
            InviteCode = inviteCode.Code.ToString()
        });

        regRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        regRes.ShouldNotBeNull();
        regRes.TotpRequired.ShouldBeTrue("Superusers should require TOTP");
        regRes.Secret.ShouldNotBeNull();
        regRes.OtpUrl.ShouldNotBeNull();

        // Verify user was created in database
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var createdUser = await db.Users
                .Include(u => u.Role)
                .WhereEmailMatches(email)
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            createdUser.ShouldNotBeNull();
            createdUser.Role.RoleName.ShouldBe(UserRoles.Superuser);
            createdUser.TotpSecret.ShouldNotBeNull("Secret should be set already");
        }

        var totpSecret = regRes.Secret;
        var secretBytes = Base32Encoding.ToBytes(totpSecret);
        var totp = new Totp(secretBytes, mode: OtpHashMode.Sha512);
        var otp = totp.ComputeTotp();

        // Step 3: Verify 2FA
        App.Client.DefaultRequestHeaders.Authorization = null;

        var (verify2FaRsp, verify2FaRes) = await App.Client.POSTAsync<Verify2FaEndpoint, Verify2FaRequest, RecoveryCodesResponse>(new Verify2FaRequest
        {
            Email = email.ToLowerInvariant(),
            Otp = otp,
            OtpSecret = totpSecret
        });

        verify2FaRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        verify2FaRes.ShouldNotBeNull();
        verify2FaRes.AllCodes.Count.ShouldBeGreaterThan(0);
        verify2FaRes.AllCodes.ShouldAllBe(code => !code.WasUsed);

        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.WhereEmailMatches(email).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            user.ShouldNotBeNull();
            user.TotpSecret.ShouldBe(totpSecret);
        }

        // Step 4: Login with OTP (first login)
        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Backend.Tests/1.0 (CI)");

        var (loginRsp, loginRes) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password,
            Otp = otp
        });

        loginRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        loginRes.ShouldNotBeNull();
        loginRes.AccessToken.ShouldNotBeNullOrEmpty();
        loginRes.RefreshToken.ShouldNotBeNullOrEmpty();

        var accessToken = loginRes.AccessToken;
        var refreshToken = loginRes.RefreshToken;

        // Verify session was created
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.Sessions.FirstOrDefaultAsync(
                s => s.RefreshToken == UserUtils.HashOpaqueToken(refreshToken),
                TestContext.Current.CancellationToken);
            session.ShouldNotBeNull();
            session.RefreshToken.ShouldNotBe(refreshToken);
        }

        // Step 6: Generate recovery codes
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var (recoveryRsp, recoveryRes) = await App.Client.POSTAsync<RecoveryCodesEndpoint, EmptyRequest, RecoveryCodesResponse>(EmptyRequest.Instance);

        recoveryRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        recoveryRes.ShouldNotBeNull();
        recoveryRes.AllCodes.ShouldNotBeEmpty();
        recoveryRes.AllCodes.ShouldAllBe(code => !code.WasUsed);

        // Verify recovery codes were saved
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var codes = await db.RecoveryCodes
                .Where(rc => rc.User.EmailBlindIndex == UserUtils.HashEmailBlindIndex(email) && !rc.WasUsed)
                .ToListAsync(TestContext.Current.CancellationToken);
            codes.Count.ShouldBe(recoveryRes.AllCodes.Count);
        }

        // Step 7: Refresh token
        var (refreshRsp, refreshRes) = await App.Client.POSTAsync<RefreshEndpoint, RefreshRequest, LoginResponse>(new RefreshRequest
        {
            RefreshToken = refreshToken
        });

        refreshRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        refreshRes.ShouldNotBeNull();
        refreshRes.AccessToken.ShouldNotBeNullOrEmpty();
        refreshRes.RefreshToken.ShouldNotBeNullOrEmpty();

        var newAccessToken = refreshRes.AccessToken;

        // Step 8: Perform second login to create additional session
        var otp2 = totp.ComputeTotp();

        var (login2Rsp, login2Res) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = password,
            Otp = otp2
        });

        login2Rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        login2Res.ShouldNotBeNull();

        // Step 9: Get sessions using the latest active session token. The prior same-device session
        // was revoked by the second login and should no longer authorize protected requests.
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login2Res.AccessToken);

        var (sessionsRsp, sessionsRes) = await App.Client.GETAsync<GetSessionsEndpoint, EmptyRequest, GetSessionsResponse>(EmptyRequest.Instance);

        sessionsRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        sessionsRes.ShouldNotBeNull();
        // Active sessions are now deduplicated by device info, so repeated logins from the same test client
        // collapse into a single visible session entry.
        sessionsRes.Data.Count.ShouldBe(1);
        sessionsRes.Data[0].DeviceInfo.ShouldNotBeNullOrWhiteSpace();

        // Step 10: Logout from one session
        var (logoutRsp, _) = await App.Client.POSTAsync<LogoutEndpoint, LogoutRequest, EmptyResponse>(new LogoutRequest
        {
            RefreshToken = refreshToken
        });

        logoutRsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify the session was revoked
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var revokedSession = await db.Sessions.FirstOrDefaultAsync(
                s => s.RefreshToken == UserUtils.HashOpaqueToken(refreshToken),
                TestContext.Current.CancellationToken);
            revokedSession.ShouldNotBeNull();
            revokedSession.WasRevoked.ShouldBeTrue();
        }

        // Step 11: Logout all sessions
        var (logoutAllRsp, _) = await App.Client.POSTAsync<LogoutAllEndpoint, EmptyRequest, NoContent>(EmptyRequest.Instance);

        logoutAllRsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify all sessions were revoked
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allSessions = await db.Sessions
                .Where(s => s.User.EmailBlindIndex == UserUtils.HashEmailBlindIndex(email))
                .ToListAsync(TestContext.Current.CancellationToken);
            allSessions.ShouldAllBe(s => s.WasRevoked);
        }

        // Step 12: Another superuser creates a reset link for the target account
        App.Client.DefaultRequestHeaders.Authorization = null;
        string restorationToken;
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operatorRole = await db.Roles
                .FirstAsync(e => e.RoleName == UserRoles.Superuser, TestContext.Current.CancellationToken);
            var operatorUser = new User
            {
                Id = Ulid.NewUlid(),
                Email = Fake.Internet.Email().ToLowerInvariant(),
                FirstName = "Reset",
                LastName = "Operator",
                Role = operatorRole,
                Password = "hash"
            };

            await db.Users.AddAsync(operatorUser, TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            App.Client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(operatorUser));

            var (createResetRsp, createResetRes) = await App.Client.POSTAsync<CreatePasswordResetLinkEndpoint, GetEntityRequest, CreatePasswordResetLinkResponse>(
                new GetEntityRequest
                {
                    Id = await db.Users
                        .WhereEmailMatches(email)
                        .Select(u => u.Id)
                        .FirstAsync(TestContext.Current.CancellationToken)
                });

            createResetRsp.StatusCode.ShouldBe(HttpStatusCode.Created);
            createResetRes.ShouldNotBeNull();
            restorationToken = createResetRes.Url.Split("code=").Last();
            App.Client.DefaultRequestHeaders.Authorization = null;
        }

        // Step 13: Reset password (with 2FA verification)
        var newOtp = totp.ComputeTotp();

        var (resetRsp, _) = await App.Client.POSTAsync<ResetPasswordEndpoint, ResetPasswordRequest, ProblemDetails>(new ResetPasswordRequest
        {
            Token = restorationToken,
            NewPassword = newPassword,
            Otp = newOtp
        });

        resetRsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify password was changed
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.WhereEmailMatches(email).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            user.ShouldNotBeNull();
            user.Password.ShouldNotBe(password);
        }

        // Verify all sessions were revoked during password reset
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = await db.Sessions
                .Where(s => s.User.EmailBlindIndex == UserUtils.HashEmailBlindIndex(email))
                .ToListAsync(TestContext.Current.CancellationToken);
            sessions.ShouldAllBe(s => s.WasRevoked);
        }

        // Step 14: Login with new password and OTP
        var loginWithNewPasswordOtp = totp.ComputeTotp();

        var (loginNewPasswordRsp, loginNewPasswordRes) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
        {
            Email = email.ToLowerInvariant(),
            Password = newPassword,
            Otp = loginWithNewPasswordOtp
        });

        loginNewPasswordRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        loginNewPasswordRes.ShouldNotBeNull();
        loginNewPasswordRes.AccessToken.ShouldNotBeNullOrEmpty();

        // Step 15: Recover 2FA using recovery code
        var recoveryCodeToUse = recoveryRes.AllCodes.First().Code;

        var (recover2FaRsp, recover2FaRes) = await App.Client.POSTAsync<Recover2FaEndpoint, Recover2FaRequest, Recover2FaResponse>(new Recover2FaRequest
        {
            Email = email.ToLowerInvariant(),
            RecoveryCode = recoveryCodeToUse
        });

        recover2FaRsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        recover2FaRes.ShouldNotBeNull();
        recover2FaRes.AccessToken.ShouldNotBeNullOrEmpty();
        recover2FaRes.RefreshToken.ShouldNotBeNullOrEmpty();
        recover2FaRes.Secret.ShouldNotBeNull();
        recover2FaRes.AllCodes.ShouldNotBeEmpty();
        recover2FaRes.AllCodes.ShouldAllBe(code => !code.WasUsed);

        // Verify the recovery code was marked as used
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usedCode = await db.RecoveryCodes
                .FirstOrDefaultAsync(rc => rc.Code == recoveryCodeToUse, TestContext.Current.CancellationToken);
            usedCode.ShouldNotBeNull();
            usedCode.WasUsed.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task CreateRecurringAppointment_AssignsNonEmptyRecurringRuleId()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await CreateScheduleClientAsync(db, TestContext.Current.CancellationToken);
        var service = await CreateScheduleServiceAsync(db, TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Weekly, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var request = new CreateAppointmentRequest
        {
            ClientId = client.Id,
            ServiceId = service.Id,
            StartDate = new DateTime(2026, 05, 11, 12, 0, 0, DateTimeKind.Utc),
            Timezone = "UTC",
            RecurrenceTypeId = recurrenceType.Id,
            PatternEndDate = new DateTime(2026, 05, 31, 23, 59, 59, DateTimeKind.Utc),
            RecurrencePattern = 1 + 4
        };

        var (rsp, res) = await App.Client.POSTAsync<CreateAppointmentEndpoint, CreateAppointmentRequest, CreateEntityResponse>(request);

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var appointment = await db.Appointments
            .Include(item => item.RecurringRule)
            .FirstAsync(item => item.Id == res.Id, TestContext.Current.CancellationToken);

        appointment.RecurringRule.ShouldNotBeNull();
        appointment.RecurringRule!.Id.ShouldNotBe(Ulid.Empty);
    }

    [Fact]
    public async Task UpdateAppointment_AddRecurringRule_AssignsNonEmptyRecurringRuleId()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await CreateScheduleClientAsync(db, TestContext.Current.CancellationToken);
        var service = await CreateScheduleServiceAsync(db, TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Daily, TestContext.Current.CancellationToken);

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2026, 05, 12, 14, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 12, 15, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, _) = await App.Client.PATCHAsync<UpdateAppointmentEndpoint, UpdateAppointmentRequest, NoContent>(new UpdateAppointmentRequest
        {
            Id = appointment.Id,
            StartDate = appointment.StartDate,
            Timezone = "UTC",
            RecurrenceTypeId = recurrenceType.Id,
            RecurrencePattern = 1
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updatedAppointment = await db.Appointments
            .Include(item => item.RecurringRule)
            .FirstAsync(item => item.Id == appointment.Id, TestContext.Current.CancellationToken);

        updatedAppointment.RecurringRule.ShouldNotBeNull();
        updatedAppointment.RecurringRule!.Id.ShouldNotBe(Ulid.Empty);
    }

    [Fact]
    public async Task GetAppointments_ReturnsRecurringRuleForRecurringAppointment()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await CreateScheduleClientAsync(db, TestContext.Current.CancellationToken);
        var service = await CreateScheduleServiceAsync(db, TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Weekly, TestContext.Current.CancellationToken);
        var recurrenceRuleId = Ulid.NewUlid();

        var recurrenceRule = new AppointmentRecurrenceRule
        {
            Id = recurrenceRuleId,
            Client = client,
            Service = service,
            StartDate = new DateTime(2026, 05, 13, 16, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 31, 23, 59, 59, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1 + 4
        };

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = recurrenceRule.StartDate,
            EndDate = recurrenceRule.StartDate.AddHours(1),
            Status = AppointmentStatus.Planned,
            IsDeleted = false,
            RecurringRule = recurrenceRule
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetAppointmentsEndpoint, GetAppointmentsRequest, GetAppointmentsResponse>(new GetAppointmentsRequest
        {
            Timezone = "UTC",
            StartDate = new DateTime(2026, 05, 12, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 14, 23, 59, 59, DateTimeKind.Utc)
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Appointments.Count.ShouldBe(1);
        res.Appointments[0].RecurringRule.ShouldNotBeNull();
        res.Appointments[0].RecurringRule!.Id.ShouldBe(recurrenceRuleId);
        res.Appointments[0].RecurringRule!.Key.ShouldBe("weekly");
    }

    [Fact]
    public async Task GetClientHistory_ReturnsSummaryAndRecentActivity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Анна",
            LastName = "Иванова",
            Patronymic = "Сергеевна",
            CreatedAtUtc = DateTime.UtcNow,
            Contacts = new ClientContacts
            {
                Id = Ulid.NewUlid(),
                Phone = "+79991234567",
                Telegram = "https://t.me/annai",
                Vk = "https://vk.com/annai"
            }
        };

        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = "Маникюр"
        };

        var servicePrice = new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 1500m,
            EffectiveDate = DateTime.UtcNow.AddMonths(-2)
        };

        var oldPayment = new Payment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Amount = 900m,
            Date = DateTime.UtcNow.AddDays(-10),
            Description = "Предоплата"
        };

        var newPayment = new Payment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Amount = 1200m,
            Date = DateTime.UtcNow.AddDays(-2),
            Description = "Доплата"
        };

        var completedAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2024, 01, 10, 9, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2024, 01, 10, 10, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Completed,
            IsDeleted = false
        };

        var burnedAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2024, 01, 12, 9, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2024, 01, 12, 10, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Burned,
            IsDeleted = false
        };

        var upcomingAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2030, 01, 15, 11, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2030, 01, 15, 12, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Clients.AddAsync(client, TestContext.Current.CancellationToken);
        await db.Services.AddAsync(service, TestContext.Current.CancellationToken);
        await db.ServicePriceHistory.AddAsync(servicePrice, TestContext.Current.CancellationToken);
        await db.Payments.AddRangeAsync([oldPayment, newPayment], TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync([completedAppointment, burnedAppointment, upcomingAppointment], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetClientHistoryEndpoint, GetClientHistoryRequest, ClientHistoryResponse>(
            new GetClientHistoryRequest { Id = client.Id, Page = 1, PageSize = 8 });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Client.Id.ShouldBe(client.Id);
        res.Client.Balance.ShouldBe(-900m);
        res.Summary.TotalPayments.ShouldBe(2100m);
        res.Summary.PaymentsCount.ShouldBe(2);
        res.Summary.CompletedAppointmentsCount.ShouldBe(2);
        res.Summary.UpcomingAppointmentsCount.ShouldBe(1);
        res.Summary.LastPaymentAtUtc.ShouldNotBeNull();
        res.Summary.LastVisitAtUtc.ShouldNotBeNull();
        res.Summary.NextAppointmentAtUtc.ShouldNotBeNull();
        res.Summary.LastPaymentAtUtc.Value.ShouldBe(newPayment.Date, TimeSpan.FromSeconds(1));
        res.Summary.LastVisitAtUtc.Value.ShouldBe(burnedAppointment.StartDate, TimeSpan.FromSeconds(1));
        res.Summary.NextAppointmentAtUtc.Value.ShouldBe(upcomingAppointment.StartDate, TimeSpan.FromSeconds(1));
        res.RecentPayments.Select(e => e.Id).ShouldBe([newPayment.Id, oldPayment.Id]);
        res.Appointments.Info.Page.ShouldBe(1);
        res.Appointments.Info.PageSize.ShouldBe(8);
        res.Appointments.Info.Total.ShouldBe(3);
        res.Appointments.Data.Select(e => e.Id).ShouldBe([upcomingAppointment.Id, burnedAppointment.Id, completedAppointment.Id]);
        res.Appointments.Data.Select(e => e.Status).ShouldBe(["planned", "burned", "completed"]);
    }

    [Fact]
    public async Task GetClientHistory_WithUnknownId_ReturnsNotFound()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetClientHistoryEndpoint, GetEntityRequest, ProblemDetails>(
            new GetEntityRequest { Id = Ulid.NewUlid() });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        res.ShouldNotBeNull();
        res.Status.ShouldBe((int)HttpStatusCode.NotFound);
        res.Detail.ShouldBe("Клиент не найден");
    }

    [Fact]
    public async Task GetPayments_AppliesFiltersAndReturnsSummary()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var clientA = await CreateScheduleClientAsync(db, TestContext.Current.CancellationToken);
        var clientB = await CreateScheduleClientAsync(db, TestContext.Current.CancellationToken);
        clientB.LastName = "Sidorova";
        var service = await CreateScheduleServiceAsync(db, TestContext.Current.CancellationToken);

        var paymentA = new Payment
        {
            Id = Ulid.NewUlid(),
            Client = clientA,
            Service = service,
            Amount = 1200m,
            Date = new DateTime(2026, 05, 02, 10, 0, 0, DateTimeKind.Utc),
            Description = "Оплата курса"
        };

        var paymentB = new Payment
        {
            Id = Ulid.NewUlid(),
            Client = clientB,
            Amount = 700m,
            Date = new DateTime(2026, 04, 28, 10, 0, 0, DateTimeKind.Utc),
            Description = "Предоплата"
        };

        await db.Payments.AddRangeAsync([paymentA, paymentB], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetPaymentsEndpoint, GetPaymentsPaginatedRequest, GetPaymentsResponse>(
            new GetPaymentsPaginatedRequest
            {
                Page = 1,
                PageSize = 10,
                Search = "курс",
                Start = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 05, 03, 0, 0, 0, DateTimeKind.Utc)
            });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Data.Count.ShouldBe(1);
        res.Data[0].Id.ShouldBe(paymentA.Id);
        res.Info.Total.ShouldBe(1);
        res.Summary.TotalAmount.ShouldBe(1200m);
        res.Summary.ItemsCount.ShouldBe(1);
        res.Summary.FirstItemAtUtc.ShouldBe(paymentA.Date);
        res.Summary.LastItemAtUtc.ShouldBe(paymentA.Date);
    }

    [Fact]
    public async Task CreatePayment_WithoutService_Succeeds()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await CreateScheduleClientAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var request = new CreatePaymentRequest
        {
            ClientId = client.Id,
            Amount = 500m,
            Date = new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc),
            Description = "Платеж без услуги"
        };

        var (rsp, res) = await App.Client.POSTAsync<CreatePaymentEndpoint, CreatePaymentRequest, CreateEntityResponse>(request);

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var payment = await db.Payments
            .Include(e => e.Client)
            .Include(e => e.Service)
            .FirstAsync(e => e.Id == res.Id, TestContext.Current.CancellationToken);

        payment.Client.Id.ShouldBe(client.Id);
        payment.Amount.ShouldBe(500m);
        payment.Service.ShouldBeNull();
    }

    [Fact]
    public async Task GetExpenses_AppliesFiltersAndReturnsSummary()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        var expenseA = new Expense
        {
            Id = Ulid.NewUlid(),
            Description = "Аренда кабинета",
            Amount = 5000m,
            Date = new DateTime(2026, 05, 05, 8, 0, 0, DateTimeKind.Utc)
        };

        var expenseB = new Expense
        {
            Id = Ulid.NewUlid(),
            Description = "Материалы",
            Amount = 1300m,
            Date = new DateTime(2026, 04, 25, 8, 0, 0, DateTimeKind.Utc)
        };

        await db.Expenses.AddRangeAsync([expenseA, expenseB], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetExpensesEndpoint, GetExpensesPaginatedRequest, GetExpensesResponse>(
            new GetExpensesPaginatedRequest
            {
                Page = 1,
                PageSize = 10,
                Search = "аренда",
                Start = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 05, 10, 0, 0, 0, DateTimeKind.Utc)
            });

        App.Client.DefaultRequestHeaders.Authorization = null;

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.Data.Count.ShouldBe(1);
        res.Data[0].Id.ShouldBe(expenseA.Id);
        res.Info.Total.ShouldBe(1);
        res.Summary.ItemsCount.ShouldBe(1);
        res.Summary.TotalAmount.ShouldBe(5000m);
        res.Summary.FirstItemAtUtc.ShouldBe(expenseA.Date);
        res.Summary.LastItemAtUtc.ShouldBe(expenseA.Date);
    }

    private static async Task<User> CreateAuthorizedScheduleUserAsync(AppDbContext db, CancellationToken ct)
    {
        var role = await db.Roles.FirstAsync(item => item.RoleName == UserRoles.User, ct);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = $"schedule-{Ulid.NewUlid()}@example.com",
            FirstName = "Schedule",
            LastName = "Tester",
            Password = "hash",
            Role = role
        };

        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);

        return user;
    }

    private static async Task<Client> CreateScheduleClientAsync(AppDbContext db, CancellationToken ct)
    {
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Nina",
            LastName = "Petrova",
            CreatedAtUtc = DateTime.UtcNow,
            Contacts = new ClientContacts
            {
                Id = Ulid.NewUlid()
            }
        };

        await db.Clients.AddAsync(client, ct);
        await db.SaveChangesAsync(ct);

        return client;
    }

    private static async Task<Service> CreateScheduleServiceAsync(AppDbContext db, CancellationToken ct)
    {
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = $"Service-{Ulid.NewUlid()}"
        };

        await db.Services.AddAsync(service, ct);
        await db.SaveChangesAsync(ct);

        return service;
    }
}
