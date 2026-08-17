import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const frontendRoot = resolve(repositoryRoot, "MelodyTrack.Web");
const packageJsonPath = resolve(frontendRoot, "package.json");
const packageLockPath = resolve(frontendRoot, "package-lock.json");
const nodeModulesPath = resolve(frontendRoot, "node_modules");
const npmLockPath = resolve(nodeModulesPath, ".package-lock.json");
const stampPath = resolve(nodeModulesPath, ".melodytrack-dependencies.json");

const [packageJson, packageLock] = await Promise.all([readFile(packageJsonPath), readFile(packageLockPath)]);
const fingerprint = createHash("sha256")
  .update(packageJson)
  .update("\0")
  .update(packageLock)
  .update("\0")
  .update(`${process.platform}\0${process.arch}\0${process.version}`)
  .digest("hex");

let currentFingerprint;
try {
  currentFingerprint = JSON.parse(await readFile(stampPath, "utf8")).fingerprint;
} catch {
  currentFingerprint = undefined;
}

if (currentFingerprint === fingerprint && existsSync(npmLockPath)) {
  console.log("Frontend dependencies are current.");
  process.exit(0);
}

const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";
const install = spawnSync(npmCommand, ["ci"], {
  cwd: frontendRoot,
  encoding: "utf8",
  stdio: "inherit",
});

if (install.error) {
  throw install.error;
}

if (install.status !== 0) {
  process.exit(install.status ?? 1);
}

await mkdir(nodeModulesPath, { recursive: true });
await writeFile(
  stampPath,
  `${JSON.stringify({ fingerprint, node: process.version, platform: process.platform, architecture: process.arch }, null, 2)}\n`,
  "utf8",
);
