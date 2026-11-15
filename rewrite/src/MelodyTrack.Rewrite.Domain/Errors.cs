namespace MelodyTrack.Rewrite.Domain;

public closed record DomainError(string Code);

public closed record IdentifierError(string Code) : DomainError(Code)
{
    public sealed record Empty() : IdentifierError("identifier.empty");
}

public closed record TimeError(string Code) : DomainError(Code)
{
    public sealed record EmptyOrReversedInterval() : TimeError("time.empty_or_reversed_interval");
    public sealed record InvalidLocalTime() : TimeError("time.invalid_local_time");
    public sealed record AmbiguousLocalTime() : TimeError("time.ambiguous_local_time");
    public sealed record OutOfRange() : TimeError("time.out_of_range");
}

public closed record SchedulingError(string Code) : DomainError(Code)
{
    public sealed record InvalidSequenceNumber(long Value) : SchedulingError("scheduling.invalid_sequence_number");
    public sealed record InvalidLessonDuration(int Minutes) : SchedulingError("scheduling.invalid_lesson_duration");
    public sealed record InvalidStartHour(int Hour) : SchedulingError("scheduling.invalid_start_hour");
    public sealed record EmptyWeeklyRule() : SchedulingError("scheduling.empty_weekly_rule");
    public sealed record DuplicateWeekday(Weekday Weekday) : SchedulingError("scheduling.duplicate_weekday");
    public sealed record InvalidAppointmentCount(int Value) : SchedulingError("scheduling.invalid_appointment_count");
    public sealed record SeriesEndBeforeStart(DateOnly StartDate, DateOnly EndDate) : SchedulingError("scheduling.series_end_before_start");
    public sealed record SeriesFinished() : SchedulingError("scheduling.series_finished");
    public sealed record SequenceNumberExhausted() : SchedulingError("scheduling.sequence_number_exhausted");
    public sealed record InvalidCountedAppointments() : SchedulingError("scheduling.invalid_counted_appointments");
    public sealed record TimeConversionFailed(TimeError Error) : SchedulingError("scheduling.time_conversion_failed");
}

public closed record CatalogueError(string Code) : DomainError(Code)
{
    public sealed record Required(string Field) : CatalogueError("catalogue.required");
    public sealed record Archived() : CatalogueError("catalogue.archived");
    public sealed record FutureAppointmentsExist() : CatalogueError("catalogue.future_appointments_exist");
    public sealed record ActiveSeriesExist() : CatalogueError("catalogue.active_series_exist");
    public sealed record HistoricalUseExists() : CatalogueError("catalogue.historical_use_exists");
    public sealed record NegativePrice() : CatalogueError("catalogue.negative_price");
    public sealed record PriceInPast() : CatalogueError("catalogue.price_in_past");
    public sealed record DuplicatePriceDate() : CatalogueError("catalogue.duplicate_price_date");
    public sealed record SoleCourseTeacher() : CatalogueError("catalogue.sole_course_teacher");
}

public closed record ClientError(string Code) : DomainError(Code)
{
    public sealed record DuplicateContact() : ClientError("client.duplicate_contact");
    public sealed record PrimaryContactRequired(ContactKindId KindId) : ClientError("client.primary_contact_required");
    public sealed record UnknownContact() : ClientError("client.unknown_contact");
    public sealed record InvalidContactValue() : ClientError("client.invalid_contact_value");
    public sealed record ContactKindArchived() : ClientError("client.contact_kind_archived");
    public sealed record UsedContactFormat() : ClientError("client.used_contact_format");
    public sealed record ArchiveDependenciesNotMigrated() : ClientError("client.archive_dependencies_not_migrated");
}

public closed record VacationError(string Code) : DomainError(Code)
{
    public sealed record RequestAlreadyDecided() : VacationError("vacation.request_already_decided");
    public sealed record VacationDeleted() : VacationError("vacation.vacation_deleted");
    public sealed record WrongVacation() : VacationError("vacation.wrong_vacation");
    public sealed record CurrentVacationRequired() : VacationError("vacation.current_vacation_required");
}

public closed record SchoolClosureError(string Code) : DomainError(Code)
{
    public sealed record Deleted() : SchoolClosureError("school_closure.deleted");
}

public closed record WorkingScheduleError(string Code) : DomainError(Code)
{
    public sealed record InvalidState() : WorkingScheduleError("working_schedule.invalid_state");
    public sealed record Forbidden() : WorkingScheduleError("working_schedule.forbidden");
    public sealed record ConflictingAppointments() : WorkingScheduleError("working_schedule.conflicting_appointments");
    public sealed record ConflictingSeries() : WorkingScheduleError("working_schedule.conflicting_series");
    public sealed record InvalidTime(TimeError Error) : WorkingScheduleError("working_schedule.invalid_time");
}

public closed record AppointmentError(string Code) : DomainError(Code)
{
    public sealed record StartNotOnWholeHour() : AppointmentError("appointment.start_not_on_whole_hour");
    public sealed record EndOutOfRange() : AppointmentError("appointment.end_out_of_range");
    public sealed record Forbidden() : AppointmentError("appointment.forbidden");
    public sealed record OutsideEditableMonth() : AppointmentError("appointment.outside_editable_month");
    public sealed record ResultInFuture() : AppointmentError("appointment.result_in_future");
    public sealed record ClearResultFirst() : AppointmentError("appointment.clear_result_first");
    public sealed record InvalidState() : AppointmentError("appointment.invalid_state");
    public sealed record Deleted() : AppointmentError("appointment.deleted");
    public sealed record RestorationBlocked() : AppointmentError("appointment.restoration_blocked");
    public sealed record CompensationNotNeeded() : AppointmentError("appointment.compensation_not_needed");
    public sealed record ReplacementRequiresReview() : AppointmentError("appointment.replacement_requires_review");
}

public closed record BookingError(string Code) : DomainError(Code)
{
    public sealed record ClientArchived() : BookingError("booking.client_archived");
    public sealed record ServiceArchived() : BookingError("booking.service_archived");
    public sealed record StaffUnavailable() : BookingError("booking.staff_unavailable");
    public sealed record RoomArchived() : BookingError("booking.room_archived");
}

public closed record SeriesConflictError(string Code) : DomainError(Code)
{
    public sealed record Forbidden() : SeriesConflictError("series_conflict.forbidden");
    public sealed record AlreadyResolved() : SeriesConflictError("series_conflict.already_resolved");
    public sealed record WrongAppointment() : SeriesConflictError("series_conflict.wrong_appointment");
    public sealed record StillBlocked() : SeriesConflictError("series_conflict.still_blocked");
}

