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

public class CreateClientPortalLinkEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<GetEntityRequest>.Res<Results<Created<CreateClientPortalLinkResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, ProblemDetails>>
{
    public override void Configure()
    {
        Post("/clients/{id}/portal-link");
    }

    public override async Task<Results<Created<CreateClientPortalLinkResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, ProblemDetails>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var currentUser = await EndpointAuthUtils.GetCurrentUserContextAsync(User, db, ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.IsAnyAdmin())
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
        var persistentToken = UserUtils.CreateClientPortalToken(client.Id);

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

        if (loginLink is null)
        {
            loginLink = new ClientPortalLoginLink
            {
                Id = Ulid.NewUlid(),
                User = existingUser,
                UserId = existingUser.Id
            };

            await db.ClientPortalLoginLinks.AddAsync(loginLink, ct);
        }

        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_portal_link_created",
            EntityType = "client_portal_link",
            EntityId = loginLink.Id.ToString(),
            ActorUserId = currentUser.Id,
            ActorEmail = currentUser.Email,
            Details = $"Создана ссылка в портал для клиента {client.LastName} {client.FirstName}".Trim()
        }, ct);

        return TypedResults.Created(
            $"/clients/{client.Id}/portal-link",
            new CreateClientPortalLinkResponse
            {
                Url = UserUtils.GetClientPortalAccessUrl(persistentToken)
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
