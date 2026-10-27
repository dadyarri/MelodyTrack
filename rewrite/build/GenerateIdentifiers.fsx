#load "IdentifierGeneration.fs"

open MelodyTrack.Rewrite.Build.IdentifierGeneration

match fsi.CommandLineArgs |> Array.skip 1 with
| arguments when arguments.Length >= 2 ->
    let output = arguments[0]
    let sources = arguments |> Array.skip 1
    try generate sources output
    with
    | InvalidIdentifierSource reason ->
        eprintfn "Identifier generation failed: %s" reason
        exit 1
    | error ->
        eprintfn "Identifier generation failed (%s)." (error.GetType().Name)
        exit 1
| _ ->
    eprintfn "Expected: <output-file> <source-file> [<source-file> ...]."
    exit 1
