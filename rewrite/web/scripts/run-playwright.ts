import { spawn, spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { mkdirSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

export function containerOptions(args: string[], env: NodeJS.ProcessEnv, root: string) {
  const artifactDir = resolve(root, env.AGENT_CHECKS_BROWSER_DIR ?? ".artifacts/browser");
  const targetDir = env.AGENT_CHECKS_BROWSER_DIR
    ? artifactDir
    : "/workspace/web/.artifacts/browser";
  const options = ["--volume", `${artifactDir}:${targetDir}`];
  if (env.AGENT_CHECKS_BROWSER_DIR) {
    options.push("--env", `AGENT_CHECKS_BROWSER_DIR=${targetDir}`);
  }
  for (const key of ["AGENT_CHECKS_BROWSER_PORT", "CI"]) {
    if (env[key]) options.push("--env", `${key}=${env[key]}`);
  }
  const forwarded = [...args];
  for (let index = 0; index < forwarded.length; index++) {
    const arg = forwarded[index];
    if (arg !== "--test-list" && !arg?.startsWith("--test-list=")) continue;
    const file = arg === "--test-list" ? forwarded[++index] : arg.slice("--test-list=".length);
    if (!file) throw new Error("--test-list requires a file");
    const target = `/tmp/playwright-test-list-${index}.txt`;
    options.push("--volume", `${resolve(root, file)}:${target}:ro`);
    forwarded[index] = arg === "--test-list" ? target : `--test-list=${target}`;
  }
  return { artifactDir, options, forwarded };
}

async function main() {
  const nodeVersion = readFileSync(join(webRoot, ".node-version"), "utf8").trim().replace(/^v/, "");
  const lock = JSON.parse(readFileSync(join(webRoot, "package-lock.json"), "utf8"));
  const playwrightVersion: string = lock.packages["node_modules/@playwright/test"].version;
  for (const version of [nodeVersion, playwrightVersion]) {
    if (!/^\d+\.\d+\.\d+$/.test(version)) throw new Error("Expected exact Node/Playwright version");
  }
  const { artifactDir, options, forwarded } = containerOptions(
    process.argv.slice(2),
    process.env,
    webRoot,
  );
  mkdirSync(artifactDir, { recursive: true });
  const temporary = mkdtempSync(join(tmpdir(), "melodytrack-playwright-"));
  const name = `melodytrack-playwright-${randomUUID()}`;
  let child: ReturnType<typeof spawn> | undefined;
  let interrupted = false;
  const stop = () => {
    interrupted = true;
    child?.kill("SIGTERM");
    spawnSync("docker", ["rm", "--force", name], { stdio: "ignore", timeout: 10_000 });
  };
  const run = (args: string[]) =>
    new Promise<number>((accept, reject) => {
      child = spawn("docker", args, { stdio: "inherit" });
      child.once("error", reject);
      child.once("exit", (code) => accept(code ?? 1));
    });
  process.on("SIGINT", stop);
  process.on("SIGTERM", stop);
  try {
    console.log("Playwright: building Docker image (cached layers), then running in Docker.");
    const buildCode = await run([
      "build",
      "--file",
      join(webRoot, "Dockerfile.playwright"),
      "--build-arg",
      `NODE_VERSION=${nodeVersion}`,
      "--build-arg",
      `PLAYWRIGHT_VERSION=${playwrightVersion}`,
      "--iidfile",
      join(temporary, "image-id"),
      webRoot,
    ]);
    if (buildCode !== 0 || interrupted) return buildCode || 130;
    const image = readFileSync(join(temporary, "image-id"), "utf8").trim();
    const user =
      process.getuid && process.getgid ? ["--user", `${process.getuid()}:${process.getgid()}`] : [];
    return await run([
      "run",
      "--rm",
      "--init",
      "--name",
      name,
      "--shm-size=1g",
      ...user,
      ...options,
      image,
      "node",
      "node_modules/@playwright/test/cli.js",
      "test",
      ...forwarded,
    ]);
  } finally {
    spawnSync("docker", ["rm", "--force", name], { stdio: "ignore", timeout: 10_000 });
    process.off("SIGINT", stop);
    process.off("SIGTERM", stop);
    rmSync(temporary, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main()
    .then((code) => {
      process.exitCode = code;
    })
    .catch((error: Error) => {
      console.error(`Playwright Docker launcher: ${error.message}`);
      process.exitCode = 1;
    });
}
