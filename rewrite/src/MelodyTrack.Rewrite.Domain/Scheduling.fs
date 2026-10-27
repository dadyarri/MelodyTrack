namespace MelodyTrack.Rewrite.Domain.Scheduling

open System

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
    | EmptyIdentifier of identifier: string
    | InvalidSequenceNumber of value: int64
    | InvalidLessonDuration of minutes: int
    | InvalidStartHour of hour: int
    | EmptyWeeklyRule
    | DuplicateWeekday of weekday: Weekday
    | InvalidAppointmentCount of value: int
    | SeriesEndBeforeStart of startDate: DateOnly * endDate: DateOnly

[<Struct>]
type SeriesId = private SeriesId of Guid

[<RequireQualifiedAccess>]
module SeriesId =
    let create value =
        if value = Guid.Empty then
            Error(SchedulingError.EmptyIdentifier "series")
        else
            Ok(SeriesId value)

    let value (SeriesId value) = value

[<Struct>]
type ClientId = private ClientId of Guid

[<RequireQualifiedAccess>]
module ClientId =
    let create value =
        if value = Guid.Empty then
            Error(SchedulingError.EmptyIdentifier "client")
        else
            Ok(ClientId value)

    let value (ClientId value) = value

[<Struct>]
type ServiceId = private ServiceId of Guid

[<RequireQualifiedAccess>]
module ServiceId =
    let create value =
        if value = Guid.Empty then
            Error(SchedulingError.EmptyIdentifier "service")
        else
            Ok(ServiceId value)

    let value (ServiceId value) = value

[<Struct>]
type StaffId = private StaffId of Guid

[<RequireQualifiedAccess>]
module StaffId =
    let create value =
        if value = Guid.Empty then
            Error(SchedulingError.EmptyIdentifier "staff")
        else
            Ok(StaffId value)

    let value (StaffId value) = value

[<Struct>]
type RoomId = private RoomId of Guid

[<RequireQualifiedAccess>]
module RoomId =
    let create value =
        if value = Guid.Empty then
            Error(SchedulingError.EmptyIdentifier "room")
        else
            Ok(RoomId value)

    let value (RoomId value) = value

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
