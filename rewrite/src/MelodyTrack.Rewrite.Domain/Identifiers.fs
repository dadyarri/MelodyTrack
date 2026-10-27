namespace MelodyTrack.Rewrite.Domain.Scheduling

open System
open MelodyTrack.Rewrite.Domain

// Move each identifier to generation when its domain model is introduced.
[<Struct>]
type ClientId = private ClientId of Guid

[<RequireQualifiedAccess>]
module ClientId =
    let create value =
        if value = Guid.Empty then
            Error IdentifierError.Empty
        else
            Ok(ClientId value)

    let value (ClientId value) = value

[<Struct>]
type ServiceId = private ServiceId of Guid

[<RequireQualifiedAccess>]
module ServiceId =
    let create value =
        if value = Guid.Empty then
            Error IdentifierError.Empty
        else
            Ok(ServiceId value)

    let value (ServiceId value) = value

[<Struct>]
type StaffId = private StaffId of Guid

[<RequireQualifiedAccess>]
module StaffId =
    let create value =
        if value = Guid.Empty then
            Error IdentifierError.Empty
        else
            Ok(StaffId value)

    let value (StaffId value) = value

[<Struct>]
type RoomId = private RoomId of Guid

[<RequireQualifiedAccess>]
module RoomId =
    let create value =
        if value = Guid.Empty then
            Error IdentifierError.Empty
        else
            Ok(RoomId value)

    let value (RoomId value) = value
