using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record ExternalLink
{
    private ExternalLink(Uri value) => Value = value;
    public Uri Value { get; }
    public static Result<ExternalLink, ErrorList> Create(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" ? new ExternalLink(uri) : ErrorList.Single(new ModelError.InvalidValue(), "url");
}

public sealed record DocumentText(string Text, bool Bold = false, bool Italic = false, ExternalLink? Link = null);
public closed record DocumentBlock
{
    public sealed record Paragraph(ImmutableArray<DocumentText> Content) : DocumentBlock;
    public sealed record Heading(string Text, HeadingSize Size) : DocumentBlock;
    public sealed record Quote(ImmutableArray<DocumentText> Content) : DocumentBlock;
    public sealed record List(bool Ordered, ImmutableArray<ImmutableArray<DocumentText>> Items) : DocumentBlock;
    public sealed record Table(ImmutableArray<ImmutableArray<ImmutableArray<DocumentText>>> Rows) : DocumentBlock;
    public sealed record File(string Name, ExternalLink Url) : DocumentBlock;
    public sealed record YouTube(YouTubeClip Clip) : DocumentBlock;
    public sealed record Trainer(Exercise Exercise) : DocumentBlock;
}
public enum HeadingSize { Large, Medium, Small }

public sealed record LearningDocument(ImmutableArray<DocumentBlock> Blocks)
{
    public static LearningDocument Empty { get; } = new([]);
}

public sealed record YouTubeClip
{
    private YouTubeClip(string videoId, int? startsAtSeconds, int? endsAtSeconds)
    { VideoId = videoId; StartsAtSeconds = startsAtSeconds; EndsAtSeconds = endsAtSeconds; }
    public string VideoId { get; }
    public int? StartsAtSeconds { get; }
    public int? EndsAtSeconds { get; }

    public static Result<YouTubeClip, ErrorList> Create(string? videoId, int? startsAtSeconds, int? endsAtSeconds)
    {
        var text = Validation.Required("videoId", videoId);
        if (text.IsFailure) return text.Error;
        if (text.Value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')) ||
            startsAtSeconds < 0 || endsAtSeconds <= (startsAtSeconds ?? 0))
            return ErrorList.Single(new ModelError.InvalidValue(), "clip");
        return new YouTubeClip(text.Value, startsAtSeconds, endsAtSeconds);
    }

    public static Result<YouTubeClip, ErrorList> FromUrl(string? value)
    {
        var link = ExternalLink.Create(value);
        if (link.IsFailure) return link.Error;
        var uri = link.Value.Value;
        var query = new Dictionary<string, string>();
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            query[Uri.UnescapeDataString(parts[0])] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
        }
        var id = uri.Host.ToLowerInvariant() switch
        {
            "youtu.be" => uri.AbsolutePath.Trim('/'),
            "youtube.com" or "www.youtube.com" or "m.youtube.com" => query.GetValueOrDefault("v")
                ?? (uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal) ? uri.AbsolutePath[7..] : null),
            _ => null
        };
        var start = ParseSeconds(query.GetValueOrDefault("start") ?? query.GetValueOrDefault("t"));
        var end = ParseSeconds(query.GetValueOrDefault("end"));
        var validation = Validation.Collect(Validation.Check(start), Validation.Check(end));
        if (validation.IsFailure) return validation.Error;
        return Create(id, start.Value, end.Value);
    }

    private static Result<int?, ErrorList> ParseSeconds(string? value)
    {
        if (value is null) return (int?)null;
        if (int.TryParse(value, out var plain) && plain >= 0) return (int?)plain;
        long total = 0;
        long number = 0;
        var digits = false;
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                number = number * 10 + character - '0';
                if (number > int.MaxValue) return ErrorList.Single(new ModelError.InvalidValue(), "videoTime");
                digits = true;
                continue;
            }
            var multiplier = character switch { 'h' => 3600, 'm' => 60, 's' => 1, _ => 0 };
            if (!digits || multiplier == 0) return ErrorList.Single(new ModelError.InvalidValue(), "videoTime");
            total += number * multiplier;
            number = 0;
            digits = false;
            if (total > int.MaxValue) return ErrorList.Single(new ModelError.InvalidValue(), "videoTime");
        }
        if (digits || total == 0 && value != "0s") return ErrorList.Single(new ModelError.InvalidValue(), "videoTime");
        return (int?)total;
    }
}
