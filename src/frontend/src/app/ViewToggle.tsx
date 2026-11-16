import { useCallback, useSyncExternalStore } from "react";

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
