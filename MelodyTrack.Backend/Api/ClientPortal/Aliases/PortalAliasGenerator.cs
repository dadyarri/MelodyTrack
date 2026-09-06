using System.Security.Cryptography;
using System.Text.Json;
using MelodyTrack.Backend.Api.ClientPortal.Aliases.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Aliases.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

namespace MelodyTrack.Backend.Api.ClientPortal.Aliases;

public sealed class PortalAliasGenerator(AppDbContext db, IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _candidateProtector = dataProtectionProvider.CreateProtector("MelodyTrack.PortalAliasCandidate.v1");

    public async Task<PortalAliasCandidateResponse?> GenerateAsync(Ulid userId, GetPortalAliasCandidateRequest request, CancellationToken ct)
    {
        var epithets = await db.AliasEpithets.AsNoTracking()
            .Where(entry => entry.IsEnabled)
            .Select(entry => new AliasPart(entry.Id, entry.Value))
            .ToListAsync(ct);
        var creatures = await db.AliasCreatures.AsNoTracking()
            .Where(entry => entry.IsEnabled)
            .Select(entry => new AliasPart(entry.Id, entry.Value))
            .ToListAsync(ct);
        var bardicTerms = await db.AliasBardicTerms.AsNoTracking()
            .Where(entry => entry.IsEnabled)
            .Select(entry => new AliasPart(entry.Id, entry.Value))
            .ToListAsync(ct);

        if (epithets.Count == 0 || creatures.Count + bardicTerms.Count == 0)
        {
            return null;
        }

        var assigned = await db.Users.AsNoTracking()
            .Where(user => user.PortalAlias != null)
            .Select(user => user.PortalAlias!)
            .ToHashSetAsync(ct);

        var candidates = new List<PortalAliasCandidateResponse>();
        foreach (var epithet in epithets)
        {
            AddCandidates(candidates, assigned, userId, epithet, creatures, AliasNounKind.Creature, request);
            AddCandidates(candidates, assigned, userId, epithet, bardicTerms, AliasNounKind.BardicTerm, request);
        }

        return candidates.Count == 0 ? null : candidates[RandomNumberGenerator.GetInt32(candidates.Count)];
    }

    private void AddCandidates(
        List<PortalAliasCandidateResponse> candidates,
        HashSet<string> assigned,
        Ulid userId,
        AliasPart epithet,
        IEnumerable<AliasPart> nouns,
        AliasNounKind nounKind,
        GetPortalAliasCandidateRequest request)
    {
        foreach (var noun in nouns)
        {
            if (request.PreviousEpithetId == epithet.Id
                && request.PreviousNounKind == nounKind
                && request.PreviousNounId == noun.Id)
            {
                continue;
            }

            var value = $"{epithet.Value} {noun.Value}";
            if (assigned.Contains(value))
            {
                continue;
            }

            var payload = new CandidatePayload(userId, epithet.Id, nounKind, noun.Id, value);
            candidates.Add(new PortalAliasCandidateResponse
            {
                EpithetId = epithet.Id,
                NounKind = nounKind,
                NounId = noun.Id,
                Value = value,
                CandidateToken = _candidateProtector.Protect(JsonSerializer.Serialize(payload))
            });
        }
    }

    private sealed record AliasPart(Ulid Id, string Value);
    internal sealed record CandidatePayload(Ulid UserId, Ulid EpithetId, AliasNounKind NounKind, Ulid NounId, string Value);

    internal bool TryReadCandidate(string token, out CandidatePayload? payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<CandidatePayload>(_candidateProtector.Unprotect(token));
            return payload is not null;
        }
        catch (CryptographicException)
        {
            payload = null;
            return false;
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
    }
}
