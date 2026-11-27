import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { ApiError } from "../api/client";
import type { LookChoice } from "../api/types";
import { SessionContext, type SessionValue } from "../session/SessionContext";
import { useLookChoice } from "./useLookChoice";

/** A wrapper that signs in a person whose look choice is as given. */
function signedIn(look: LookChoice, setLook = vi.fn().mockResolvedValue(undefined)) {
  const value = {
    state: {
      status: "signedIn",
      session: { id: "1", email: "a@example.com", displayName: "Ana", role: "Admin", timeZone: null, look },
    },
    setLook,
  } as unknown as SessionValue;

  return ({ children }: { children: ReactNode }) => <SessionContext value={value}>{children}</SessionContext>;
}

describe("useLookChoice", () => {
  it("useLookChoice_NoChoiceYet_ShowsTheDefault", () => {
    const { result } = renderHook(() => useLookChoice(), {
      wrapper: signedIn({ chosen: null, default: "console", mayChoose: true }),
    });

    expect(result.current).toMatchObject({ shown: "console", fallback: "console", mayChoose: true, own: false });
  });

  it("useLookChoice_Pick_KeepsItOnTheAccount", async () => {
    const setLook = vi.fn().mockResolvedValue(undefined);
    const { result } = renderHook(() => useLookChoice(), {
      wrapper: signedIn({ chosen: null, default: "console", mayChoose: true }, setLook),
    });

    await act(() => result.current?.choose("forecast"));

    expect(setLook).toHaveBeenCalledWith("forecast");
  });

  it("useLookChoice_RefusedByTheServer_SaysWhy", async () => {
    const setLook = vi.fn().mockRejectedValue(new ApiError(400, "That look is not offered."));
    const { result } = renderHook(() => useLookChoice(), {
      wrapper: signedIn({ chosen: "forecast", default: "console", mayChoose: true }, setLook),
    });

    await act(() => result.current?.choose(null));

    await waitFor(() => expect(result.current?.error).toBe("That look is not offered."));
    expect(result.current?.own).toBe(true);
  });
});
