#r "System.Text.Json"
#load "MigrationGeneration.fs"

open MelodyTrack.Rewrite.Build.MigrationGeneration

match fsi.CommandLineArgs |> Array.skip 1 with
| [| directory; output; namespaceName |] ->
    try generate directory output namespaceName
    with
    | InvalidMigrationSource reason ->
        eprintfn "Migration generation failed: %s" reason
        exit 1
    | error ->
        eprintfn "Migration generation failed (%s)." (error.GetType().Name)
        exit 1
| _ ->
    eprintfn "Expected: <migration-directory> <output-file> <namespace>."
    exit 1
