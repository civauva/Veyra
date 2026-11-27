import { type RefObject, useEffect, useRef, useState } from "react";

/** What {@link usePopover} gives the component that opens it. */
export interface Popover<T extends HTMLElement> {
  /** Whether it is open. */
  open: boolean;
  /** Opens it when closed, closes it when open. */
  toggle(): void;
  /**
   * Closes it.
   *
   * @param refocus - Gives the focus back to the button that opened it, as after choosing from it.
   */
  close(refocus?: boolean): void;
  /** The element that holds the button and the popover; a press outside it closes the popover. */
  rootRef: RefObject<HTMLDivElement | null>;
  /** The button that opens it, which gets the focus back when Escape closes it. */
  buttonRef: RefObject<T | null>;
}

/**
 * A small panel that opens from a button, such as the environment switcher and the account menu (VEY-26). It closes
 * on a press outside it and on Escape, which gives the focus back to its button.
 *
 * Escape is caught before it reaches the rest of the page, so inside the phone drawer it closes only the popover and
 * leaves the drawer open.
 *
 * @returns The state, the ways to change it, and the two elements it watches.
 */
export function usePopover<T extends HTMLElement = HTMLButtonElement>(): Popover<T> {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<T>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    function pressed(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    function keyed(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.stopPropagation();
        setOpen(false);
        buttonRef.current?.focus();
      }
    }

    // Capture, so Escape reaches this popover before the drawer's own handler.
    document.addEventListener("pointerdown", pressed);
    document.addEventListener("keydown", keyed, true);

    return () => {
      document.removeEventListener("pointerdown", pressed);
      document.removeEventListener("keydown", keyed, true);
    };
  }, [open]);

  return {
    open,
    toggle: () => setOpen((current) => !current),
    close(refocus = false) {
      setOpen(false);

      if (refocus) {
        buttonRef.current?.focus();
      }
    },
    rootRef,
    buttonRef,
  };
}
