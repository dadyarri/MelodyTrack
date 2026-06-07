using MelodyTrack.Backend.Api.Courses.Requests;

namespace MelodyTrack.Backend.Api.Courses.Validators;

internal static class CourseStructureValidation
{
    public static void ValidateBlocks(List<CreateCourseBlockRequest>? blocks, Action<string, string> addFailure)
    {
        blocks ??= [];

        if (blocks.Select(block => block.Order).Distinct().Count() != blocks.Count)
        {
            addFailure("Blocks", "Порядок блоков должен быть уникальным.");
        }

        foreach (var block in blocks)
        {
            var branches = block.Branches ?? [];

            if (branches.Select(branch => branch.Order).Distinct().Count() != branches.Count)
            {
                addFailure("Blocks", $"Порядок веток в блоке \"{block.Title}\" должен быть уникальным.");
            }

            foreach (var branch in branches)
            {
                var themes = branch.Themes ?? [];

                if (themes.Select(theme => theme.Order).Distinct().Count() != themes.Count)
                {
                    addFailure("Blocks", $"Порядок тем в ветке \"{branch.Title}\" должен быть уникальным.");
                }
            }
        }

        var allThemes = blocks
            .SelectMany(block => block.Branches ?? [])
            .SelectMany(branch => branch.Themes ?? [])
            .ToList();

        var duplicateKeys = allThemes
            .GroupBy(theme => theme.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var duplicateKey in duplicateKeys)
        {
            addFailure("Blocks", $"Ключ темы \"{duplicateKey}\" должен быть уникальным.");
        }

        var themeKeys = allThemes
            .Select(theme => theme.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var themeTitleByKey = allThemes
            .GroupBy(theme => theme.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().Title) ? group.Key : group.First().Title,
                StringComparer.OrdinalIgnoreCase);

        var dependenciesByThemeKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var theme in allThemes)
        {
            dependenciesByThemeKey[theme.Key] = [];

            foreach (var dependencyKey in theme.DependencyKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!themeKeys.Contains(dependencyKey))
                {
                    addFailure("Blocks", $"Тема \"{theme.Title}\" ссылается на неизвестную зависимость \"{dependencyKey}\".");
                    continue;
                }

                if (string.Equals(theme.Key, dependencyKey, StringComparison.OrdinalIgnoreCase))
                {
                    addFailure("Blocks", $"Тема \"{theme.Title}\" не может зависеть сама от себя.");
                    continue;
                }

                dependenciesByThemeKey[theme.Key].Add(dependencyKey);
            }
        }

        if (HasCycle(dependenciesByThemeKey, out var cycleStartKey))
        {
            var cycleThemeLabel = themeTitleByKey.GetValueOrDefault(cycleStartKey, cycleStartKey);
            addFailure("Blocks", $"Обнаружена циклическая зависимость тем. Проверьте тему \"{cycleThemeLabel}\".");
        }
    }

    private static bool HasCycle(IReadOnlyDictionary<string, List<string>> graph, out string cycleStartKey)
    {
        cycleStartKey = string.Empty;
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Keys)
        {
            if (DetectCycle(node, graph, visiting, visited, out cycleStartKey))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DetectCycle(
        string node,
        IReadOnlyDictionary<string, List<string>> graph,
        HashSet<string> visiting,
        HashSet<string> visited,
        out string cycleStartKey)
    {
        cycleStartKey = string.Empty;

        if (visited.Contains(node))
        {
            return false;
        }

        if (!visiting.Add(node))
        {
            cycleStartKey = node;
            return true;
        }

        foreach (var dependency in graph.GetValueOrDefault(node, []))
        {
            if (DetectCycle(dependency, graph, visiting, visited, out cycleStartKey))
            {
                return true;
            }
        }

        visiting.Remove(node);
        visited.Add(node);
        return false;
    }
}
