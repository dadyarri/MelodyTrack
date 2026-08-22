import { spawnSync } from "node:child_process";

const previewWarningMarker = "the typescript language is in preview (preview)";
const result = spawnSync("dotnet", ["kiota", "generate", ...process.argv.slice(2)], {
  encoding: "utf8",
  maxBuffer: 16 * 1024 * 1024,
});

if (result.error) {
  throw result.error;
}

process.stdout.write(reclassifyKnownPreviewWarning(result.stdout ?? ""));
process.stderr.write(reclassifyKnownPreviewWarning(result.stderr ?? ""));

if (result.signal) {
  process.stderr.write(`Kiota generation terminated by ${result.signal}.\n`);
}

process.exitCode = result.status ?? 1;

function reclassifyKnownPreviewWarning(output) {
  return output
    .split(/\r?\n/)
    .map((line) =>
      line.toLowerCase().includes(previewWarningMarker)
        ? "Kiota TypeScript generator maturity: Preview; generated output is checked by TypeScript compilation."
        : line,
    )
    .join("\n");
}
