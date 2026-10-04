using System.Text.Json;
using System.Text.Json.Nodes;
using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Courses;

internal static class CourseThemeDocumentPersistence
{
    private const int MaximumDocumentLength = 100_000;
    private static readonly HashSet<string> AllowedNodes = ["doc", "paragraph", "text", "hardBreak", "bulletList", "orderedList", "listItem", "blockquote", "codeBlock", "practiceExercise"];
    private static readonly HashSet<string> AllowedMarks = ["bold", "italic", "underline", "strike", "link", "code"];

    public static void Apply(
        CourseTheme theme,
        string? lessonDocument,
        string? homeworkDocument,
        IEnumerable<CourseThemePracticeExerciseRequest> requests)
    {
        var requestList = requests.ToList();
        var references = new HashSet<string>(StringComparer.Ordinal);
        var lesson = ParseAndValidateDocument(lessonDocument, references);
        var homework = ParseAndValidateDocument(homeworkDocument, references);
        var requested = ValidateRequests(requestList, references);

        var existing = theme.PracticeExercises.ToDictionary(item => item.Id.ToString(), StringComparer.Ordinal);
        var retained = new List<CourseThemePracticeExercise>();
        var resolvedReferences = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            var request = requested[reference];
            if (existing.TryGetValue(reference, out var exercise))
            {
                exercise.Type = request.Type;
                exercise.Configuration = request.Configuration;
                retained.Add(exercise);
                resolvedReferences[reference] = exercise.Id.ToString();
                continue;
            }

            exercise = new CourseThemePracticeExercise
            {
                Id = Ulid.NewUlid(),
                CourseTheme = theme,
                CourseThemeId = theme.Id,
                Type = request.Type,
                Configuration = request.Configuration
            };
            retained.Add(exercise);
            resolvedReferences[reference] = exercise.Id.ToString();
        }

        theme.PracticeExercises = retained;
        theme.LessonContent = Serialize(lesson, resolvedReferences);
        theme.HomeworkDocument = Serialize(homework, resolvedReferences);
    }

    public static void Validate(
        string? lessonDocument,
        string? homeworkDocument,
        IEnumerable<CourseThemePracticeExerciseRequest> requests)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        _ = ParseAndValidateDocument(lessonDocument, references);
        _ = ParseAndValidateDocument(homeworkDocument, references);
        _ = ValidateRequests(requests.ToList(), references);
    }

    private static JsonObject? ParseAndValidateDocument(string? document, ISet<string> references)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;
        if (document.Length > MaximumDocumentLength) throw new ArgumentException("Документ темы курса слишком большой.");
        try
        {
            var envelope = JsonNode.Parse(document) as JsonObject
                ?? throw new ArgumentException("Некорректный документ темы курса.");
            if (envelope["schemaVersion"]?.GetValue<int>() != 1 || envelope["document"] is not JsonObject root)
                throw new ArgumentException("Неподдерживаемая версия документа темы курса.");
            if (root["type"]?.GetValue<string>() != "doc")
                throw new ArgumentException("Корневой узел документа должен иметь тип doc.");
            ValidateNode(root, references);
            return envelope;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Некорректный документ темы курса.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException("Некорректный документ темы курса.", exception);
        }
    }

    private static Dictionary<string, CourseThemePracticeExerciseRequest> ValidateRequests(
        IReadOnlyCollection<CourseThemePracticeExerciseRequest> requests,
        HashSet<string> references)
    {
        if (requests.Any(item => string.IsNullOrWhiteSpace(item.Reference)))
            throw new ArgumentException("У конфигурации упражнения отсутствует ссылка.");
        if (requests.GroupBy(item => item.Reference, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Конфигурация упражнения передана более одного раза.");
        var requested = requests.ToDictionary(item => item.Reference, StringComparer.Ordinal);
        if (!references.SetEquals(requested.Keys))
            throw new ArgumentException("Упражнения документов не соответствуют переданной конфигурации.");
        foreach (var request in requests) ValidateExercise(request);
        return requested;
    }

    private static void ValidateNode(JsonObject node, ISet<string> references)
    {
        var type = node["type"]?.GetValue<string>();
        if (type is null || !AllowedNodes.Contains(type)) throw new ArgumentException("Документ содержит неподдерживаемый узел.");
        if (node["marks"] is JsonArray marks && marks.Any(mark => mark is not JsonObject item || item["type"]?.GetValue<string>() is not { } markType || !AllowedMarks.Contains(markType)))
            throw new ArgumentException("Документ содержит неподдерживаемое форматирование.");
        if (type == "practiceExercise")
        {
            var reference = (node["attrs"] as JsonObject)?["exerciseId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("У упражнения отсутствует ссылка.");
            if (!references.Add(reference)) throw new ArgumentException("Упражнение может находиться в документах темы только один раз.");
        }
        if (node["content"] is not { } contentNode) return;
        if (contentNode is not JsonArray content || content.Any(child => child is not JsonObject))
            throw new ArgumentException("Документ содержит некорректное содержимое узла.");
        foreach (var child in content.Cast<JsonObject>()) ValidateNode(child, references);
    }

    private static string? Serialize(JsonObject? envelope, IReadOnlyDictionary<string, string> references)
    {
        if (envelope is null) return null;
        RewriteReferences((JsonObject)envelope["document"]!, references);
        return envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static void RewriteReferences(JsonObject node, IReadOnlyDictionary<string, string> references)
    {
        if (node["type"]?.GetValue<string>() == "practiceExercise" && node["attrs"] is JsonObject attrs)
            attrs["exerciseId"] = references[attrs["exerciseId"]!.GetValue<string>()];
        if (node["content"] is JsonArray content)
            foreach (var child in content.Cast<JsonObject>()) RewriteReferences(child, references);
    }

    private static void ValidateExercise(CourseThemePracticeExerciseRequest request)
    {
        if (request.Type is not "strumming" || request.Type.Length > 100 || string.IsNullOrWhiteSpace(request.Configuration) || request.Configuration.Length > 50_000)
            throw new ArgumentException("Некорректная конфигурация упражнения.");
        StrummingConfigurationValidation.Validate(request.Configuration);
    }
}
