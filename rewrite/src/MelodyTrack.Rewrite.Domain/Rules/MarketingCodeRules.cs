using System.Security.Cryptography;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class MarketingCodeRules
{
    public static string Normalize(string code) => code.ToUpperInvariant();

    public static bool Matches(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.Ordinal);

    public static Result<string, ErrorList> Generate(IEnumerable<string> existingCodes, string alphabet)
    {
        if (string.IsNullOrEmpty(alphabet) || alphabet.Select(character => Normalize(character.ToString())).Distinct(StringComparer.Ordinal).Count() != alphabet.Length || !alphabet.All(char.IsLetterOrDigit))
            return ErrorList.Single(new ModelError.InvalidValue(), "alphabet");
        var used = existingCodes.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var positions = Enumerable.Range(0, 5).Select(_ => RandomNumberGenerator.GetInt32(alphabet.Length)).ToArray();
        var start = positions.ToArray();
        // At most used.Count candidates can be occupied; do not rely on luck or retry forever.
        for (long offset = 0; offset <= used.Count; offset++)
        {
            var code = new string(positions.Select(position => alphabet[position]).ToArray());
            if (!used.Contains(Normalize(code))) return code;
            for (var position = positions.Length - 1; position >= 0; position--)
            {
                positions[position] = (positions[position] + 1) % alphabet.Length;
                if (positions[position] != 0) break;
            }
            if (positions.SequenceEqual(start)) break;
        }
        return ErrorList.Single(new ModelError.InvalidState(), "code");
    }
}
