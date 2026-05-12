using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IRequestReplayService
{
    string? GetReplayKey(IHeaderDictionary headers);
    Task<Ulid?> TryGetResponseEntityIdAsync(string endpoint, string replayKey, CancellationToken ct);
    Task<RequestReplay> ReserveAsync(string endpoint, string replayKey, CancellationToken ct);
    Task CompleteAsync(RequestReplay replay, Ulid responseEntityId, CancellationToken ct);
    Task<Ulid?> WaitForResponseEntityIdAsync(string endpoint, string replayKey, CancellationToken ct);
}

public class RequestReplayService(AppDbContext db) : IRequestReplayService
{
    public string? GetReplayKey(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue("Idempotency-Key", out var replayKey))
        {
            return null;
        }

        var key = replayKey.ToString().Trim();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public Task<Ulid?> TryGetResponseEntityIdAsync(string endpoint, string replayKey, CancellationToken ct)
    {
        return db.RequestReplays
            .AsNoTracking()
            .Where(item => item.Endpoint == endpoint && item.ReplayKey == replayKey)
            .Select(item => item.ResponseEntityId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RequestReplay> ReserveAsync(string endpoint, string replayKey, CancellationToken ct)
    {
        var replay = new RequestReplay
        {
            Id = Ulid.NewUlid(),
            Endpoint = endpoint,
            ReplayKey = replayKey,
            CreatedAtUtc = DateTime.UtcNow
        };

        await db.RequestReplays.AddAsync(replay, ct);
        await db.SaveChangesAsync(ct);
        return replay;
    }

    public async Task CompleteAsync(RequestReplay replay, Ulid responseEntityId, CancellationToken ct)
    {
        replay.ResponseEntityId = responseEntityId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Ulid?> WaitForResponseEntityIdAsync(string endpoint, string replayKey, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var responseEntityId = await TryGetResponseEntityIdAsync(endpoint, replayKey, ct);
            if (responseEntityId is not null)
            {
                return responseEntityId;
            }

            await Task.Delay(100, ct);
        }

        return null;
    }
}
