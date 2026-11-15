import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../", import.meta.url));

function run(args) {
  const result = spawnSync("dotnet", args, { cwd: root, stdio: "inherit" });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}

// Run in an elevated shell: restore and build may access NuGet.
run(["tool", "restore", "--tool-manifest", "dotnet-tools.json"]);
run(["build", "MelodyTrack.Rewrite.slnx", "--verbosity", "minimal"]);
run([
  "kiota", "generate",
  "--language", "TypeScript",
  "--openapi", "src/MelodyTrack.Rewrite.Api/obj/openapi/MelodyTrack.Rewrite.Api.json",
  "--class-name", "RewriteClient",
  "--output", "web/src/shared/api/generated",
  "--clean-output",
  "--include-path", "/api/auth/staff/password",
  "--include-path", "/api/auth/staff/passkey/**",
  "--include-path", "/api/auth/staff/factor",
  "--include-path", "/api/auth/staff/refresh",
  "--include-path", "/api/auth/staff/restore",
  "--include-path", "/api/auth/staff/register/**",
  "--include-path", "/api/auth/me",
  "--include-path", "/api/auth/logout",
  "--include-path", "/api/releases",
  "--include-path", "/api/releases/**",
  "--include-path", "/api/clients",
  "--include-path", "/api/clients/options",
]);
const normalization = spawnSync(process.execPath, ["web/scripts/normalize-kiota-optionals.ts"], {
  cwd: root,
  stdio: "inherit",
});
if (normalization.error) throw normalization.error;
if (normalization.status !== 0) process.exit(normalization.status ?? 1);
