using System.Text.Json;
using System.Text.Json.Nodes;
using MelodyTrack.Backend.Api.Courses;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class StrummingConfigurationValidationTests
{
    [Fact]
    public void Validate_CompleteConfiguration_AcceptsExercise()
    {
        Should.NotThrow(() => StrummingConfigurationValidation.Validate(CreateConfiguration().ToJsonString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-80)]
    [InlineData(29)]
    [InlineData(241)]
    public void Validate_TempoOutsideRange_RejectsExercise(int bpm)
    {
        var configuration = CreateConfiguration();
        configuration["bpm"] = bpm;

        Should.Throw<ArgumentException>(() => StrummingConfigurationValidation.Validate(configuration.ToJsonString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    [InlineData(101)]
    public void Validate_InvalidRepetitions_RejectsExercise(double repetitions)
    {
        var configuration = CreateConfiguration();
        configuration["repetitions"] = repetitions;

        Should.Throw<ArgumentException>(() => StrummingConfigurationValidation.Validate(configuration.ToJsonString()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("invalid-json")]
    public void Validate_MalformedConfiguration_ReturnsValidationError(string configuration)
    {
        Should.Throw<ArgumentException>(() => StrummingConfigurationValidation.Validate(configuration));
    }

    [Fact]
    public void Validate_UnknownVersion_RejectsExercise()
    {
        var configuration = CreateConfiguration();
        configuration["schemaVersion"] = 2;

        Should.Throw<ArgumentException>(() => StrummingConfigurationValidation.Validate(configuration.ToJsonString()));
    }

    [Fact]
    public void Validate_ShorterMovementRow_RejectsExercise()
    {
        var configuration = CreateConfiguration();
        configuration["bars"]![0]!["handMotions"]!.AsArray().RemoveAt(0);

        Should.Throw<ArgumentException>(() => StrummingConfigurationValidation.Validate(configuration.ToJsonString()));
    }

    [Fact]
    public void Validate_UnknownAction_RejectsExercise()
    {
        var configuration = CreateConfiguration();
        configuration["bars"]![0]!["strokes"]![0]!["action"] = "unknown";

        Should.Throw<ArgumentException>(() => StrummingConfigurationValidation.Validate(configuration.ToJsonString()));
    }

    [Fact]
    public void Validate_InvalidProgressionInterval_RejectsExercise()
    {
        var configuration = CreateConfiguration();
        configuration["tempoProgression"]!["every"] = 0;

        Should.Throw<ArgumentException>(() => StrummingConfigurationValidation.Validate(configuration.ToJsonString()));
    }

    private static JsonObject CreateConfiguration() => JsonSerializer.SerializeToNode(new
    {
        schemaVersion = 1,
        bpm = 80,
        timeSignature = "4/4",
        bars = new[] { new { rhythmicValue = "quarter", strokes = Enumerable.Range(0, 4).Select(_ => new { action = "down", accent = false }), handMotions = new[] { "down", "up", "down", "up" } } },
        repetitions = 4,
        loop = true,
        metronome = new { enabled = true, volume = 0.6 },
        tempoProgression = new { enabled = false, increment = 5, every = 4, unit = "repetitions" }
    })!.AsObject();
}
