import { fileURLToPath, URL } from "node:url";
import react from "@vitejs/plugin-react";
import { loadEnv } from "vite";
import { defineConfig } from "vitest/config";

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), "MELODYTRACK_");
  const target = environment.MELODYTRACK_API_TARGET || "http://localhost:5280";
  const api = new URL(target);
  const port = Number(process.env.AGENT_CHECKS_BROWSER_PORT ?? 5174);
  const testReport = process.env.AGENT_CHECKS_VITEST_REPORT;
  const testFiles = process.env.AGENT_CHECKS_VITEST_FILES;
  if (!["http:", "https:"].includes(api.protocol) || api.username || api.password) {
    throw new Error("MELODYTRACK_API_TARGET должен быть HTTP(S)-адресом API без учётных данных.");
  }

  return {
    plugins: [
      react(),
      {
        name: "localhost-navigation",
        configureServer(server) {
          server.middlewares.use((request, response, next) => {
            const loopbackIp =
              request.headers.host === `127.0.0.1:${port}` ||
              request.headers.host === `[::1]:${port}`;
            if (
              loopbackIp &&
              (request.method === "GET" || request.method === "HEAD") &&
              request.headers.accept?.includes("text/html") &&
              request.url?.startsWith("/")
            ) {
              response.writeHead(307, {
                Location: `http://localhost:${port}${request.url}`,
                "Cache-Control": "no-store",
              });
              response.end();
              return;
            }
            next();
          });
        },
      },
    ],
    resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
    server: {
      host: "localhost",
      port,
      strictPort: true,
      proxy: { "/api": { target: api.origin, changeOrigin: true } },
    },
    build: {
      target: ["chrome109", "edge109", "firefox115", "safari16.4"],
      rolldownOptions: {
        preserveEntrySignatures: false,
        output: {
          strictExecutionOrder: true,
          codeSplitting: {
            // Keep route-only dependencies lazy and split large library groups.
            includeDependenciesRecursively: false,
            maxSize: 400_000,
            groups: [
              {
                name: "react",
                test: /[\\/]node_modules[\\/](?:react|react-dom|scheduler)[\\/]/,
                priority: 40,
                entriesAware: true,
              },
              {
                name: "api",
                test: /[\\/]node_modules[\\/]@microsoft[\\/]kiota-/,
                priority: 30,
                entriesAware: true,
              },
              {
                name: "icons",
                test: /[\\/]node_modules[\\/]@tabler[\\/]/,
                priority: 20,
                entriesAware: true,
              },
              {
                name: "ui",
                test: /[\\/]node_modules[\\/](?:@base-ui|react-hook-form|react-paginate|qrcode.react)[\\/]/,
                priority: 10,
                entriesAware: true,
              },
              { name: "vendor", test: /[\\/]node_modules[\\/]/, entriesAware: true },
            ],
          },
        },
      },
    },
    test: {
      ...(testFiles ? { include: JSON.parse(testFiles) as string[] } : {}),
      reporters: testReport ? ["default", "json"] : ["default"],
      ...(testReport ? { outputFile: { json: testReport } } : {}),
      exclude: ["**/node_modules/**", "e2e/**"],
      environment: "jsdom",
      setupFiles: ["./src/test/setup.ts"],
      clearMocks: true,
      restoreMocks: true,
    },
  };
});
