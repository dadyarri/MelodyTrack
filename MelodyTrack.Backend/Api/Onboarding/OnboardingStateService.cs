using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Onboarding;

public sealed class OnboardingStateService(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<UserOnboardingState> GetCurrentAsync(User user, CancellationToken ct)
    {
        var (state, changed) = await LoadCurrentAsync(user, ct);
        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }

        return state;
    }

    public async Task<UserOnboardingState> UpdateProgressAsync(
        User user,
        string? currentStep,
        string? currentPath,
        CancellationToken ct)
    {
        var (state, _) = await LoadCurrentAsync(user, ct);
        state.Status = OnboardingStatus.Active;
        state.CurrentStep = string.IsNullOrWhiteSpace(currentStep) ? state.CurrentStep : currentStep.Trim();
        state.CurrentPath = string.IsNullOrWhiteSpace(currentPath) ? state.CurrentPath : currentPath.Trim();
        state.UpdatedAtUtc = GetUtcNow();
        state.CompletedAtUtc = null;

        await db.SaveChangesAsync(ct);
        return state;
    }

    public Task<UserOnboardingState> CompleteAsync(User user, CancellationToken ct)
    {
        return SetStatusAsync(user, OnboardingStatus.Completed, ct);
    }

    public Task<UserOnboardingState> SkipAsync(User user, CancellationToken ct)
    {
        return SetStatusAsync(user, OnboardingStatus.Skipped, ct);
    }

    public async Task<UserOnboardingState> ResetAsync(User user, CancellationToken ct)
    {
        var (state, _) = await LoadCurrentAsync(user, ct);
        ResetToCurrentDefinition(state);
        await db.SaveChangesAsync(ct);
        return state;
    }

    private async Task<UserOnboardingState> SetStatusAsync(User user, OnboardingStatus status, CancellationToken ct)
    {
        var (state, _) = await LoadCurrentAsync(user, ct);
        var now = GetUtcNow();
        state.Status = status;
        state.UpdatedAtUtc = now;
        state.CompletedAtUtc = status == OnboardingStatus.Completed ? now : null;

        await db.SaveChangesAsync(ct);
        return state;
    }

    private async Task<(UserOnboardingState State, bool Changed)> LoadCurrentAsync(User user, CancellationToken ct)
    {
        await db.Entry(user).Reference(item => item.OnboardingState).LoadAsync(ct);
        if (user.OnboardingState is null)
        {
            var createdState = OnboardingDefaults.CreateState(user, timeProvider);
            user.OnboardingState = createdState;
            return (createdState, true);
        }

        if (user.OnboardingState.DefinitionVersion < OnboardingDefaults.CurrentDefinitionVersion)
        {
            ResetToCurrentDefinition(user.OnboardingState);
            return (user.OnboardingState, true);
        }

        return (user.OnboardingState, false);
    }

    private void ResetToCurrentDefinition(UserOnboardingState state)
    {
        state.Status = OnboardingStatus.Active;
        state.CurrentStep = OnboardingDefaults.InitialStep;
        state.CurrentPath = OnboardingDefaults.InitialPath;
        state.DefinitionVersion = OnboardingDefaults.CurrentDefinitionVersion;
        state.UpdatedAtUtc = GetUtcNow();
        state.CompletedAtUtc = null;
    }

    private DateTime GetUtcNow()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }
}
