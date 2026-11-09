import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { api, ApiError, resetAntiforgeryForTests } from "./client";

/** Replaces fetch with a queue of canned responses and records every call. */
function mockFetch(...responses: Response[]) {
  const fetchMock = vi.fn<typeof fetch>();

  for (const response of responses) {
    fetchMock.mockResolvedValueOnce(response);
  }

  vi.stubGlobal("fetch", fetchMock);

  return fetchMock;
}

/** The request options of one recorded fetch call. */
function initOf(fetchMock: ReturnType<typeof mockFetch>, index: number): RequestInit | undefined {
  const call = fetchMock.mock.calls[index];

  if (!call) {
    throw new Error(`fetch was called fewer than ${index + 1} times.`);
  }

  return call[1];
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const session = { id: "1", email: "ana@example.com", displayName: "Ana", role: "Admin" };

beforeEach(() => resetAntiforgeryForTests());

afterEach(() => vi.unstubAllGlobals());

describe("api.signIn", () => {
  it("signs in, then fetches the antiforgery token that later writes carry", async () => {
    const fetchMock = mockFetch(json(session), json({ token: "af-1" }), new Response(null, { status: 204 }));

    await api.signIn("ana@example.com", "secret secret");
    await api.revokeKey("key-1");

    expect(new Headers(initOf(fetchMock, 2)?.headers).get("X-Veyra-Antiforgery")).toBe("af-1");
  });

  it("throws an ApiError with the problem title when the API refuses", async () => {
    mockFetch(json({ title: "The email or password is wrong." }, 401));

    await expect(api.signIn("ana@example.com", "wrong")).rejects.toEqual(
      new ApiError(401, "The email or password is wrong."),
    );
  });
});

describe("api.currentSession", () => {
  it("returns null when nobody is signed in", async () => {
    mockFetch(new Response(null, { status: 401 }));

    await expect(api.currentSession()).resolves.toBeNull();
  });

  it("returns the session and prepares the antiforgery token", async () => {
    const fetchMock = mockFetch(json(session), json({ token: "af-2" }));

    await expect(api.currentSession()).resolves.toEqual(session);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});

describe("requests", () => {
  it("send cookies for the same origin only and ask for JSON", async () => {
    const fetchMock = mockFetch(json({ required: false }));

    await api.setupStatus();

    expect(fetchMock.mock.calls[0]?.[0]).toBe("/api/v1/setup");
    expect(initOf(fetchMock, 0)?.credentials).toBe("same-origin");
    expect(new Headers(initOf(fetchMock, 0)?.headers).get("Accept")).toBe("application/json");
  });

  it("use a readable message when the API sends no problem details", async () => {
    mockFetch(new Response("oops", { status: 500 }));

    await expect(api.setupStatus()).rejects.toEqual(new ApiError(500, "Veyra answered with status 500."));
  });
});
