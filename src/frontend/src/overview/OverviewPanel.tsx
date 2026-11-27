import { type ReactNode, type RefObject, useRef } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";

import { carryTime } from "../time/carryTime";
import { HelpTip } from "../ui/HelpTip";
import { useHashPulse } from "../ui/useHashPulse";

/** Props for {@link OverviewPanel}. */
interface OverviewPanelProps {
  /** The panel's name, which is also its heading. */
  title: string;
  /** What the panel shows and where it comes from, for its question mark. */
  help: string;
  /** The screen with the details, which the title opens in the same range (VEY-82). */
  to?: string;
  /**
   * Whether the whole panel opens {@link to}, for a chart with nothing else to click; a panel with rows of its own
   * opens from its title and each row opens its own details.
   */
  whole?: boolean;
  /** A short note at the right of the heading, such as "Last 24 hours". */
  meta?: ReactNode;
  /** A line at the foot, such as a link to the full screen. */
  foot?: ReactNode;
  /** Whether the content runs to the panel's edges, as a table does. */
  flush?: boolean;
  /** An extra class, for its place in the grid. */
  className?: string;
  /** The body, for a list that measures how many rows fit in it (VEY-81). */
  bodyRef?: RefObject<HTMLDivElement | null>;
  /** The anchor that brings the panel into view and pulses it, such as `databases` for `/#databases` (VEY-83). */
  anchor?: string;
  /** Whether the screen has its content, so the panels above stop moving before it scrolls to this one. */
  settled?: boolean;
  /** The content. */
  children: ReactNode;
}

/**
 * One panel of the Overview: a heading with its question mark and a note, the content, and an optional foot. Its
 * title opens the screen with the details, keeping the range; a chart's panel opens it from anywhere on it.
 */
export function OverviewPanel({
  title,
  help,
  to,
  whole = false,
  meta,
  foot,
  flush = false,
  className,
  bodyRef,
  anchor,
  settled = true,
  children,
}: OverviewPanelProps) {
  const id = `ov-${title.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`;
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const section = useRef<HTMLElement>(null);
  useHashPulse(anchor ?? "", section, anchor !== undefined && settled);

  return (
    <section
      ref={section}
      tabIndex={anchor ? -1 : undefined}
      className={["ov-panel", to && whole ? "whole" : "", className].filter(Boolean).join(" ")}
      aria-labelledby={id}
      // A chart's panel opens from a click anywhere on it, so the chart under the pointer still shows its moments
      // (VEY-83); its title stays the link the keyboard and screen readers use.
      onClick={
        to && whole
          ? (event) => {
              if (!(event.target as Element).closest("a, button")) {
                void navigate(carryTime(to, params));
              }
            }
          : undefined
      }
    >
      <header>
        <h2 id={id}>
          {to ? (
            <Link className="ov-panel-link" to={carryTime(to, params)}>
              {title}
              <span className="ov-panel-chevron" aria-hidden="true">
                ›
              </span>
            </Link>
          ) : (
            title
          )}
        </h2>
        <HelpTip label={title}>{help}</HelpTip>
        {meta && <span className="ov-meta">{meta}</span>}
      </header>
      <div className={flush ? "ov-body flush" : "ov-body"} ref={bodyRef}>
        {children}
      </div>
      {foot && <footer>{foot}</footer>}
    </section>
  );
}
