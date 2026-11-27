import { useCallback, useSyncExternalStore } from "react";

import { usePopover } from "./usePopover";

/** How much a screen shows: its fundamentals, or everything, the fundamentals included. */
export type ViewMode = "simple" | "advanced";

/** Where the choice is remembered, in this browser only. One choice serves every screen. */
const storageKey = "veyra.view";

/** Components showing the mode, told when it changes. */
const listeners = new Set<() => void>();

/** The choice when storage refuses to keep it, as in some private windows. */
let fallback: ViewMode = "simple";

function read(): ViewMode {
  try {
    return localStorage.getItem(storageKey) === "advanced" ? "advanced" : "simple";
  } catch {
    return fallback;
  }
}

function write(mode: ViewMode): void {
  fallback = mode;

  try {
    localStorage.setItem(storageKey, mode);
  } catch {
    // Storage refused: the fallback keeps the choice for this visit.
  }

  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);

  return () => listeners.delete(listener);
}

/**
 * The app-wide Simple or Advanced choice. Settings and the monitoring screens show their fundamentals in Simple and
 * everything, the fundamentals included, in Advanced (VEY-37). Simple until the person chooses.
 *
 * @returns The mode, and a function that changes it for every screen at once.
 */
export function useViewMode(): [ViewMode, (mode: ViewMode) => void] {
  const mode = useSyncExternalStore(subscribe, read, () => "simple" as ViewMode);
  const set = useCallback((next: ViewMode) => write(next), []);

  return [mode, set];
}

/** Props for {@link ViewToggle}. */
interface ViewToggleProps {
  /** An extra class, for placing it in a screen's header. */
  className?: string;
}

/** Simple or Advanced, in the header of Settings and each monitoring screen; the choice holds across visits. */
export function ViewToggle({ className }: ViewToggleProps) {
  const [mode, setMode] = useViewMode();

  return (
    <div className={className ? `view-toggle ${className}` : "view-toggle"} role="group" aria-label="View">
      {(["simple", "advanced"] as const).map((option) => (
        <button key={option} type="button" aria-pressed={mode === option} onClick={() => setMode(option)}>
          {option === "simple" ? "Simple" : "Advanced"}
        </button>
      ))}
    </div>
  );
}

/** What each view shows, in the lens menu's words. */
const viewWords: Record<ViewMode, { name: string; what: string }> = {
  simple: { name: "Simple", what: "The figures that need you, and what to do about them." },
  advanced: { name: "Advanced", what: "Every counter, wait, column, and setting, the fundamentals included." },
};

/**
 * The same choice as {@link ViewToggle} as a lens (VEY-83): one button that names the view in force and opens a small
 * menu with a sentence about each, so people learn what Advanced adds. It is for a look whose top bar is dense, as
 * Console's. Escape or a press outside closes the menu; the arrow keys move between the two views.
 */
export function ViewSwitch() {
  const [mode, setMode] = useViewMode();
  const { open, toggle, close, rootRef, buttonRef } = usePopover();

  function choose(next: ViewMode) {
    setMode(next);
    close(true);
  }

  return (
    <div ref={rootRef} className="view-lens">
      <button
        ref={buttonRef}
        type="button"
        className="view-lens-button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`${viewWords[mode].name} view`}
        onClick={toggle}
      >
        <svg className="view-lens-eye" viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
          <path d="M1.5 8s2.4-4.5 6.5-4.5S14.5 8 14.5 8 12.1 12.5 8 12.5 1.5 8 1.5 8Z" />
          <circle cx="8" cy="8" r={mode === "advanced" ? 2.6 : 1.6} />
        </svg>
        <span className="view-lens-words">{viewWords[mode].name} view</span>
        <svg className="view-lens-chevron" viewBox="0 0 10 10" width="9" height="9" aria-hidden="true">
          <path d={open ? "M2 6.5 5 3.5l3 3" : "M2 3.5 5 6.5l3-3"} />
        </svg>
      </button>
      {open && (
        <div
          className="view-lens-menu"
          role="menu"
          aria-label="View"
          onKeyDown={(event) => {
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
              event.preventDefault();
              const items = [...event.currentTarget.querySelectorAll<HTMLButtonElement>("button")];
              const at = items.indexOf(document.activeElement as HTMLButtonElement);
              items[(at + (event.key === "ArrowDown" ? 1 : items.length - 1)) % items.length]?.focus();
            }
          }}
        >
          {(["simple", "advanced"] as const).map((option) => (
            <button
              key={option}
              type="button"
              role="menuitemradio"
              aria-checked={mode === option}
              className="view-lens-item"
              // The view in force takes the focus when the menu opens, as a radio group would.
              autoFocus={mode === option}
              onClick={() => choose(option)}
            >
              <b>{viewWords[option].name}</b>
              <span>{viewWords[option].what}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
