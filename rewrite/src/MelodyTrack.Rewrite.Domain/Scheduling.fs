namespace MelodyTrack.Rewrite.Domain.Scheduling

open System
open MelodyTrack.Rewrite.Domain

[<RequireQualifiedAccess>]
type Weekday =
    | Monday
    | Tuesday
    | Wednesday
    | Thursday
    | Friday
    | Saturday
    | Sunday

[<RequireQualifiedAccess>]
type SchedulingError =
    | InvalidSequenceNumber of value: int64
    | InvalidLessonDuration of minutes: int
    | InvalidStartHour of hour: int
    | EmptyWeeklyRule
    | DuplicateWeekday of weekday: Weekday
    | InvalidAppointmentCount of value: int
    | SeriesEndBeforeStart of startDate: DateOnly * endDate: DateOnly

[<Struct>]
type SequenceNumber = private SequenceNumber of int64

[<RequireQualifiedAccess>]
module SequenceNumber =
    let create value =
        if value <= 0L then
            Error(SchedulingError.InvalidSequenceNumber value)
        else
            Ok(SequenceNumber value)

    let first = SequenceNumber 1L

    let value (SequenceNumber value) = value

[<Struct>]
type LessonDuration = private LessonDuration of int

[<RequireQualifiedAccess>]
module LessonDuration =
    let create minutes =
        match minutes with
        | 30
        | 45
        | 60
        | 90
        | 120 -> Ok(LessonDuration minutes)
        | _ -> Error(SchedulingError.InvalidLessonDuration minutes)

    let minutes (LessonDuration minutes) = minutes

    let toTimeSpan duration = TimeSpan.FromMinutes(float (minutes duration))

[<Struct>]
type StartHour = private StartHour of int

[<RequireQualifiedAccess>]
module StartHour =
    let create hour =
        if hour < 0 || hour > 23 then
            Error(SchedulingError.InvalidStartHour hour)
        else
            Ok(StartHour hour)

    let value (StartHour hour) = hour

    let toTimeOnly startHour = TimeOnly(value startHour, 0)

type WeeklySlot =
    { Weekday: Weekday
      StartHour: StartHour }

type WeeklyRule = private WeeklyRule of WeeklySlot list

[<RequireQualifiedAccess>]
module WeeklyRule =
    let create slots =
        let slots = Seq.toList slots

        match slots with
        | [] -> Error SchedulingError.EmptyWeeklyRule
        | _ ->
            let duplicate =
                slots
                |> List.countBy (fun slot -> slot.Weekday)
                |> List.tryFind (fun (_, count) -> count > 1)

            match duplicate with
            | Some(weekday, _) -> Error(SchedulingError.DuplicateWeekday weekday)
            | None -> slots |> List.sortBy (fun slot -> slot.Weekday) |> WeeklyRule |> Ok

    let slots (WeeklyRule slots) = slots

[<Struct>]
type AppointmentCount = private AppointmentCount of int

[<RequireQualifiedAccess>]
module AppointmentCount =
    let create value =
        if value <= 0 then
            Error(SchedulingError.InvalidAppointmentCount value)
        else
            Ok(AppointmentCount value)

    let value (AppointmentCount value) = value

[<RequireQualifiedAccess>]
type SeriesEnd =
    | OnDateInclusive of DateOnly
    | AfterAppointments of AppointmentCount
    | OpenEnded

type SeriesRule =
    { Weekly: WeeklyRule
      StaffId: StaffId
      RoomId: RoomId option }

[<GenerateIdentifier(typeof<Guid>)>]
type Series =
    private
        { Id: SeriesId
          ClientId: ClientId
          ServiceId: ServiceId
          Duration: LessonDuration
          StartsOn: DateOnly
          End: SeriesEnd
          Rule: SeriesRule
          NextSequenceNumber: SequenceNumber }

[<RequireQualifiedAccess>]
module Series =
    let create id clientId serviceId duration startsOn ending rule =
        match ending with
        | SeriesEnd.OnDateInclusive endDate when endDate < startsOn ->
            Error(SchedulingError.SeriesEndBeforeStart(startsOn, endDate))
        | _ ->
            Ok
                { Id = id
                  ClientId = clientId
                  ServiceId = serviceId
                  Duration = duration
                  StartsOn = startsOn
                  End = ending
                  Rule = rule
                  NextSequenceNumber = SequenceNumber.first }

    let id series = series.Id

    let clientId series = series.ClientId

    let serviceId series = series.ServiceId

    let duration series = series.Duration

    let startsOn series = series.StartsOn

    let ending series = series.End

    let rule series = series.Rule

    let nextSequenceNumber series = series.NextSequenceNumber
