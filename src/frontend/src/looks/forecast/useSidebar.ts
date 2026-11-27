import { useState, useSyncExternalStore } from "react";

/**
 * The three forms of the sidebar (VEY-26):
 *
 * - `full`: labelled, 232 px wide; the default from 1600 px.
 * - `mini`: icons only, 64 px wide, with the names as hover labels; the default from 1024 px, on laptops and tablets.
 * - `drawer`: below 1024 px the sidebar waits behind a menu button in a slim top bar, as on phones.
 */
export type SidebarForm = "full" | "mini" | "drawer";

/** The media query below which the sidebar becomes a drawer. */
export const drawerQuery = "(max-width: 1023px)";

/** The media query from which the sidebar is labelled unless the person collapsed it. */
export const fullQuery = "(min-width: 1600px)";

const storageKey = "veyra.sidebar";

function matches(query: string): boolean {
  return window.matchMedia(query).matches;
}

/** The form the width asks for. Without media queries, as in component tests, the window counts as a wide screen. */
function readWidth(): SidebarForm {
  if (typeof window.matchMedia !== "function") {
    return "full";
  }

  if (matches(drawerQuery)) {
    return "drawer";
  }

  return matches(fullQuery) ? "full" : "mini";
}

function subscribe(changed: () => void): () => void {
  if (typeof window.matchMedia !== "function") {
    return () => {};
  }

  const lists = [window.matchMedia(drawerQuery), window.matchMedia(fullQuery)];

  for (const list of lists) {
    list.addEventListener("change", changed);
  }

  return () => {
    for (const list of lists) {
      list.removeEventListener("change", changed);
    }
  };
}

/** The form the person chose in this browser, or null when they never did. Blocked storage means no choice. */
function readChoice(): "full" | "mini" | null {
  try {
    const saved = localStorage.getItem(storageKey);

    return saved === "full" || saved === "mini" ? saved : null;
  } catch {
    return null;
  }
}

/** What {@link useSidebar} gives the shell. */
export interface Sidebar {
  /** The form to draw. */
  form: SidebarForm;
  /** Switches between labelled and icons, and remembers the choice in this browser. */
  toggle(): void;
}

/**
 * The sidebar's form for the current window. Above phone width the person's own choice wins over the default of the
 * width, so collapsing it on a large screen keeps it collapsed. The choice is a convenience, so a failure to save it
 * is ignored.
 *
 * @returns The form and the way to switch it.
 */
export function useSidebar(): Sidebar {
  const width = useSyncExternalStore(subscribe, readWidth, () => "full" as const);
  const [choice, setChoice] = useState(readChoice);
  const form = width === "drawer" ? "drawer" : (choice ?? width);

  return {
    form,
    toggle() {
      const next = form === "full" ? "mini" : "full";
      setChoice(next);

      try {
        localStorage.setItem(storageKey, next);
      } catch {
        // Storage blocked: the choice holds until the page is reloaded.
      }
    },
  };
}
