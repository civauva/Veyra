import { useSyncExternalStore } from "react";

/** How the Console sidebar is drawn: with labels, as icons, or as a drawer behind the menu button. */
export type ConsoleForm = "full" | "mini" | "drawer";

/** From this width the sidebar shows its labels. */
export const fullQuery = "(min-width: 1180px)";

/** From this width, and below the labelled form, it shows icons. */
export const miniQuery = "(min-width: 720px)";

/** The form for the window as it is now. Without media queries, as in component tests, the window counts as wide. */
function formNow(): ConsoleForm {
  if (typeof window.matchMedia !== "function" || window.matchMedia(fullQuery).matches) {
    return "full";
  }

  return window.matchMedia(miniQuery).matches ? "mini" : "drawer";
}

/** Calls back whenever the window crosses one of the widths. */
function subscribe(changed: () => void): () => void {
  if (typeof window.matchMedia !== "function") {
    return () => undefined;
  }

  const queries = [window.matchMedia(fullQuery), window.matchMedia(miniQuery)];

  queries.forEach((query) => query.addEventListener("change", changed));

  return () => queries.forEach((query) => query.removeEventListener("change", changed));
}

/**
 * The Console sidebar's form for the window's width: labelled on desktops, icons on tablets and small laptops, and a
 * drawer on phones.
 *
 * @returns The form, kept current as the window changes.
 */
export function useConsoleForm(): ConsoleForm {
  return useSyncExternalStore(subscribe, formNow, () => "full");
}
