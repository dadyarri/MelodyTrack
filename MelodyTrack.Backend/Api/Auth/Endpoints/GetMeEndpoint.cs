using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.Clients;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetMeEndpoint(AppDbContext db, IRecordActivityService recordActivityService, ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<Ok<MeResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/me");
    }

    public override async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            Logger.LogWarning("Profile request without a current user");
            return TypedResults.Unauthorized();
        }

        decimal? balance = null;
        if (user.ClientId is { } clientId)
        {
            var totalPayments = await db.Payments
                .AsNoTracking()
                .Where(payment => payment.Client.Id == clientId)
                .SumAsync(payment => (decimal?)payment.Amount, ct) ?? 0m;

            var appointments = await db.Appointments
                .AsNoTracking()
                .Where(appointment =>
                    appointment.Client.Id == clientId &&
                    (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned) &&
                    !appointment.IsDeleted)
                .Select(appointment => new
                {
                    ServiceId = appointment.Service.Id,
                    StartDate = appointment.StartDate
                })
                .ToListAsync(ct);

            var serviceIds = appointments
                .Select(appointment => appointment.ServiceId)
                .Distinct()
                .ToList();

            var priceLookup = await db.ServicePriceHistory
                .AsNoTracking()
                .Where(price => serviceIds.Contains(price.Service.Id))
                .Select(price => new
                {
                    ServiceId = price.Service.Id,
                    price.EffectiveDate,
                    price.Price
                })
                .ToListAsync(ct);

            var groupedPriceLookup = priceLookup
                .GroupBy(price => price.ServiceId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(price => new ServicePriceSnapshot(price.EffectiveDate, price.Price))
                        .ToList());

            var totalServiceCost = ClientBalanceCalculator.CalculateServiceCost(
                appointments.Select(appointment => (appointment.ServiceId, appointment.StartDate)),
                groupedPriceLookup);

            balance = totalPayments - totalServiceCost;
        }

        return TypedResults.Ok(new MeResponse
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            RoleDisplayName = user.Role.DisplayName,
            Phone = user.Phone,
            Telegram = user.Telegram,
            Vk = user.Vk,
            LastActivity = await recordActivityService.GetLatestActivityAsync("user", user.Id.ToString(), ct),
            IsAdmin = user.Role.RoleName.IsAnyAdmin(),
            IsSuperuser = user.Role.RoleName.IsSuperuser(),
            IsClientPortal = user.Role.RoleName.IsClient(),
            LinkedClientId = user.ClientId,
            Balance = balance,
            IsTwoFactorEnabled = user.TotpSecret is not null,
            IsTwoFactorRequired = user.Role.RoleName.IsAnyAdmin()
        });
    }
}
