using System.Text.Json;

namespace MelodyTrack.Backend.Api.Courses;

internal static class StrummingConfigurationValidation
{
    private static readonly HashSet<string> Actions = ["down", "up", "down-click", "down-dead-notes", "up-dead-notes", "down-bass-strings", "up-bass-strings", "down-treble-strings", "up-treble-strings", "rest"];
    private static readonly HashSet<string> Motions = ["down", "up", "muted", "rest", "down-up", "up-down"];

    public static void Validate(string configuration)
    {
        try
        {
            using var document = JsonDocument.Parse(configuration);
            if (!IsValid(document.RootElement)) throw new ArgumentException("Некорректная конфигурация гитарного боя. Проверьте версию, темп и ритмический рисунок.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Некорректная конфигурация гитарного боя: ожидается JSON.", exception);
        }
    }

    private static bool IsValid(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !Number(root, "schemaVersion", 1, 1, true) ||
            !Number(root, "bpm", 30, 240) || !Number(root, "repetitions", 1, 100, true) || !Boolean(root, "loop")) return false;
        if (!root.TryGetProperty("timeSignature", out var signature) || signature.ValueKind != JsonValueKind.String) return false;
        var quarterNotes = signature.GetString() switch { "2/4" => 2, "3/4" => 3, "4/4" => 4, "6/8" => 3, _ => 0 };
        if (quarterNotes == 0 || !root.TryGetProperty("bars", out var bars) || bars.ValueKind != JsonValueKind.Array || bars.GetArrayLength() is < 1 or > 16) return false;
        foreach (var bar in bars.EnumerateArray())
        {
            if (bar.ValueKind != JsonValueKind.Object || !bar.TryGetProperty("rhythmicValue", out var value) || value.ValueKind != JsonValueKind.String) return false;
            var multiplier = value.GetString() switch { "quarter" => 1, "eighth" => 2, "sixteenth" => 4, _ => 0 };
            if (multiplier == 0 || signature.GetString() == "6/8" && multiplier == 1) return false;
            var slots = quarterNotes * multiplier;
            if (!bar.TryGetProperty("strokes", out var strokes) || strokes.ValueKind != JsonValueKind.Array || strokes.GetArrayLength() != slots ||
                !bar.TryGetProperty("handMotions", out var motions) || motions.ValueKind != JsonValueKind.Array || motions.GetArrayLength() != slots) return false;
            foreach (var stroke in strokes.EnumerateArray())
            {
                if (stroke.ValueKind != JsonValueKind.Object || !StringIn(stroke, "action", Actions) || !Boolean(stroke, "accent")) return false;
                if (stroke.GetProperty("action").GetString() == "rest" && stroke.GetProperty("accent").GetBoolean()) return false;
            }
            if (motions.EnumerateArray().Any(motion => motion.ValueKind != JsonValueKind.String || !Motions.Contains(motion.GetString()!))) return false;
        }
        if (!root.TryGetProperty("metronome", out var metronome) || !Boolean(metronome, "enabled") || !Number(metronome, "volume", 0, 1)) return false;
        return root.TryGetProperty("tempoProgression", out var progression) && Boolean(progression, "enabled") &&
            Number(progression, "increment", 1, 60, true) && Number(progression, "every", 1, 100, true) && StringIn(progression, "unit", ["bars", "repetitions"]);
    }

    private static bool Boolean(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False;
    private static bool StringIn(JsonElement parent, string name, HashSet<string> allowed) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString()!);
    private static bool Number(JsonElement parent, string name, double minimum, double maximum, bool integer = false) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum && number <= maximum && (!integer || number == Math.Truncate(number));
}
