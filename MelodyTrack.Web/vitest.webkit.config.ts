import { fileURLToPath, URL } from "node:url";

import react from "@vitejs/plugin-react";
import { playwright } from "@vitest/browser-playwright";
import { defineConfig } from "vitest/config";

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
      provider: playwright({ contextOptions: { hasTouch: true, isMobile: true, reducedMotion: "reduce" } }),
      instances: [{ browser: "webkit" }],
    },
  },
});
