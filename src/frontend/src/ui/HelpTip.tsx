import { useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

import "./helpTip.css";

/** Props for {@link HelpTip}. */
export interface HelpTipProps {
  /** The name of what it explains, such as "Peak"; its button is read as "What Peak means". */
  label: string;
  /** The explanation, a sentence or a few. */
  children: ReactNode;
}

/** How far the explanation keeps from the window's edges, in pixels. */
const margin = 12;

/**
 * A question mark beside a tile's name that explains the tile (VEY-60). It opens on hover, on a click or tap, and on
 * keyboard focus, and closes on leaving it, a second click, Escape, a click elsewhere, or once it is scrolled out of
 * view, so it works with a mouse, a finger, and the keyboard alike.
 *
 * The explanation is drawn on the page itself, under the question mark and inside the window: tiles are glass, and
 * glass starts a layer of its own that would cover an explanation drawn inside a tile with the tiles after it.
 *
 * @param props - What it explains, and the explanation.
 * @returns The button and, while open, its explanation.
 */
export function HelpTip({ label, children }: HelpTipProps) {
  const [open, setOpen] = useState(false);
  // A click opens it for good, so the explanation stays while the pointer moves on.
  const [pinned, setPinned] = useState(false);
  const [place, setPlace] = useState<{ top: number; left: number } | null>(null);
  const id = useId();
  const button = useRef<HTMLButtonElement>(null);
  const text = useRef<HTMLSpanElement>(null);

  useLayoutEffect(() => {
    setPlace(open ? placeUnder(button.current, text.current) : null);
  }, [open]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const close = () => {
      setOpen(false);
      setPinned(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        close();
      }
    };
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node;

      if (!button.current?.contains(target) && !text.current?.contains(target)) {
        close();
      }
    };

    document.addEventListener("keydown", onKey);
    document.addEventListener("pointerdown", onPointer);
    // It follows its question mark while the page scrolls, as a finger reading on a phone moves it, and closes once
    // the question mark has left the window.
    const follow = () => {
      const anchor = button.current?.getBoundingClientRect();

      if (!anchor || anchor.bottom < 0 || anchor.top > window.innerHeight) {
        close();

        return;
      }

      setPlace(placeUnder(button.current, text.current));
    };

    window.addEventListener("scroll", follow, { capture: true, passive: true });
    window.addEventListener("resize", follow);

    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("pointerdown", onPointer);
      window.removeEventListener("scroll", follow, { capture: true });
      window.removeEventListener("resize", follow);
    };
  }, [open]);

  const leave = () => {
    if (!pinned) {
      setOpen(false);
    }
  };

  return (
    <>
      <button
        ref={button}
        type="button"
        className="help-tip"
        aria-label={`What ${label} means`}
        aria-expanded={open}
        aria-describedby={open ? id : undefined}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={leave}
        onFocus={() => setOpen(true)}
        onBlur={leave}
        onClick={() => {
          const stay = !pinned || !open;

          setPinned(stay);
          setOpen(stay);
        }}
      >
        ?
      </button>
      {open &&
        createPortal(
          <span
            ref={text}
            id={id}
            role="tooltip"
            className="help-tip-text"
            style={place ? { top: place.top, left: place.left } : { visibility: "hidden" }}
          >
            {children}
          </span>,
          document.body,
        )}
    </>
  );
}

/**
 * Where an explanation goes: centred under its question mark, above it when there is no room below, and always inside
 * the window.
 *
 * @param button - The question mark.
 * @param text - The explanation, drawn already so its size is known.
 * @returns Its place in the window, or null before both are drawn.
 */
function placeUnder(button: HTMLElement | null, text: HTMLElement | null): { top: number; left: number } | null {
  const anchor = button?.getBoundingClientRect();
  const box = text?.getBoundingClientRect();

  if (!anchor || !box) {
    return null;
  }

  const width = document.documentElement.clientWidth;
  const centred = anchor.left + anchor.width / 2 - box.width / 2;
  const left = Math.max(margin, Math.min(centred, width - margin - box.width));
  const below = anchor.bottom + 8;
  const top = below + box.height > window.innerHeight - margin ? anchor.top - 8 - box.height : below;

  return { top: Math.max(margin, top), left };
}
