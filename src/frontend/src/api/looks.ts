import type { LookChoice } from "./types";

/**
 * The looks the server knows (ADR 0021): the values `PUT /api/v1/account/look` accepts and the session names. Kept in
 * the API layer because they are part of the contract, so every layer, screens included, may name them.
 */
export const lookIds = ["console", "forecast"] as const;

/** One of the looks. */
export type LookId = (typeof lookIds)[number];

/** Each look's name on screen. */
export const lookNames: Record<LookId, string> = { console: "Console", forecast: "Forecast" };

/**
 * Whether a value names a look this app has.
 *
 * @param value - A look id from the server, or anything else.
 * @returns True for a known look.
 */
export function isLookId(value: string | null | undefined): value is LookId {
  return lookIds.includes(value as LookId);
}

/**
 * The look to draw: the person's own when they picked one and may choose, otherwise the instance's default. A look
 * this app does not know, such as one added on a newer server, falls back to the default and then to Console, the
 * default look (ADR 0029).
 *
 * @param choice - The person's look, the default, and whether people may choose, from the session.
 * @returns The look's id.
 */
export function lookIdOf(choice: LookChoice): LookId {
  if (choice.mayChoose && isLookId(choice.chosen)) {
    return choice.chosen;
  }

  return isLookId(choice.default) ? choice.default : "console";
}
