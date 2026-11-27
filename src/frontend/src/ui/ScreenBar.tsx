import type { ReactNode } from "react";
import { createPortal } from "react-dom";
import { Link } from "react-router";

import { useShell } from "../app/shellContext";
import { ViewSwitch, ViewToggle } from "./ViewToggle";
import "./screenBar.css";

/** One step of the trail to the screen. */
export interface Crumb {
  /** What it says, or a control of its own, such as a server switcher. */
  label: ReactNode;
  /** Where it leads; the screen itself, the last step, has none. */
  to?: string;
}

/** Props for {@link ScreenBar}. */
export interface ScreenBarProps {
  /** The trail to the screen, from the top: `Metrics › web-01`. */
  trail: Crumb[];
  /** Whether the screen has the Simple and Advanced views; its toggle then ends the bar, or sits in the shell's top bar. */
  view?: boolean;
  /** The screen's own controls, such as its time range, placed before the view toggle. */
  children?: ReactNode;
}

/**
 * The bar across the top of a screen (VEY-62): where the screen is on the left, its controls on the right, and the
 * Simple and Advanced toggle always at the right end, so it never moves between screens, tabs, or views. Inside the
 * app's frame it fills the frame's own bar, which stays put while the screen scrolls; outside it, as in a component
 * test, it is drawn in place. Where the shell's top bar has a place for the toggle, as Console's on wider screens, the
 * toggle goes there instead, beside the time range. A look may ask for one Advanced switch instead of the pair.
 *
 * @param props - The trail, whether the screen has views, and its controls.
 * @returns The bar's content.
 */
export function ScreenBar({ trail, view = false, children }: ScreenBarProps) {
  const { bar, viewSlot, viewSwitch } = useShell();
  const toggleInSlot = view && viewSlot;
  const toggle = viewSwitch ? <ViewSwitch /> : <ViewToggle />;
  const content = (
    <>
      <nav className="screen-trail" aria-label="Where you are">
        <ol>
          {trail.map((crumb, index) => (
            <li key={index}>
              {crumb.to ? (
                <Link to={crumb.to}>{crumb.label}</Link>
              ) : index === trail.length - 1 ? (
                <span aria-current="page">{crumb.label}</span>
              ) : (
                crumb.label
              )}
            </li>
          ))}
        </ol>
      </nav>
      <div className="screen-tools">
        {children}
        {view && !toggleInSlot && toggle}
      </div>
    </>
  );

  return (
    <>
      {bar ? createPortal(content, bar) : <div className="screen-bar">{content}</div>}
      {toggleInSlot && createPortal(toggle, viewSlot)}
    </>
  );
}
