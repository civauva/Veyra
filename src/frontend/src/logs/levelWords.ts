import type { Severity } from "../api/types";

/*
 * Levels as the log query language writes them, and as Console names them (ADR 0024).
 */

/** The word the query language uses for each severity: `level:info`, `level>=warning`. */
export const levelWord: Record<Severity, string> = {
  Unspecified: "unspecified",
  Trace: "trace",
  Debug: "debug",
  Information: "info",
  Warning: "warning",
  Error: "error",
  Fatal: "fatal",
};

/** A short name for each severity, as the tables show it. */
export const levelName: Record<Severity, string> = {
  Unspecified: "None",
  Trace: "Trace",
  Debug: "Debug",
  Information: "Info",
  Warning: "Warning",
  Error: "Error",
  Fatal: "Fatal",
};

/** The levels a person picks from, most common first, as the By level panel lists them. */
export const pickableLevels: readonly Severity[] = ["Information", "Warning", "Error", "Fatal", "Debug"];

/**
 * The severity a word of the query names, ignoring case, so a chip can show a level the server parsed.
 *
 * @param word - The word, such as `warning` or `Info`.
 * @returns The severity, or null for a word that names none.
 */
export function severityOfWord(word: string): Severity | null {
  const lower = word.toLowerCase();
  const found = (Object.keys(levelWord) as Severity[]).find(
    (severity) => levelWord[severity] === lower || severity.toLowerCase() === lower,
  );

  return found ?? null;
}
