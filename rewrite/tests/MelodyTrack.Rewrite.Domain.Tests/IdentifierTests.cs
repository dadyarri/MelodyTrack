using System.Text.Json;
using MelodyTrack.Rewrite.Domain.Scheduling;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class IdentifierTests
{
    [Fact]
    public void Create_SameGuidInDifferentIdentifiers_KeepsTypesDistinct()
    {
        var client = new ClientId(Guid(1));
        var staff = new StaffId(Guid(1));
        Assert.Equal(client.Value, staff.Value);
        Assert.False(((object)client).Equals(staff));
    }
    [Fact]
    public void Serialize_ClientIdentifier_RoundTripsAsGuidValue()
    {
        var id = new ClientId(Guid(1));
        var json = JsonSerializer.Serialize(id);
        Assert.Equal(JsonSerializer.Serialize(id.Value), json);
        Assert.Equal(id, JsonSerializer.Deserialize<ClientId>(json));
    }
    [Fact]
    public void Parse_ClientIdentifier_RoundTripsValue() => Assert.Equal(TestData.ClientId, global::MelodyTrack.Rewrite.Domain.Scheduling.ClientId.Parse(TestData.ClientId.Value.ToString()));
    [Fact]
    public void Create_DefaultClientIdentifier_RejectsEntity() => AssertError(new IdentifierError.Empty(), Client.Create(default, Profile, [Contact(30, true)]));
    [Fact]
    public void Create_DefaultReferences_ReturnsEachInvalidField()
    {
        var result = Appointment.Create(default, default, default, default, new RoomId(System.Guid.Empty), Utc(5, 15), Duration(60));
        Assert.True(result.IsFailure);
        Assert.Equal(new[] { "id", "clientId", "serviceId", "staffId", "roomId" }, result.Error.Issues.Select(issue => issue.Field));
        Assert.All(result.Error.Issues, issue => Assert.IsType<IdentifierError.Empty>(issue.Error));
    }
}
