import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// The API serves the built app from its wwwroot folder (ADR 0004). During development, Vite serves the app and
// forwards API calls to the API running on its development port.
const api = "http://localhost:5292";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": api,
      "/healthz": api,
      "/readyz": api,
    },
  },
  build: {
    outDir: "dist",
    sourcemap: true,
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: false,
    // Playwright runs the end-to-end tests in e2e/; Vitest runs only the unit and component tests.
    include: ["src/**/*.test.{ts,tsx}"],
  },
});
