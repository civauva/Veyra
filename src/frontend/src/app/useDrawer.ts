import { useEffect, useRef, useState, type RefObject } from "react";
import { useLocation } from "react-router";

/** A menu that opens over the screen on phones, as every look's shell has one (ADR 0021). */
export interface Drawer {
  /** Whether it is open now. */
  open: boolean;
  /** Opens it, from the menu button. */
  show(): void;
  /** Closes it and gives the focus back to the menu button. */
  close(): void;
  /** The menu button that opens it, which gets the focus back when it closes. */
  menuRef: RefObject<HTMLButtonElement | null>;
  /** The close button inside it, which gets the focus when it opens. */
  closeRef: RefObject<HTMLButtonElement | null>;
}

/**
 * The open and closed state of a shell's phone drawer: it opens from the menu button, takes the focus to its close
 * button, closes with Escape, and closes by itself when a link in it leads to another screen or when the window grows
 * out of the drawer form.
 *
 * @param active - Whether the shell is in its drawer form now.
 * @returns The drawer.
 */
export function useDrawer(active: boolean): Drawer {
  const [drawer, setDrawer] = useState({ open: false, path: "" });
  const location = useLocation();
  const menuRef = useRef<HTMLButtonElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const open = active && drawer.open && drawer.path === location.pathname;

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    closeRef.current?.focus();

    function keyed(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setDrawer({ open: false, path: "" });
        menuRef.current?.focus();
      }
    }

    document.addEventListener("keydown", keyed);

    return () => document.removeEventListener("keydown", keyed);
  }, [open]);

  return {
    open,
    show: () => setDrawer({ open: true, path: location.pathname }),
    close: () => {
      setDrawer({ open: false, path: "" });
      menuRef.current?.focus();
    },
    menuRef,
    closeRef,
  };
}
