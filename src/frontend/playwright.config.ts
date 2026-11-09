import { defineConfig, devices } from "@playwright/test";

// End-to-end tests run the built app in a real browser at the three sizes every screen must support
// (AGENTS.md section 5). The REST API is answered by the tests themselves (e2e/api.ts), so the suite needs no
// database; the API's own behaviour is covered by the backend integration tests.
const port = 4174;

export default defineConfig({
  testDir: "e2e",
  fullyParallel: true,
  reporter: "list",
  use: {
    baseURL: `http://localhost:${port}`,
    trace: "retain-on-failure",
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 } } },
    // A landscape tablet: below the 1200 px breakpoint, so the record opens as a drawer.
    { name: "tablet", use: { ...devices["Desktop Chrome"], viewport: { width: 1024, height: 768 }, hasTouch: true } },
    { name: "phone", use: { ...devices["Pixel 7"], viewport: { width: 390, height: 844 } } },
  ],
  webServer: {
    command: `npx vite build && npx vite preview --port ${port} --strictPort`,
    port,
    reuseExistingServer: true,
  },
});
