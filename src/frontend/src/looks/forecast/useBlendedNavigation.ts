import type { MouseEvent } from "react";
import { flushSync } from "react-dom";
import { useNavigate } from "react-router";

/**
 * Moving between screens through a view transition (VEY-23): the browser keeps a picture of the old screen, the new
 * one renders at once underneath, and the shell's styles fade one into the other. The top bar and the weather keep
 * their place.
 *
 * React Router starts view transitions itself only in its data-router mode; Veyra uses the declarative router, so
 * the shell starts them here for its own links. Browsers without view transitions, people who ask for reduced
 * motion, and clicks that open a new tab navigate as usual.
 *
 * @returns A click handler for a link to the given path.
 */
export function useBlendedNavigation(): (event: MouseEvent<HTMLAnchorElement>, to: string) => void {
  const navigate = useNavigate();

  return (event, to) => {
    const plainClick = event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey;
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;

    if (event.defaultPrevented || !plainClick || reduced || typeof document.startViewTransition !== "function") {
      return;
    }

    // The link would navigate on its own; the transition navigates instead, rendering the new screen synchronously
    // so the browser can take its picture.
    event.preventDefault();
    document.startViewTransition(() => {
      flushSync(() => {
        void navigate(to);
      });
    });
  };
}
