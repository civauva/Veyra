import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { useMetricData } from "./useMetricData";

/** A fetch that answers when told to, so a test can look between asking and answering. */
function deferred() {
  const answers = new Map<string, (value: string) => void>();
  const fetch = (value: string) => () =>
    new Promise<string>((resolve) => {
      answers.set(value, resolve);
    });

  return { fetch, answer: (value: string) => answers.get(value)?.(value) };
}

describe("useMetricData", () => {
  it("keeps the previous answer on screen while new choices are asked for", async () => {
    const server = deferred();
    const { result, rerender } = renderHook(
      ({ choice }: { choice: string }) => useMetricData("queries-sql-1h", server.fetch(choice), { choices: choice }),
      { initialProps: { choice: "total" } },
    );

    act(() => server.answer("total"));
    await waitFor(() => expect(result.current.data).toBe("total"));

    rerender({ choice: "reads" });
    expect(result.current.data).toBe("total");

    act(() => server.answer("reads"));
    await waitFor(() => expect(result.current.data).toBe("reads"));
  });

  it("starts over when the key changes", async () => {
    const server = deferred();
    const { result, rerender } = renderHook(({ key }: { key: string }) => useMetricData(key, server.fetch(key)), {
      initialProps: { key: "1h" },
    });

    act(() => server.answer("1h"));
    await waitFor(() => expect(result.current.data).toBe("1h"));

    rerender({ key: "7d" });
    expect(result.current.data).toBeNull();
  });
});
