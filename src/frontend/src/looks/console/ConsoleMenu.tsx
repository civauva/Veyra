import { useEffect, useId, type ReactNode } from "react";

import { usePopover } from "../../ui/usePopover";

/** Props for {@link ConsoleMenu}. */
interface ConsoleMenuProps {
  /** What the button shows. */
  button: ReactNode;
  /** The button's name for screen readers, when its picture alone says it. */
  label?: string;
  /** The button's class: a control with words, or an icon button. */
  buttonClass?: string;
  /** The menu's name, for screen readers. */
  menuLabel: string;
  /** A class for the button and its menu together, so a look can place or hide them. */
  wrapClass?: string;
  /** A key that opens and closes it from anywhere, when the focus is not in a field. */
  hotkey?: string;
  /** Extra classes for the menu, such as its width. */
  menuClass?: string;
  /**
   * What the menu holds, given a way to close it after a pick.
   *
   * @param close - Closes the menu and gives the focus back to its button.
   */
  children(close: () => void): ReactNode;
}

/**
 * A button of the Console top bar that opens a small menu under it: the time range, the refresh, and help. It closes
 * on Escape, on a click outside, and after a pick.
 */
export function ConsoleMenu({
  button,
  label,
  buttonClass = "cn-control",
  wrapClass,
  hotkey,
  menuLabel,
  menuClass,
  children,
}: ConsoleMenuProps) {
  const { open, toggle, close, rootRef, buttonRef } = usePopover();
  const menuId = useId();

  useEffect(() => {
    if (!hotkey) {
      return undefined;
    }

    function keyed(event: KeyboardEvent) {
      if (event.key.toLowerCase() === hotkey && !event.ctrlKey && !event.metaKey && !event.altKey && !typing(event)) {
        event.preventDefault();
        toggle();
      }
    }

    document.addEventListener("keydown", keyed);

    return () => document.removeEventListener("keydown", keyed);
  }, [hotkey, toggle]);

  return (
    <div className={["cn-menu-wrap", wrapClass].filter(Boolean).join(" ")} ref={rootRef}>
      <button
        ref={buttonRef}
        type="button"
        className={buttonClass}
        aria-label={label}
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={toggle}
      >
        {button}
      </button>
      {open && (
        <div
          id={menuId}
          className={["cn-menu", menuClass].filter(Boolean).join(" ")}
          role="dialog"
          aria-label={menuLabel}
        >
          {children(() => close(true))}
        </div>
      )}
    </div>
  );
}

/**
 * Whether a key press lands in a field, where letters are text and not shortcuts.
 *
 * @param event - The key press.
 * @returns True inside an input, a text area, a select, or editable text.
 */
export function typing(event: KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null;

  return target !== null && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
}
