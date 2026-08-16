import { fileURLToPath, URL } from "node:url";

import react from "@vitejs/plugin-react";
import browserslist from "browserslist";
import { browserslistToTargets } from "lightningcss";
import { visualizer } from "rollup-plugin-visualizer";
import { defineConfig, loadEnv } from "vite";

const cssTargets = browserslistToTargets(browserslist());

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), "");
  const apiProxyTarget = resolveApiProxyTarget(environment.MELODY_TRACK_API_PROXY_TARGET);

  return {
    plugins: [
      react(),
      mode === "analyze" &&
        visualizer({
          filename: "artifacts/bundle-stats.html",
          gzipSize: true,
          brotliSize: true,
          open: false,
        }),
    ],
    resolve: {
      dedupe: ["react", "react-dom", "react/jsx-runtime", "react/jsx-dev-runtime", "scheduler"],
      alias: {
        "@": fileURLToPath(new URL("./src", import.meta.url)),
      },
    },
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        "/api": {
          target: apiProxyTarget,
          changeOrigin: true,
        },
      },
    },
    optimizeDeps: {
      include: ["react", "react-dom", "react/jsx-runtime", "react/jsx-dev-runtime", "scheduler"],
    },
    build: {
      target: ["es2022", "safari16.4"],
      cssTarget: "safari16.4",
      cssMinify: "lightningcss",
      cssMinifyOptions: {
        lightningcss: {
          targets: cssTargets,
        },
      },
      rolldownOptions: {
        output: {
          codeSplitting: {
            groups: [
              {
                name: "query",
                test: /[\\/]node_modules[\\/]@tanstack[\\/](query-core|react-query)[\\/]/,
              },
              {
                name: "network",
                test: /[\\/]node_modules[\\/]axios[\\/]/,
              },
              {
                name: "lucide",
                test: /[\\/]node_modules[\\/]lucide-react[\\/]/,
              },
              {
                name: "select-runtime",
                test: /[\\/]node_modules[\\/]@rc-component[\\/](select|virtual-list)[\\/]/,
              },
            ],
          },
        },
      },
    },
  };
});

function resolveApiProxyTarget(value?: string) {
  const target = new URL(value?.trim() || "http://localhost:5000");
  if (target.protocol !== "http:" && target.protocol !== "https:") {
    throw new Error("MELODY_TRACK_API_PROXY_TARGET must use HTTP or HTTPS.");
  }

  return target.toString();
}
