import type { LiveSession, WaitTerm } from "../../api/types";

/**
 * Seconds as a clock: `0:41`, `2:14`, `1:02:14`.
 *
 * @param seconds - The length in seconds.
 * @returns The clock.
 */
export function clock(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = String(total % 60).padStart(2, "0");

  return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${rest}` : `${minutes}:${rest}`;
}

/**
 * A count of reads or rows, short: `812`, `96 k`, `4.1 M`.
 *
 * @param value - The count.
 * @returns The text.
 */
export function shortCount(value: number): string {
  if (value >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)} M`;
  }

  return value >= 10_000 ? `${Math.round(value / 1000)} k` : value.toLocaleString("en-US");
}

/**
 * What a session does now, in words for the Simple view.
 *
 * @param session - The session.
 * @param waits - The wait types explained.
 * @returns For example "waits for the disk", "busy computing", or "holds a transaction, runs nothing".
 */
export function doingWords(session: LiveSession, waits: Record<string, WaitTerm>): string {
  if (session.status === "sleeping") {
    return session.transactionSeconds !== null ? "holds a transaction, runs nothing" : "connected, runs nothing";
  }

  if (session.blockedBy !== null) {
    return `waits for connection ${session.blockedBy}`;
  }

  if (session.waitType) {
    return waits[session.waitType]?.simple ?? `waits (${session.waitType})`;
  }

  return "busy computing";
}

/**
 * A program's name for sentences.
 *
 * @param program - The name it connected with.
 * @returns The name, or words for a program that gave none.
 */
export function programName(program: string): string {
  return program.trim().length > 0 ? program : "an unnamed program";
}

/**
 * Copies text to the clipboard.
 *
 * @param text - The text.
 * @returns True when it was copied.
 */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);

    return true;
  } catch {
    return false;
  }
}
