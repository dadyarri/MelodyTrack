
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AppointmentTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 18, 0, 0, TimeSpan.FromHours(3));
    [Fact]
    public void Create_StandaloneAppointment_StartsScheduledWithoutSeries()
    {
        var appointment = Ok(Appointment.Create(new(Guid(20)), TestData.ClientId, TestData.ServiceId, TestData.StaffId, null, Start, Duration(45)));
        Assert.Equal(new AppointmentId(Guid(20)), appointment.Id);
        Assert.Equal(TestData.ClientId, appointment.ClientId);
        Assert.Equal(TestData.ServiceId, appointment.ServiceId);
        Assert.Equal(TestData.StaffId, appointment.StaffId);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Null(appointment.RoomId);
        Assert.Null(appointment.Series);
    }
    [Fact]
    public void Create_NonUtcOffset_NormalizesStartAndEndToUtc()
    {
        var input = new DateTimeOffset(2026, 1, 5, 16, 0, 0, TimeSpan.FromHours(1));
        var appointment = Ok(Appointment.Create(new(Guid(20)), TestData.ClientId, TestData.ServiceId, TestData.StaffId, TestData.RoomId, input, Duration(45)));
        Assert.Equal(Utc(5, 15), appointment.StartsAt);
        Assert.Equal(Utc(5, 15).AddMinutes(45), appointment.EndsAt);
        Assert.Equal(TimeSpan.Zero, appointment.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, appointment.EndsAt.Offset);
    }
    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    public void Create_AllowedDuration_ComputesEndWithoutWholeHourRestriction(int minutes)
    {
        var appointment = Ok(Appointment.Create(new(Guid(20)), TestData.ClientId, TestData.ServiceId, TestData.StaffId, null, Start, Duration(minutes)));
        Assert.Equal(Start.ToUniversalTime().AddMinutes(minutes), appointment.EndsAt);
    }
    [Theory]
    [InlineData(1L)]
    [InlineData(10000000L)]
    [InlineData(18000000000L)]
    public void Create_StartOffWholeHour_RejectsAppointment(long extraTicks) => AssertError(new AppointmentError.StartNotOnWholeHour(),
        Appointment.Create(new(Guid(20)), TestData.ClientId, TestData.ServiceId, TestData.StaffId, null, Start.AddTicks(extraTicks), Duration(45)));
    [Fact]
    public void Create_EndBeyondSupportedRange_ReturnsDomainError() => AssertError(new AppointmentError.EndOutOfRange(),
        Appointment.Create(new(Guid(20)), TestData.ClientId, TestData.ServiceId, TestData.StaffId, null, new(9999, 12, 31, 23, 0, 0, TimeSpan.Zero), Duration(120)));
    [Fact]
    public void CreateFromSeries_ValidSeries_CopiesFrozenParametersAndOriginalPosition()
    {
        var series = MakeSeries();
        var number = Number(17);
        var appointment = Ok(Appointment.CreateFromSeries(new(Guid(20)), series, number, Start));
        var membership = Assert.IsType<SeriesMembership>(appointment.Series);
        Assert.Equal(series.Id, membership.SeriesId);
        Assert.Equal(number, membership.SequenceNumber);
        Assert.Empty(membership.Overrides);
        Assert.Equal(series.ClientId, appointment.ClientId);
        Assert.Equal(series.ServiceId, appointment.ServiceId);
        Assert.Equal(series.Rule.StaffId, appointment.StaffId);
        Assert.Equal(series.Rule.RoomId, appointment.RoomId);
        Assert.Equal(series.Duration, appointment.Duration);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }
    [Fact]
    public void Create_CrossesMidnight_ComputesEndOnNextDate()
    {
        var appointment = Ok(Appointment.Create(new(Guid(20)), TestData.ClientId, TestData.ServiceId, TestData.StaffId, null, Utc(5, 23), Duration(120)));
        Assert.Equal(Utc(6, 1), appointment.EndsAt);
    }
}
