using MelodyTrack.Backend.Api.AliasDictionaries.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.AliasDictionaries;

internal static class AliasDictionaryStore
{
    public static Task<List<AliasDictionaryEntryDto>> ListAsync(
        AppDbContext db,
        AliasDictionaryKind dictionary,
        string? search,
        CancellationToken ct)
    {
        var normalizedSearch = search?.Trim();
        return dictionary switch
        {
            AliasDictionaryKind.Epithets => ListAsync(db.AliasEpithets, normalizedSearch, ct),
            AliasDictionaryKind.Creatures => ListAsync(db.AliasCreatures, normalizedSearch, ct),
            AliasDictionaryKind.BardicTerms => ListAsync(db.AliasBardicTerms, normalizedSearch, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(dictionary))
        };
    }

    public static async Task<AliasDictionaryEntryDto> AddAsync(
        AppDbContext db,
        AliasDictionaryKind dictionary,
        string value,
        CancellationToken ct)
    {
        var id = Ulid.NewUlid();
        switch (dictionary)
        {
            case AliasDictionaryKind.Epithets:
                db.AliasEpithets.Add(new AliasEpithet { Id = id, Value = value, IsEnabled = true });
                break;
            case AliasDictionaryKind.Creatures:
                db.AliasCreatures.Add(new AliasCreature { Id = id, Value = value, IsEnabled = true });
                break;
            case AliasDictionaryKind.BardicTerms:
                db.AliasBardicTerms.Add(new AliasBardicTerm { Id = id, Value = value, IsEnabled = true });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dictionary));
        }

        await db.SaveChangesAsync(ct);
        return new AliasDictionaryEntryDto { Id = id, Value = value, IsEnabled = true };
    }

    public static async Task<bool> UpdateAsync(
        AppDbContext db,
        AliasDictionaryKind dictionary,
        Ulid id,
        string value,
        bool isEnabled,
        CancellationToken ct)
    {
        var affected = dictionary switch
        {
            AliasDictionaryKind.Epithets => await db.AliasEpithets.Where(entry => entry.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(entry => entry.Value, value).SetProperty(entry => entry.IsEnabled, isEnabled), ct),
            AliasDictionaryKind.Creatures => await db.AliasCreatures.Where(entry => entry.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(entry => entry.Value, value).SetProperty(entry => entry.IsEnabled, isEnabled), ct),
            AliasDictionaryKind.BardicTerms => await db.AliasBardicTerms.Where(entry => entry.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(entry => entry.Value, value).SetProperty(entry => entry.IsEnabled, isEnabled), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(dictionary))
        };
        return affected > 0;
    }

    public static async Task<bool> DeleteAsync(AppDbContext db, AliasDictionaryKind dictionary, Ulid id, CancellationToken ct)
    {
        var affected = dictionary switch
        {
            AliasDictionaryKind.Epithets => await db.AliasEpithets.Where(entry => entry.Id == id).ExecuteDeleteAsync(ct),
            AliasDictionaryKind.Creatures => await db.AliasCreatures.Where(entry => entry.Id == id).ExecuteDeleteAsync(ct),
            AliasDictionaryKind.BardicTerms => await db.AliasBardicTerms.Where(entry => entry.Id == id).ExecuteDeleteAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(dictionary))
        };
        return affected > 0;
    }

    private static Task<List<AliasDictionaryEntryDto>> ListAsync<T>(DbSet<T> entries, string? search, CancellationToken ct)
        where T : BaseModel
    {
        var query = entries.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(entry => EF.Functions.ILike(EF.Property<string>(entry, "Value"), $"%{search}%"));
        }

        return query.OrderBy(entry => EF.Property<string>(entry, "Value"))
            .Select(entry => new AliasDictionaryEntryDto
            {
                Id = entry.Id,
                Value = EF.Property<string>(entry, "Value"),
                IsEnabled = EF.Property<bool>(entry, "IsEnabled")
            })
            .ToListAsync(ct);
    }
}
