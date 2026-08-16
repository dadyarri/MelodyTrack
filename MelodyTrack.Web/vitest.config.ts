import { fileURLToPath, URL } from "node:url";

import { configDefaults, defineConfig } from "vitest/config";

export default defineConfig({
  define: {
    "import.meta.env.VITE_API_BASE_URL": JSON.stringify("/api"),
  },
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    exclude: [...configDefaults.exclude, "**/*.browser.test.tsx", "**/*.webkit.test.tsx"],
    clearMocks: true,
    restoreMocks: true,
  },
});
