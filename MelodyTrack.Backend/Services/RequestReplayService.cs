using System.Security.Cryptography;
using System.Text.Json;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public enum RequestReplayStatus
{
    Reserved,
    Completed
}

public sealed record RequestReplayDecision(
    RequestReplayStatus Status,
    Ulid? ReservationId = null,
    Ulid? ResponseEntityId = null);

public sealed class RequestReplayConflictException(string message) : Exception(message);

public interface IRequestReplayService
{
    string? GetReplayKey(IHeaderDictionary headers);
    Task<RequestReplayDecision> AcquireAsync<TRequest>(
        string endpoint,
        string replayKey,
        TRequest request,
        CancellationToken ct);
    Task CompleteAsync(Ulid reservationId, Ulid responseEntityId, CancellationToken ct);
}

public class RequestReplayService(
    AppDbContext db,
    TimeProvider timeProvider,
    ICurrentUserAccessor currentUserAccessor) : IRequestReplayService
{
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public string? GetReplayKey(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue("Idempotency-Key", out var replayKey))
        {
            return null;
        }

        var key = replayKey.ToString().Trim();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public async Task<RequestReplayDecision> AcquireAsync<TRequest>(
        string endpoint,
        string replayKey,
        TRequest request,
        CancellationToken ct)
    {
        var caller = await currentUserAccessor.GetAsync(ct)
            ?? throw new InvalidOperationException("An authenticated caller is required for idempotent requests.");
        var reservationId = Ulid.NewUlid();
        var reservationIdBytes = reservationId.ToByteArray();
        var callerIdBytes = caller.Id.ToByteArray();
        var fingerprint = CreateFingerprint(request);
        var createdAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var expiresBeforeUtc = createdAtUtc - Retention;

        await db.RequestReplays
            .Where(item => item.Endpoint == endpoint
                           && item.CallerId == caller.Id
                           && item.CreatedAtUtc < expiresBeforeUtc)
            .ExecuteDeleteAsync(ct);

        var rowsInserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RequestReplays" ("Id", "Endpoint", "ReplayKey", "CallerId", "RequestFingerprint", "CreatedAtUtc")
            VALUES ({reservationIdBytes}, {endpoint}, {replayKey}, {callerIdBytes}, {fingerprint}, {createdAtUtc})
            ON CONFLICT ("Endpoint", "CallerId", "ReplayKey") DO NOTHING
            """, ct);

        if (rowsInserted == 1)
        {
            return new RequestReplayDecision(RequestReplayStatus.Reserved, ReservationId: reservationId);
        }

        var existing = await db.RequestReplays
            .AsNoTracking()
            .SingleAsync(item =>
                item.Endpoint == endpoint
                && item.CallerId == caller.Id
                && item.ReplayKey == replayKey, ct);

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existing.RequestFingerprint),
                Convert.FromHexString(fingerprint)))
        {
            throw new RequestReplayConflictException(
                "Этот ключ идемпотентности уже использован для другого запроса.");
        }

        if (existing.ResponseEntityId is not { } responseEntityId)
        {
            throw new RequestReplayConflictException(
                "Запрос с этим ключом идемпотентности уже выполняется. Повторите попытку позже.");
        }

        return new RequestReplayDecision(RequestReplayStatus.Completed, ResponseEntityId: responseEntityId);
    }

    public async Task CompleteAsync(Ulid reservationId, Ulid responseEntityId, CancellationToken ct)
    {
        var rowsUpdated = await db.RequestReplays
            .Where(item => item.Id == reservationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ResponseEntityId, responseEntityId),
                ct);

        if (rowsUpdated != 1)
        {
            throw new InvalidOperationException($"Request replay reservation {reservationId} was not found.");
        }
    }

    private static string CreateFingerprint<TRequest>(TRequest request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        return Convert.ToHexString(SHA256.HashData(payload));
    }
}
