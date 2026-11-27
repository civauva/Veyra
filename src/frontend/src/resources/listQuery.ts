import type { ResourceQuery } from "../api/types";
import type { ShellTime } from "../app/shellContext";

/** The length of the window outside a shell, as in a component test: the last 24 hours. */
const outsideMs = 24 * 3_600_000;

/** What a resource list asks the server, with a key that changes when the request would. */
export interface ListQuery {
  /** What the request depends on, as one string: the range, the environment, and the filters. */
  key: string;
  /** The request, with a window that ends now worked out when it is asked. */
  request(): Omit<ResourceQuery, "cursor">;
}

/**
 * What a resource list asks: the shared range, the environment, and the filters the address holds (ADR 0022).
 *
 * @param params - The address's query parameters.
 * @param time - The shell's shared time, missing outside a shell.
 * @param environment - The chosen environment, or null.
 * @param names - The filters the screen has, by parameter name.
 * @returns The query.
 */
export function listQuery(
  params: URLSearchParams,
  time: ShellTime | undefined,
  environment: string | null,
  names: string[],
): ListQuery {
  const filters: Record<string, string> = {};

  for (const name of names) {
    const value = params.get(name);

    if (value) {
      filters[name] = value;
    }
  }

  const choice = time?.choice ?? null;

  return {
    key: JSON.stringify({ choice, environment, filters }),
    request: () => {
      const window = time
        ? time.window()
        : { from: new Date(Date.now() - outsideMs).toISOString(), to: new Date().toISOString() };

      return { ...window, environment, filters };
    },
  };
}
