# Coverage Analysis - MelodyTrack.Backend

| Metric | Value |
|--------|-------|
| **Date** | 2026-07-27 |
| **Authored-code line coverage** | 84.2% |
| **Authored-code branch coverage** | 63.9% |
| **Risk hotspots** | 51 (CRAP > 30) |
| **Tests** | 329 passed · 0 failed |

## Summary

The collector excludes generated `obj/` sources and EF migration scaffolding through `coverage.runsettings`. Migration execution tests still ran as part of the complete integration suite. Compared with the original risk inventory, branch coverage increased from 54.1% to 63.9% and CRAP hotspots fell from 75 to 51. The authored-code-only line percentage is not directly comparable to the former aggregate that included generated files.

The remediated hotspot families no longer appear in the top risk list: appointment update no-op detection and orchestration, appointment analytics aggregation, client update comparison/listing, recurring/custom transition cancellation, client creation/debt export, user availability, reference-book creation, and destructive client/payment/course operations.

## Remaining risk hotspots

| Rank | Method | File | Complexity | Coverage | CRAP |
|------|--------|------|------------|----------|------|
| 1 | `CreateServiceEndpoint.MoveNext` | `Api/Services/Endpoints/CreateServiceEndpoint.cs` | 18 | 0.0% | 342.00 |
| 2 | `RecurringTaskPresentationMapper.MapCustomTaskExecution` | `Services/RecurringTasks/RecurringTaskPresentationMapper.cs` | 16 | 0.0% | 272.00 |
| 3 | `RecurringTaskTypeExtensions.TryParseApiKey` | `Data/Enums/RecurringTaskType.cs` | 34 | 42.1% | 258.32 |
| 4 | `SetLeadStatusEndpoint.MoveNext` | `Api/Clients/Endpoints/SetLeadStatusEndpoint.cs` | 14 | 0.0% | 210.00 |
| 5 | `ExportExpensesEndpoint.MoveNext` | `Api/Expenses/Endpoints/ExportExpensesEndpoint.cs` | 12 | 0.0% | 156.00 |
| 6 | `GetClientAnalyticsEndpoint.ResolveRfmSegment` | `Api/Dashboard/Endpoints/GetClientAnalyticsEndpoint.cs` | 30 | 52.4% | 127.18 |
| 7 | `UpdateCourseEnrollmentThemeProgressEndpoint.MoveNext` | `Api/CourseEnrollments/Endpoints/UpdateCourseEnrollmentThemeProgressEndpoint.cs` | 42 | 64.9% | 117.97 |
| 8 | `UpdateExpenseEndpoint.IsNoOp` | `Api/Expenses/Endpoints/UpdateExpenseEndpoint.cs` | 10 | 0.0% | 110.00 |
| 9 | `ApiErrorResponseFactory.GetDefaultDetail` | `ErrorHandling/ApiErrorResponseFactory.cs` | 23 | 46.7% | 103.25 |
| 10 | `CreateAppointmentEndpoint.MoveNext` | `Api/Schedule/Endpoints/CreateAppointmentEndpoint.cs` | 36 | 65.8% | 87.79 |

These are the next candidates for later risk-driven testing; they are outside the remediated appointment-update, session/portal, and destructive-transition groups.

## Reports

| Report | Path |
|--------|------|
| Markdown summary | `MelodyTrack.Backend.Tests/TestResults/coverage-analysis/coverage-analysis.md` |
| Final raw Cobertura | `MelodyTrack.Backend.Tests/TestResults/stage9-complete/5e405ee1-ab0b-4d22-88cd-5776b79bf5db/coverage.cobertura.xml` |
