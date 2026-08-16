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
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    include: ["**/*.browser.test.tsx", "**/*.webkit.test.tsx"],
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
