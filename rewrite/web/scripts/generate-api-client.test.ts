// @vitest-environment node

import { spawnSync } from "node:child_process";
import {
  chmodSync,
  copyFileSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { URL } from "node:url";
import { afterEach, describe, expect, it } from "vitest";

const directories: string[] = [];
afterEach(() => {
  for (const directory of directories.splice(0))
    rmSync(directory, { recursive: true, force: true });
});

function invoke(failAt = -1) {
  const directory = mkdtempSync(join(tmpdir(), "rewrite-generator-"));
  directories.push(directory);
  mkdirSync(join(directory, "scripts"));
  mkdirSync(join(directory, "web/scripts"), { recursive: true });
  copyFileSync(
    new URL("../../scripts/generate-api-client.mjs", import.meta.url),
    join(directory, "scripts/generate-api-client.mjs"),
  );
  const log = join(directory, "commands.jsonl");
  const fake = join(directory, "dotnet");
  writeFileSync(
    fake,
    `#!${process.execPath}\nconst fs = require('node:fs'); const path = process.env.GENERATOR_LOG; const newline = String.fromCharCode(10); const calls = fs.existsSync(path) ? fs.readFileSync(path, 'utf8').trim().split(newline).length : 0; fs.appendFileSync(path, JSON.stringify(process.argv.slice(2)) + newline); process.exit(calls === Number(process.env.GENERATOR_FAIL_AT) ? 17 : 0);\n`,
  );
  chmodSync(fake, 0o700);
  writeFileSync(
    join(directory, "web/scripts/normalize-kiota-optionals.ts"),
    `import { appendFileSync } from 'node:fs'; appendFileSync(process.env.GENERATOR_LOG, JSON.stringify(['normalize']) + String.fromCharCode(10));`,
  );
  const result = spawnSync(process.execPath, [join(directory, "scripts/generate-api-client.mjs")], {
    encoding: "utf8",
    env: {
      ...process.env,
      PATH: `${directory}:${process.env.PATH}`,
      GENERATOR_LOG: log,
      GENERATOR_FAIL_AT: String(failAt),
    },
  });
  return {
    status: result.status,
    calls: readFileSync(log, "utf8")
      .trim()
      .split("\n")
      .map((line) => JSON.parse(line) as string[]),
  };
}

describe("rewrite API generation command", () => {
  it("builds the contract before generating every supported route and normalizing output", () => {
    const { status, calls } = invoke();
    expect(status).toBe(0);
    expect(calls[0]).toEqual(["tool", "restore", "--tool-manifest", "dotnet-tools.json"]);
    expect(calls[1]).toEqual(["build", "MelodyTrack.Rewrite.slnx", "--verbosity", "minimal"]);
    const generate = calls[2];
    if (!generate) throw new Error("Missing generation command");
    expect(generate.slice(0, 2)).toEqual(["kiota", "generate"]);
    expect(generate[generate.indexOf("--openapi") + 1]).toBe(
      "src/MelodyTrack.Rewrite.Api/obj/openapi/MelodyTrack.Rewrite.Api.json",
    );
    expect(generate[generate.indexOf("--output") + 1]).toBe("web/src/shared/api/generated");
    const paths = generate.flatMap((value, index) =>
      value === "--include-path" ? [generate[index + 1]] : [],
    );
    expect(paths).toEqual([
      "/api/auth/staff/password",
      "/api/auth/staff/passkey/**",
      "/api/auth/staff/factor",
      "/api/auth/staff/refresh",
      "/api/auth/staff/restore",
      "/api/auth/staff/register/**",
      "/api/auth/me",
      "/api/auth/logout",
      "/api/releases",
      "/api/releases/**",
      "/api/clients",
      "/api/clients/options",
      "/api/changes/events",
    ]);
    expect(calls[3]).toEqual(["normalize"]);
    expect(calls).toHaveLength(4);
  });

  it.each([0, 1, 2])(
    "stops after failed command %i without generating or normalizing a partial contract",
    (index) => {
      const { status, calls } = invoke(index);
      expect(status).toBe(17);
      expect(calls).toHaveLength(index + 1);
      expect(calls).not.toContainEqual(["normalize"]);
    },
  );
});
