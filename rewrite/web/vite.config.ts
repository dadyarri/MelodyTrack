import { fileURLToPath, URL } from "node:url";
import react from "@vitejs/plugin-react";
import { loadEnv } from "vite";
import { defineConfig } from "vitest/config";

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), "MELODYTRACK_");
  const target = environment.MELODYTRACK_API_TARGET || "http://localhost:5280";
  const api = new URL(target);
  if (!["http:", "https:"].includes(api.protocol) || api.username || api.password) {
    throw new Error("MELODYTRACK_API_TARGET должен быть HTTP(S)-адресом API без учётных данных.");
  }

  return {
    plugins: [react()],
    resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
    server: {
      port: 5174,
      strictPort: true,
      proxy: { "/api": { target: api.origin, changeOrigin: true } },
    },
    build: { target: ["chrome109", "edge109", "firefox115", "safari16.4"] },
    test: {
      environment: "jsdom",
      setupFiles: ["./src/test/setup.ts"],
      clearMocks: true,
      restoreMocks: true,
    },
  };
});
