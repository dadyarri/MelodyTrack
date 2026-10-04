using System.Text.Json;
using MelodyTrack.Backend.Api.Courses;
using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Data.Models;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class CourseThemeDocumentPersistenceTests
{
    [Fact]
    public void Apply_NewExercise_RewritesDraftReferenceAndStoresConfigurationSeparately()
    {
        var theme = CreateTheme();
        const string document = """
            {"schemaVersion":1,"document":{"type":"doc","content":[{"type":"practiceExercise","attrs":{"exerciseId":"draft-one"}}]}}
            """;

        CourseThemeDocumentPersistence.Apply(theme, document, null,
        [
            new CourseThemePracticeExerciseRequest
            {
                Reference = "draft-one",
                Type = "strumming",
                Configuration = CreateConfiguration(80)
            }
        ]);

        theme.PracticeExercises.Count.ShouldBe(1);
        theme.PracticeExercises[0].Configuration.ShouldBe(CreateConfiguration(80));
        using var saved = JsonDocument.Parse(theme.LessonContent!);
        saved.RootElement.GetProperty("document").GetProperty("content")[0].GetProperty("attrs")
            .GetProperty("exerciseId").GetString().ShouldBe(theme.PracticeExercises[0].Id.ToString());
        theme.LessonContent!.ShouldNotContain("bpm");
    }

    [Fact]
    public void Apply_RemovedNode_RemovesOwnedExercise()
    {
        var theme = CreateTheme();
        theme.PracticeExercises.Add(new CourseThemePracticeExercise
        {
            Id = Ulid.NewUlid(),
            CourseTheme = theme,
            CourseThemeId = theme.Id,
            Type = "strumming",
            Configuration = "{}"
        });

        CourseThemeDocumentPersistence.Apply(theme, null, "{\"schemaVersion\":1,\"document\":{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}}", []);

        theme.PracticeExercises.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_UnsupportedNode_RejectsDocument()
    {
        var theme = CreateTheme();

        var exception = Should.Throw<ArgumentException>(() => CourseThemeDocumentPersistence.Apply(
            theme,
            null,
            "{\"schemaVersion\":1,\"document\":{\"type\":\"doc\",\"content\":[{\"type\":\"image\"}]}}",
            []));

        exception.Message.ShouldBe("Документ содержит неподдерживаемый узел.");
    }

    [Fact]
    public void Validate_NonDocumentRoot_RejectsDocument()
    {
        var exception = Should.Throw<ArgumentException>(() => CourseThemeDocumentPersistence.Validate(
            null,
            "{\"schemaVersion\":1,\"document\":{\"type\":\"paragraph\"}}",
            []));

        exception.Message.ShouldBe("Корневой узел документа должен иметь тип doc.");
    }

    [Fact]
    public void Validate_NonObjectChild_RejectsDocument()
    {
        var exception = Should.Throw<ArgumentException>(() => CourseThemeDocumentPersistence.Validate(
            "{\"schemaVersion\":1,\"document\":{\"type\":\"doc\",\"content\":[null]}}",
            null,
            []));

        exception.Message.ShouldBe("Документ содержит некорректное содержимое узла.");
    }

    [Fact]
    public void Apply_ExercisesInLessonAndHomework_RetainsBothOwnedExercises()
    {
        var theme = CreateTheme();
        const string lesson = "{\"schemaVersion\":1,\"document\":{\"type\":\"doc\",\"content\":[{\"type\":\"practiceExercise\",\"attrs\":{\"exerciseId\":\"draft-lesson\"}}]}}";
        const string homework = "{\"schemaVersion\":1,\"document\":{\"type\":\"doc\",\"content\":[{\"type\":\"practiceExercise\",\"attrs\":{\"exerciseId\":\"draft-homework\"}}]}}";

        CourseThemeDocumentPersistence.Apply(theme, lesson, homework,
        [
            new CourseThemePracticeExerciseRequest { Reference = "draft-lesson", Type = "strumming", Configuration = CreateConfiguration(80) },
            new CourseThemePracticeExerciseRequest { Reference = "draft-homework", Type = "strumming", Configuration = CreateConfiguration(100) }
        ]);

        theme.PracticeExercises.Count.ShouldBe(2);
        theme.LessonContent!.ShouldNotContain("bpm");
        theme.HomeworkDocument!.ShouldNotContain("bpm");
    }

    private static string CreateConfiguration(int bpm)
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            bpm,
            timeSignature = "4/4",
            bars = new[] { new { rhythmicValue = "quarter", strokes = Enumerable.Range(0, 4).Select(_ => new { action = "down", accent = false }), handMotions = new[] { "down", "up", "down", "up" } } },
            repetitions = 4,
            loop = true,
            metronome = new { enabled = true, volume = 0.6 },
            tempoProgression = new { enabled = false, increment = 5, every = 4, unit = "repetitions" }
        });
    }

    private static CourseTheme CreateTheme()
    {
        return new CourseTheme
        {
            Id = Ulid.NewUlid(),
            BranchId = Ulid.NewUlid(),
            Branch = null!,
            Key = "theme",
            Title = "Тема",
            Order = 1,
            ExperiencePointsReward = 0
        };
    }
}
