import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";

const directArguments = ["run", "test:webkit:direct"];
const operatingSystem = process.platform === "linux" ? readFileSync("/etc/os-release", "utf8") : "";
const needsContainer = /^(?:ID|ID_LIKE)=.*\barch\b/m.test(operatingSystem);

const command = needsContainer ? "docker" : "npm";
const argumentsList = needsContainer
  ? [
      "run",
      "--rm",
      "--init",
      "--ipc=host",
      "--user",
      `${String(process.getuid?.() ?? 1000)}:${String(process.getgid?.() ?? 1000)}`,
      "--env",
      "HOME=/tmp",
      "--env",
      "PLAYWRIGHT_BROWSERS_PATH=/ms-playwright",
      "--volume",
      `${process.cwd()}:/work`,
      "--workdir",
      "/work",
      "mcr.microsoft.com/playwright:v1.61.0-noble",
      "npm",
      ...directArguments,
    ]
  : directArguments;

const result = spawnSync(command, argumentsList, { stdio: "inherit" });

if (result.error) {
  console.error(result.error.message);
  process.exit(1);
}

process.exit(result.status ?? 1);
