import { existsSync } from "node:fs";
import { fileURLToPath, URL } from "node:url";

import react from "@vitejs/plugin-react";
import { playwright } from "@vitest/browser-playwright";
import { defineConfig } from "vitest/config";

const configuredChromium = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH;
const systemChromium = "/usr/bin/chromium";
const executablePath = configuredChromium ?? (existsSync(systemChromium) ? systemChromium : undefined);

export default defineConfig({
  plugins: [react()],
  optimizeDeps: {
    // A dependency discovered after the browser runner starts makes Vite reload the
    // page, aborting whichever test module is being imported. Keep the browser
    // test server's dependency graph fixed before Chromium connects.
    noDiscovery: true,
    include: [
      "@tanstack/react-query",
      "@testing-library/react",
      "@xyflow/react",
      "antd",
      "antd/locale/ru_RU",
      "dayjs",
      "dayjs/plugin/timezone",
      "dayjs/plugin/utc",
      "dexie",
      "libphonenumber-js",
      "lucide-react",
      "react",
      "react-dom",
      "react/jsx-dev-runtime",
      "react/jsx-runtime",
      "react-router",
      "scheduler",
      "tinyduration",
      "use-sync-external-store/shim/index.js",
      "use-sync-external-store/shim/with-selector.js",
      "vitest-browser-react",
    ],
    needsInterop: [
      "dayjs/plugin/timezone",
      "dayjs/plugin/utc",
      "use-sync-external-store/shim/index.js",
      "use-sync-external-store/shim/with-selector.js",
    ],
  },
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    include: ["**/*.browser.test.tsx", "**/*.webkit.test.tsx"],
    setupFiles: ["./src/test/browserSetup.ts"],
    browser: {
      enabled: true,
      headless: true,
      provider: playwright({
        launchOptions: { executablePath },
        contextOptions: { hasTouch: true, reducedMotion: "reduce" },
      }),
      instances: [{ browser: "chromium" }],
    },
  },
});
