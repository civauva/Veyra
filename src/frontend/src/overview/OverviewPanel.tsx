import type { ReactNode, RefObject } from "react";
import { Link, useSearchParams } from "react-router";

import { carryTime } from "../time/carryTime";
import { HelpTip } from "../ui/HelpTip";

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
  children,
}: OverviewPanelProps) {
  const id = `ov-${title.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`;
  const [params] = useSearchParams();

  return (
    <section
      className={["ov-panel", to && whole ? "whole" : "", className].filter(Boolean).join(" ")}
      aria-labelledby={id}
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
