import { type RefObject, useEffect } from "react";
import { useLocation } from "react-router";

/** How long the border pulses, in milliseconds; the CSS animation lasts as long. */
const pulseMs = 1600;

/**
 * Brings a section into view when the address points at it, and pulses its border once so the reader sees where the
 * link took them (VEY-83), such as "What it means and how to fix it" opening an alert at its fix. React Router does
 * not scroll to an anchor by itself. It waits for the section to be drawn, since the page may still be reading; it
 * scrolls without motion when the person asks for reduced motion.
 *
 * @param hash - The anchor, without `#`, such as `fix`.
 * @param target - The section.
 * @param ready - Whether the section is drawn yet.
 */
export function useHashPulse(hash: string, target: RefObject<HTMLElement | null>, ready: boolean): void {
  const location = useLocation();
  const asked = location.hash === `#${hash}`;

  useEffect(() => {
    const element = target.current;

    if (!asked || !ready || !element) {
      return;
    }

    const still = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;

    element.scrollIntoView?.({ block: "start", behavior: still ? "auto" : "smooth" });
    element.focus({ preventScroll: true });
    pulseOnce(element);
  }, [asked, ready, target, location.key]);
}

/**
 * Pulses an element's border once, as a section a link or a tile just brought into view (VEY-83). Following the same
 * link twice pulses it again.
 *
 * @param element - The section.
 */
export function pulseOnce(element: HTMLElement): void {
  element.classList.remove("pulse-once");
  // Read the layout, so the animation starts again when the class comes back.
  void element.offsetWidth;
  element.classList.add("pulse-once");
  window.setTimeout(() => element.classList.remove("pulse-once"), pulseMs);
}
