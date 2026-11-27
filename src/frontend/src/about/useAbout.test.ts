import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { newerBuild, useAbout } from "./useAbout";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { about: vi.fn() } };
});

const first = { version: "0.4.1", build: "aaaaaaaaaaaa" };
const next = { version: "0.4.2", build: "bbbbbbbbbbbb" };

describe("newerBuild", () => {
  it("newerBuild_SameBuild_IsNull", () => {
    expect(newerBuild(first, { ...first })).toBeNull();
  });

  it("newerBuild_AnotherBuild_IsTheRunningOne", () => {
    expect(newerBuild(first, next)).toEqual(next);
  });

  it("newerBuild_NothingReadYet_IsNull", () => {
    expect(newerBuild(null, next)).toBeNull();
    expect(newerBuild(first, null)).toBeNull();
  });
});

describe("useAbout", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("useAbout_ReleaseRunsWhileThePageIsOpen_OffersIt", async () => {
    vi.mocked(api.about).mockResolvedValueOnce(first).mockResolvedValue(next);
    const { result } = renderHook(() => useAbout());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(result.current.version).toBe("0.4.1");
    expect(result.current.newer).toBeNull();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5 * 60_000);
    });
    // The page still runs the build it loaded, so it keeps saying 0.4.1 and offers 0.4.2.
    expect(result.current.version).toBe("0.4.1");
    expect(result.current.newer).toEqual(next);
  });

  it("useAbout_Dismissed_HidesTheOfferUntilAnotherBuild", async () => {
    vi.mocked(api.about).mockResolvedValueOnce(first).mockResolvedValue(next);
    const { result } = renderHook(() => useAbout());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5 * 60_000);
    });
    act(() => result.current.dismiss());
    expect(result.current.newer).toBeNull();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5 * 60_000);
    });
    expect(result.current.newer).toBeNull();
  });

  it("useAbout_PageShownAgain_AsksAtOnce", async () => {
    vi.mocked(api.about).mockResolvedValueOnce(first).mockResolvedValue(next);
    const { result } = renderHook(() => useAbout());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    await act(async () => {
      document.dispatchEvent(new Event("visibilitychange"));
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(result.current.newer).toEqual(next);
  });
});
