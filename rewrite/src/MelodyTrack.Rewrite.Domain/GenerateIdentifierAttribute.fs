namespace MelodyTrack.Rewrite.Domain

open System

[<Sealed; AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false, Inherited = false)>]
type GenerateIdentifierAttribute(valueType: Type) =
    inherit Attribute()

    member _.ValueType = valueType

[<RequireQualifiedAccess>]
type IdentifierError =
    | Empty
