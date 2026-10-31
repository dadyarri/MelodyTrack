using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;

namespace MelodyTrack.Rewrite.Domain.Tests;

internal static class TestData
{
    internal static T Ok<T>(Result<T, ErrorList> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? string.Join(", ", result.Error.Issues.Select(issue => issue.Error.Code)) : "");
        return result.Value;
    }

    internal static void AssertError<T>(DomainError expected, Result<T, ErrorList> result)
    {
        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error == expected);
    }

    internal static Guid Guid(int value) => System.Guid.Parse($"019a2c63-8c00-7000-8000-{value:D12}");
    internal static DateTimeOffset Utc(int day, int hour) => new(2026, 1, day, hour, 0, 0, TimeSpan.Zero);
    internal static DateOnly Date(int day) => new(2026, 1, day);
    internal static LessonDuration Duration(int minutes) => Ok(LessonDuration.Create(minutes));
    internal static SequenceNumber Number(long value) => Ok(SequenceNumber.Create(value));
    internal static TimeInterval Period(DateTimeOffset start, DateTimeOffset end) => Ok(TimeInterval.Create(start, end));
    internal static ClientId ClientId => new(Guid(1));
    internal static ServiceId ServiceId => new(Guid(2));
    internal static StaffId StaffId => new(Guid(3));
    internal static RoomId RoomId => new(Guid(4));
    internal static SchedulingUsage NoUsage => new(false, false, false);
    internal static DateTimeOffset Now => Utc(1, 0);
    internal static WeeklyRule Weekly => Ok(WeeklyRule.Create([new(Weekday.Monday, Ok(StartHour.Create(18))), new(Weekday.Thursday, Ok(StartHour.Create(19)))]));

    internal static Series MakeSeries(SeriesEnd? end = null) => Ok(SeriesRules.Create(new(Guid(5)), ClientId, ServiceId, Duration(60), Date(5), end ?? new SeriesEnd.OpenEnded(), new(Weekly, StaffId, RoomId)));
    internal static Appointment MakeAppointment(int number, DateTimeOffset startsAt) => Ok(AppointmentRules.Create(new(Guid(number)), ClientId, ServiceId, StaffId, RoomId, startsAt, Duration(60)));
    internal static Appointment SeriesAppointment(int number, long sequence, DateTimeOffset startsAt) => Ok(AppointmentRules.CreateFromSeries(new(Guid(number)), MakeSeries(), Number(sequence), startsAt));
    internal static Vacation MakeVacation(int number, VacationSubject subject, DateTimeOffset start, DateTimeOffset end) => Ok(VacationRules.Create(new(Guid(number)), subject, Period(start, end), null));
    internal static LocalTimeWindow Window(int start, int end) => Ok(LocalTimeWindow.Create(new(start, 0), new(end, 0)));
    internal static AvailabilitySnapshot Available(Appointment appointment) => new([appointment.Interval], [], [], [], []);
    internal static ContactKind PhoneKind => Ok(ContactKindRules.Create(new(Guid(21)), "Телефон", ContactFormat.Phone));
    internal static ClientContact Contact(int number, bool primary) => Ok(ClientContactRules.Create(new(Guid(number)), PhoneKind, "+44 20 1234 5678", null, primary));
    internal static ClientProfile Profile => new(Ok(PersonName.Create("Иван", "Иванов", null)), null, new(Guid(22)), "Служебные заметки");
    internal static Client MakeClient() => Ok(ClientRules.Create(ClientId, Profile, [Contact(30, true)]));
    internal static Service MakeService(decimal amount = 1000m, bool consultation = false) => Ok(ServiceRules.Create(ServiceId, "Гитара", Duration(60), consultation, Ok(ServicePrice.Create(Now, amount))));
    internal static Staff MakeStaff() => Ok(StaffRules.Create(StaffId, "Преподаватель", StaffRole.Teacher, true));
}
