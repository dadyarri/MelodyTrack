import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const sourceRoot = fileURLToPath(new URL("../src", import.meta.url));
const reviewedClipboardFiles = new Set([
  "entities/session/ui/RecoveryCodesCard.tsx",
  "entities/session/ui/TotpSecretPanel.tsx",
  "pages/tasks/model/useTasksPageController.ts",
  "shared/ui/UrlCopyModal.tsx",
]);
const reviewedCopyableFiles = new Set(["entities/session/ui/TotpSecretPanel.tsx", "pages/auth/ui/AuthPage.tsx"]);
const requiredUrlModalProducers = new Map([
  ["pages/client-portal-schedule/ui/ClientPortalSchedulePage.tsx", 1],
  ["pages/clients/model/useClientsPageController.tsx", 2],
  ["pages/profile/model/useProfilePageController.ts", 1],
  ["pages/users/model/useUsersPageController.ts", 2],
]);

const files = await collectSourceFiles(sourceRoot);
const problems = [];

for (const filePath of files) {
  const relativePath = path.relative(sourceRoot, filePath);
  const source = await readFile(filePath, "utf8");

  if (/\.(?:test|spec)\.[cm]?[jt]sx?$/.test(relativePath)) {
    continue;
  }

  if (source.includes("navigator.clipboard") && !reviewedClipboardFiles.has(relativePath)) {
    problems.push(`${relativePath} accesses navigator.clipboard outside the reviewed shared URL modal or non-URL workflows.`);
  }

  if (/\bcopyable\b/.test(source) && !reviewedCopyableFiles.has(relativePath)) {
    problems.push(`${relativePath} uses copyable content outside the reviewed non-URL TOTP workflow.`);
  }

  if (
    /ссылк[^\n]{0,80}(?:скопирован|скопирована|скопировано)|скопирован[^\n]{0,80}ссылк/i.test(source) &&
    relativePath !== "shared/ui/UrlCopyModal.tsx"
  ) {
    problems.push(`${relativePath} claims that a URL was copied outside the shared URL modal.`);
  }
}

for (const [relativePath, expectedCount] of requiredUrlModalProducers) {
  const source = await readFile(path.join(sourceRoot, relativePath), "utf8");
  const actualCount = source.match(/\.openUrlModal\s*\(/g)?.length ?? 0;
  if (actualCount !== expectedCount) {
    problems.push(
      `${relativePath} must route ${String(expectedCount)} URL producer(s) through the shared modal; found ${String(actualCount)}.`,
    );
  }
}

if (problems.length > 0) {
  console.error(["URL copy boundary check failed:", ...problems.map((problem) => `- ${problem}`)].join("\n"));
  process.exitCode = 1;
} else {
  console.log("URL copy boundary: all URL producers use the shared explicit-copy modal.");
}

async function collectSourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nestedFiles = await Promise.all(
    entries.map((entry) => {
      const entryPath = path.join(directory, entry.name);
      return entry.isDirectory() ? collectSourceFiles(entryPath) : [entryPath];
    }),
  );

  return nestedFiles.flat().filter((filePath) => /\.(?:ts|tsx)$/.test(filePath));
}
