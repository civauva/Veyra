// Shared setup for every test file.
import "@testing-library/jest-dom/vitest";

import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";

// Remove what a test rendered and forget recorded calls, so no test sees another test's page or mocks.
// Testing Library only does this by itself when Vitest globals are on, which this project does not use.
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
