import { pulseOnce } from "../../ui/useHashPulse";

/**
 * Brings a part of the screen into view and gives it the focus, as a tile that leads somewhere does (VEY-62): the
 * blocking tree from the Blocked tile, a database's row from what needs doing. The part has `tabIndex={-1}`, so it
 * takes the focus without becoming a stop of the Tab key, and its border pulses once to show where the tile led
 * (VEY-83).
 *
 * @param id - The ID of the part.
 * @returns Whether the part is on the screen.
 */
export function showPart(id: string): boolean {
  const part = document.getElementById(id);

  if (!part) {
    return false;
  }

  const still = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;

  // jsdom has no scrolling, so the call is skipped there.
  part.scrollIntoView?.({ block: "center", behavior: still ? "auto" : "smooth" });
  part.focus({ preventScroll: true });
  pulseOnce(part);

  return true;
}

/**
 * The ID of a database's row in the Databases tab's list.
 *
 * @param name - The database.
 * @returns An ID that holds no spaces, whatever the name.
 */
export function databaseRowId(name: string): string {
  return `database-row-${encodeURIComponent(name)}`;
}

/**
 * The ID of a database's details in the Databases tab's Advanced view.
 *
 * @param name - The database.
 * @returns An ID that holds no spaces, whatever the name.
 */
export function databaseDetailId(name: string): string {
  return `database-detail-${encodeURIComponent(name)}`;
}
