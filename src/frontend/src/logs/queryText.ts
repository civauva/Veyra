import type { LogQueryField } from "../api/types";

/*
 * Writing the text of a log query (ADR 0024). The server parses the query and says where each term sits; these
 * helpers only add a term, or remove or replace one at the place the server named, so the browser never interprets
 * the query itself.
 */

/** Where a term sits in the text, as the server located it. */
export interface TermPlace {
  /** Its first character, counted from 0. */
  start: number;
  /** The character after its last one. */
  end: number;
}

/**
 * A value as the language writes it: as it is when it is one plain word, quoted otherwise.
 *
 * @param value - The value.
 * @returns The value, quoted with its own quotes escaped when it needs quoting.
 */
export function quoteValue(value: string): string {
  return /^[^\s"()]+$/.test(value) ? value : `"${value.replace(/\\/g, "\\\\").replace(/"/g, '\\"')}"`;
}

/**
 * One term in the language: `service:orders`, `level>=warning`, `http.route:/pay`, or a quoted phrase of the message.
 *
 * @param field - What the term filters on.
 * @param value - The value.
 * @param op - `:` for "is", `>=` for a level or worse.
 * @param key - The attribute's name, for an attribute term.
 * @returns The term's text.
 */
export function termText(field: LogQueryField, value: string, op: ":" | ">=" = ":", key?: string): string {
  if (field === "text") {
    return /\s/.test(value) ? quoteValue(value) : value;
  }

  const name = field === "attribute" ? (key ?? "") : field;

  return `${name}${op}${quoteValue(value)}`;
}

/**
 * The query with one more term, joined with `AND`.
 *
 * @param text - The query.
 * @param term - The term's text.
 * @returns The new query.
 */
export function addTerm(text: string, term: string): string {
  const query = text.trim();

  return query ? `${query} AND ${term}` : term;
}

/**
 * The query without the term at a place the server named, and without the `AND` that joined it to its neighbour.
 *
 * @param text - The query.
 * @param place - Where the term sits.
 * @returns The new query.
 */
export function removeTerm(text: string, place: TermPlace): string {
  const before = text.slice(0, place.start).replace(/\s*(\bAND\s*)?$/, "");
  const after = text.slice(place.end).replace(/^\s*(AND\b\s*)?/, "");

  if (!before) {
    return after.trim();
  }

  if (!after) {
    return before.trim();
  }

  // Keep the joint the person wrote: AND where an AND was on either side, a space otherwise.
  const joined = /\bAND\s*$/.test(text.slice(0, place.start)) || /^\s*AND\b/.test(text.slice(place.end));

  return `${before.trim()}${joined ? " AND " : " "}${after.trim()}`;
}

/**
 * The query with another term in the place of one the server named.
 *
 * @param text - The query.
 * @param place - Where the old term sits.
 * @param term - The new term's text.
 * @returns The new query.
 */
export function replaceTerm(text: string, place: TermPlace, term: string): string {
  return `${text.slice(0, place.start)}${term}${text.slice(place.end)}`;
}
