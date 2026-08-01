using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class CreateClientPortalLinkEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    IPublicUrlBuilder publicUrlBuilder,
    ICurrentUserAccessor currentUserAccessor)
    : Ep.Req<GetEntityRequest>.Res<Results<Created<CreateClientPortalLinkResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, ApiProblemDetails>>
{
    public override void Configure()
    {
        Post("/clients/{id}/portal-links");
    }

    public override async Task<Results<Created<CreateClientPortalLinkResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, ApiProblemDetails>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var client = await db.Clients
            .Include(item => item.Contacts)
            .FirstOrDefaultAsync(item => item.Id == req.Id, ct);

        if (client is null)
        {
            AddError(r => r.Id, "Клиент не найден");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status404NotFound));
        }

        var desiredEmail = BuildClientPortalEmail(client);
        var hasRealEmail = !string.IsNullOrWhiteSpace(client.Contacts.Email);
        var portalToken = UserUtils.GenerateRandomString(48);

        var clientRole = await db.Roles.FirstAsync(role => role.RoleName == UserRoles.Client, ct);

        var existingUser = await db.Users
            .Include(item => item.Role)
            .Where(item => item.ClientId == client.Id || item.EmailBlindIndex == UserUtils.HashEmailBlindIndex(desiredEmail))
            .OrderByDescending(item => item.ClientId == client.Id)
            .FirstOrDefaultAsync(ct);

        if (hasRealEmail && existingUser is not null && existingUser.Role.RoleName != UserRoles.Client)
        {
            AddError(r => r.Id, "Этот email уже используется в рабочем аккаунте. Для клиента нужен отдельный email.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status409Conflict);
        }

        if (existingUser is not null && existingUser.ClientId is not null && existingUser.ClientId != client.Id)
        {
            AddError(r => r.Id, "Этот email уже привязан к другому клиентскому кабинету.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status409Conflict);
        }

        if (existingUser is null)
        {
            UserUtils.HashPassword(UserUtils.GenerateRandomString(32), out var passwordHash);

            existingUser = new User
            {
                Id = Ulid.NewUlid(),
                Email = desiredEmail,
                FirstName = client.FirstName,
                LastName = client.LastName,
                Password = passwordHash,
                Role = clientRole,
                ClientId = client.Id,
                Phone = client.Contacts.Phone,
                Telegram = client.Contacts.Telegram,
                Vk = client.Contacts.Vk
            };

            await db.Users.AddAsync(existingUser, ct);
        }
        else
        {
            existingUser.ClientId = client.Id;
            existingUser.FirstName = client.FirstName;
            existingUser.LastName = client.LastName;
            existingUser.Email = desiredEmail;
            existingUser.Phone = client.Contacts.Phone;
            existingUser.Telegram = client.Contacts.Telegram;
            existingUser.Vk = client.Contacts.Vk;
        }

        var loginLink = await db.ClientPortalLoginLinks
            .FirstOrDefaultAsync(item => item.UserId == existingUser.Id, ct);
        var isRotation = loginLink is not null;

        if (loginLink is null)
        {
            loginLink = new ClientPortalLoginLink
            {
                Id = Ulid.NewUlid(),
                User = existingUser,
                UserId = existingUser.Id,
                TokenHash = UserUtils.HashOpaqueToken(portalToken)
            };

            await db.ClientPortalLoginLinks.AddAsync(loginLink, ct);
        }
        else
        {
            await db.ClientPortalSavedIdentityReferences
                .Where(item => item.LoginLinkId == loginLink.Id)
                .ExecuteDeleteAsync(ct);
            loginLink.TokenHash = UserUtils.HashOpaqueToken(portalToken);
            loginLink.RevokedAtUtc = null;
            loginLink.FailedPinAttempts = 0;
            loginLink.LastFailedPinAttemptAtUtc = null;

            await db.Sessions
                .Where(item => item.User.Id == existingUser.Id && !item.WasRevoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.WasRevoked, true), ct);
        }

        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = isRotation ? "client_portal_link_rotated" : "client_portal_link_created",
            EntityType = "client_portal_link",
            EntityId = loginLink.Id.ToString(),
            ActorUserId = currentUser.Id,
            ActorEmail = currentUser.Email,
            Details = AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim())
        }, ct);

        return TypedResults.Created(
            $"/clients/{client.Id}/portal-links",
            new CreateClientPortalLinkResponse
            {
                Url = publicUrlBuilder.GetClientPortalAccessUrl(portalToken)
            });
    }

    private static string BuildClientPortalEmail(Client client)
    {
        if (!string.IsNullOrWhiteSpace(client.Contacts.Email))
        {
            return UserUtils.NormalizeEmail(client.Contacts.Email);
        }

        return $"client-{client.Id}@portal.melodytrack.local";
    }
}
