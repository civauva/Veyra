import { useLayoutEffect, useRef } from "react";

import { ReleaseNotes } from "./ReleaseNotes";
import "./about.css";

/** Props for {@link ReleaseNotesDialog}. */
interface ReleaseNotesDialogProps {
  /** The version this page runs, marked in the notes. */
  current: string | null;
  /** Closes it. */
  onClose(): void;
}

/**
 * The release notes over the screen (VEY-82), opened from the version in the sidebar or from the notice of a newer
 * version, in every look. Escape, its close button, or a click beside it closes it; the notes scroll inside it.
 */
export function ReleaseNotesDialog({ current, onClose }: ReleaseNotesDialogProps) {
  const closeRef = useRef<HTMLButtonElement>(null);

  // Before it is painted, so a key pressed as soon as it shows already reaches it.
  useLayoutEffect(() => {
    closeRef.current?.focus();

    function keyed(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    document.addEventListener("keydown", keyed);

    return () => document.removeEventListener("keydown", keyed);
  }, [onClose]);

  return (
    <div className="ab-scrim" onClick={onClose}>
      <div
        className="ab-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="ab-notes-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="ab-dialog-head">
          <h2 id="ab-notes-title">What's new in Veyra</h2>
          <button ref={closeRef} type="button" className="ab-close" aria-label="Close" onClick={onClose}>
            <span aria-hidden="true">×</span>
          </button>
        </header>
        <div className="ab-dialog-body">
          <ReleaseNotes current={current} />
        </div>
      </div>
    </div>
  );
}
