import { defineConfig, devices } from "@playwright/test";

if (process.env.MELODYTRACK_PLAYWRIGHT_DOCKER !== "1") {
  throw new Error("Run Playwright in Docker with npm run test:browser");
}

const artifacts = process.env.AGENT_CHECKS_BROWSER_DIR ?? ".artifacts/browser";
const baseURL = `http://localhost:${process.env.AGENT_CHECKS_BROWSER_PORT ?? 5174}`;
const reportFile = process.env.AGENT_CHECKS_BROWSER_DIR ? "result.json" : "report.json";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: true,
  retries: 0,
  outputDir: `${artifacts}/results`,
  reporter: [["list"], ["json", { outputFile: `${artifacts}/${reportFile}` }]],
  use: { baseURL, trace: "retain-on-failure" },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "webkit", use: { ...devices["Desktop Safari"] } },
    { name: "webkit-mobile", use: { ...devices["iPhone 13"], defaultBrowserType: "webkit" } },
  ],
  webServer: {
    command: "npm run dev",
    url: baseURL,
    reuseExistingServer: false,
  },
});
