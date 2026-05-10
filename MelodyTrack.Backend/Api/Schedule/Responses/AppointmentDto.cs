using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Api.Schedule.Responses;

public class AppointmentDto
{
    public required Ulid Id { get; set; }
    public required AppointmentClientDto Client { get; set; }
    public required AppointmentServiceDto Service { get; set; }
    public AppointmentProviderDto? Provider { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required bool IsCompleted { get; set; }
    public required bool IsCanceled { get; set; }
    public AppointmentRecurrenceRuleDto? RecurringRule { get; set; }

    public static AppointmentDto FromModel(Appointment appointment)
    {
        var contacts = appointment.Client.Contacts;
        var provider = appointment.Provider;
        var recurringRule = appointment.RecurringRule;

        return new AppointmentDto
        {
            Id = appointment.Id,
            Client = new AppointmentClientDto
            {
                Id = appointment.Client.Id,
                FirstName = appointment.Client.FirstName,
                LastName = appointment.Client.LastName,
                Patronymic = appointment.Client.Patronymic,
                Contacts = contacts is null
                    ? null
                    : new AppointmentClientContactsDto
                    {
                        Id = contacts.Id,
                        Phone = contacts.Phone,
                        Telegram = contacts.Telegram,
                        Vk = contacts.Vk
                    }
            },
            Service = new AppointmentServiceDto
            {
                Id = appointment.Service.Id,
                Name = appointment.Service.Name
            },
            Provider = provider is null
                ? null
                : new AppointmentProviderDto
                {
                    Id = provider.Id,
                    FirstName = provider.FirstName,
                    LastName = provider.LastName,
                    RoleDisplayName = provider.Role.DisplayName
                },
            StartDate = appointment.StartDate,
            EndDate = appointment.EndDate,
            IsCompleted = appointment.IsCompleted,
            IsCanceled = appointment.IsCanceled,
            RecurringRule = recurringRule is null
                ? null
                : new AppointmentRecurrenceRuleDto
                {
                    Id = recurringRule.Id,
                    StartDate = recurringRule.StartDate,
                    EndDate = recurringRule.EndDate,
                    Key = recurringRule.RecurrenceType.Type switch
                    {
                        AppointmentRecurrenceType.Daily => "daily",
                        AppointmentRecurrenceType.Weekly => "weekly",
                        _ => "monthly"
                    },
                    RecurrencePattern = recurringRule.RecurrencePattern
                }
        };
    }
}

public class AppointmentClientDto
{
    public required Ulid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Patronymic { get; set; }
    public AppointmentClientContactsDto? Contacts { get; set; }
}

public class AppointmentClientContactsDto
{
    public Ulid? Id { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public string? Phone { get; set; }
}

public class AppointmentServiceDto
{
    public required Ulid Id { get; set; }
    public required string Name { get; set; }
}

public class AppointmentProviderDto
{
    public required Ulid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string RoleDisplayName { get; set; }
}

public class AppointmentRecurrenceRuleDto
{
    public required Ulid Id { get; set; }
    public required DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public required string Key { get; set; }
    public int? RecurrencePattern { get; set; }
}
