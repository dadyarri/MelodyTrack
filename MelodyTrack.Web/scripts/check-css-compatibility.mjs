import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

import browserslist from "browserslist";
import { browserslistToTargets, transform } from "lightningcss";

const sourceRoot = path.resolve("src");
const targets = browserslistToTargets(browserslist());
const cssFiles = await findCssFiles(sourceRoot);
const failures = [];

for (const filename of cssFiles) {
  try {
    const result = transform({
      filename,
      code: await readFile(filename),
      cssModules: filename.endsWith(".module.css"),
      minify: false,
      targets,
    });
    for (const warning of result.warnings) {
      failures.push(`${path.relative(process.cwd(), filename)}: ${warning.message}`);
    }
  } catch (error) {
    failures.push(`${path.relative(process.cwd(), filename)}: ${error instanceof Error ? error.message : String(error)}`);
  }
}

if (failures.length > 0) {
  console.error(`CSS compatibility check failed:\n${failures.map((failure) => `- ${failure}`).join("\n")}`);
  process.exitCode = 1;
} else {
  console.log(`CSS compatibility: ${String(cssFiles.length)} files compile for the configured browser matrix (iOS Safari 16.4+).`);
}

async function findCssFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(
    entries.map((entry) => {
      const entryPath = path.join(directory, entry.name);
      return entry.isDirectory() ? findCssFiles(entryPath) : Promise.resolve(entry.name.endsWith(".css") ? [entryPath] : []);
    }),
  );
  return nested.flat().sort();
}
