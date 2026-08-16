import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const publicDirectory = fileURLToPath(new URL("../public", import.meta.url));
const maximumFileBytes = 128 * 1024;
const maximumTotalBytes = 256 * 1024;

async function collectFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = await Promise.all(
    entries.map(async (entry) => {
      const entryPath = path.join(directory, entry.name);
      return entry.isDirectory() ? collectFiles(entryPath) : [entryPath];
    }),
  );

  return files.flat();
}

const files = await collectFiles(publicDirectory);
const measuredFiles = await Promise.all(
  files.map(async (filePath) => ({
    filePath,
    size: (await stat(filePath)).size,
  })),
);
const totalBytes = measuredFiles.reduce((total, file) => total + file.size, 0);
const problems = measuredFiles
  .filter((file) => file.size > maximumFileBytes)
  .map(
    (file) =>
      `${path.relative(publicDirectory, file.filePath)} is ${formatKiB(file.size)}; the per-file budget is ${formatKiB(maximumFileBytes)}.`,
  );

for (const { filePath } of measuredFiles.filter((file) => file.filePath.endsWith(".svg"))) {
  const source = await readFile(filePath, "utf8");
  if (source.includes("data:image/")) {
    problems.push(`${path.relative(publicDirectory, filePath)} embeds a raster data URI; keep SVG assets natively vector.`);
  }
}

if (totalBytes > maximumTotalBytes) {
  problems.push(`public/ totals ${formatKiB(totalBytes)}; the directory budget is ${formatKiB(maximumTotalBytes)}.`);
}

if (problems.length > 0) {
  console.error(["Public asset budget failed:", ...problems.map((problem) => `- ${problem}`)].join("\n"));
  process.exitCode = 1;
} else {
  console.log(
    `Public assets: ${String(measuredFiles.length)} files, ${formatKiB(totalBytes)} total (budget ${formatKiB(maximumTotalBytes)}).`,
  );
}

function formatKiB(bytes) {
  return `${(bytes / 1024).toFixed(1)} KiB`;
}
