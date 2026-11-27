import type { ReactNode } from "react";
import { Link, useSearchParams } from "react-router";

import type { BlockHealth } from "../api/types";
import { carryTime } from "../time/carryTime";
import { HelpTip } from "../ui/HelpTip";
import { useViewMode } from "../ui/ViewToggle";
import { ScreenBar } from "../ui/ScreenBar";
import "./resources.css";

/** A figure of the header strip. */
export interface ResourceFigure {
  /** Its name. */
  label: string;
  /** The figure itself, worded. */
  value: ReactNode;
  /** A line under it, such as a change against the previous period. */
  detail: ReactNode;
  /** Whether it needs a look: amber for caution, red for warning. */
  tone?: "warn" | "bad";
  /** What it shows and where it comes from, for its question mark. */
  help: string;
  /** The screen behind the figure, which the whole tile opens in the same range (VEY-83). */
  to?: string;
}

/** A filter the server applies, kept in the address under its parameter name. */
export interface ResourceFilter {
  /** Its parameter name, as the API and the address take it. */
  name: string;
  /** Its words before the choice, such as "Health". */
  label: string;
  /** The choices, the first being the one when the address names none. */
  options: { value: string; label: string }[];
}

/** A column of the list. */
export interface ResourceColumn<TItem> {
  /** Its heading. */
  title: string;
  /** Whether it holds numbers, which line up on the right. */
  numeric?: boolean;
  /** Whether it is left out on phones, so the list fits. */
  wide?: boolean;
  /** Whether only the Advanced view shows it (VEY-83). */
  advanced?: boolean;
  /**
   * Its content for one item.
   *
   * @param item - The item.
   */
  cell(item: TItem): ReactNode;
}

/** Props for {@link ResourceScreen}. */
export interface ResourceScreenProps<TItem> {
  /** The screen's name. */
  title: string;
  /** What the screen holds, in a sentence. */
  intro: string;
  /** A short state beside the name, such as "1 warning · 1 caution", with where it leads, or null. */
  state: { words: string; tone: "warn" | "bad" | "ok"; to?: string } | null;
  /** The header figures, for the whole filtered set. */
  figures: ResourceFigure[];
  /** The filters. */
  filters: ResourceFilter[];
  /** What the find box looks for, such as "Find a service". */
  find: string;
  /** The list's name, over its table. */
  listName: string;
  /** What the list's question mark says. */
  listHelp: string;
  /** The columns. */
  columns: ResourceColumn<TItem>[];
  /** The items loaded so far, in the server's order. */
  items: TItem[];
  /** A key for each item. */
  keyOf(item: TItem): string;
  /** Where each item leads. */
  linkOf(item: TItem): string;
  /** How many items the filters keep. */
  total: number;
  /** A sentence under the list. */
  note: string;
  /** Whether there is more to load. */
  more: boolean;
  /** Whether a page is on its way. */
  loading: boolean;
  /** Why the last request failed, or null. */
  error: string | null;
  /** Loads the next page. */
  onMore(): void;
  /** Controls beside the name, such as Add a server. */
  actions?: ReactNode;
}

/**
 * The frame of a resource list (ADR 0022): the trail, the name with its state, a strip of figures for the whole
 * filtered set, the filters and the find box, and the list worst first. Every filter, count, and order comes from the
 * server; the screen keeps the filters in the address, so a link shows the same list. Every figure with a screen
 * behind it opens it from anywhere on its tile, and every row opens its item from anywhere on it; links inside a row,
 * such as a health pill or a late backup, open their own detail instead (VEY-83).
 */
export function ResourceScreen<TItem>(props: ResourceScreenProps<TItem>) {
  const [params, setParams] = useSearchParams();
  const [mode] = useViewMode();

  function choose(name: string, value: string, first: string) {
    const next = new URLSearchParams(params);

    if (value === first || value === "") {
      next.delete(name);
    } else {
      next.set(name, value);
    }

    setParams(next, { replace: true });
  }

  return (
    <main className="rs" data-view={mode}>
      <ScreenBar trail={[{ label: props.title }]} view />
      <header className="rs-head">
        <div>
          <h1>
            {props.title}
            {props.state &&
              (props.state.to ? (
                <Link className={`rs-pill ${props.state.tone}`} to={carryTime(props.state.to, params)}>
                  {props.state.words}
                </Link>
              ) : (
                <span className={`rs-pill ${props.state.tone}`}>{props.state.words}</span>
              ))}
          </h1>
          <p>{props.intro}</p>
        </div>
        {props.actions && <div className="rs-actions">{props.actions}</div>}
      </header>

      <section className="rs-figures" aria-label="Figures">
        {props.figures.map((figure) => (
          <div key={figure.label} className={["rs-figure", figure.tone].filter(Boolean).join(" ")}>
            <div className="rs-figure-name">
              {figure.to ? (
                <Link className="rs-figure-link" to={carryTime(figure.to, params)}>
                  {figure.label}
                </Link>
              ) : (
                figure.label
              )}
              <HelpTip label={figure.label}>{figure.help}</HelpTip>
            </div>
            <div className="rs-figure-value">{figure.value}</div>
            <div className="rs-figure-detail">{figure.detail}</div>
          </div>
        ))}
      </section>

      <div className="rs-filters">
        {props.filters.map((filter) => {
          const first = filter.options[0]?.value ?? "";

          return (
            <label key={filter.name} className="rs-chip">
              <span>{filter.label}:</span>
              <select
                value={params.get(filter.name) ?? first}
                onChange={(event) => choose(filter.name, event.target.value, first)}
              >
                {filter.options.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
          );
        })}
        <input
          type="search"
          className="rs-find"
          aria-label={props.find}
          placeholder={props.find}
          defaultValue={params.get("search") ?? ""}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              choose("search", event.currentTarget.value.trim(), "");
            }
          }}
          onBlur={(event) => choose("search", event.currentTarget.value.trim(), "")}
        />
      </div>

      <section className="rs-panel" aria-labelledby="rs-list-title">
        <header>
          <h2 id="rs-list-title">{props.listName}</h2>
          <HelpTip label={props.listName}>{props.listHelp}</HelpTip>
          <span className="rs-meta">
            {props.loading && props.items.length === 0 ? "Loading…" : `${props.total.toLocaleString("en")} in all`}
          </span>
        </header>
        {props.error && (
          <p className="rs-error" role="alert">
            {props.error}
          </p>
        )}
        <div className="rs-scroll">
          <table>
            <thead>
              <tr>
                {props.columns.map((column) => (
                  <th
                    key={column.title}
                    scope="col"
                    className={[
                      column.numeric ? "num" : "",
                      column.wide ? "wide" : "",
                      column.advanced ? "advanced-only" : "",
                    ]
                      .filter(Boolean)
                      .join(" ")}
                  >
                    {column.title}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {props.items.map((item) => (
                <tr key={props.keyOf(item)}>
                  {props.columns.map((column, index) => (
                    <td
                      key={column.title}
                      className={[
                        column.numeric ? "num" : "",
                        column.wide ? "wide" : "",
                        column.advanced ? "advanced-only" : "",
                      ]
                        .filter(Boolean)
                        .join(" ")}
                    >
                      {index === 0 ? (
                        <Link className="rs-name" to={props.linkOf(item)}>
                          {column.cell(item)}
                        </Link>
                      ) : (
                        column.cell(item)
                      )}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          {!props.loading && props.items.length === 0 && !props.error && (
            <p className="rs-empty">Nothing matches these filters.</p>
          )}
        </div>
        <footer>
          <span>{props.note}</span>
          {props.more && (
            <button type="button" className="link-button" disabled={props.loading} onClick={props.onMore}>
              Load more
            </button>
          )}
        </footer>
      </section>
    </main>
  );
}

/** Props for {@link HealthPill}. */
interface HealthPillProps {
  /** The health from the alert rules. */
  health: BlockHealth;
  /** Whether nothing arrived for a while, which says more than the health. */
  silent: boolean;
  /**
   * The item's name, as its alerts name it. A warning or caution then opens its open alerts, each with what it means
   * and how to fix it (VEY-83).
   */
  name?: string;
}

/**
 * The open alerts of an item, for a warning or caution that names no alert of its own.
 *
 * @param name - The item, as its alerts name it.
 * @returns The Alerts list, open alerts only, found by the name.
 */
export function openAlertsOf(name: string): string {
  return `/alerts?${new URLSearchParams({ state: "open", q: name }).toString()}`;
}

/** An item's health in a word: Warning, Caution, Healthy, Unknown, or Silent; a warning or caution opens its alerts. */
export function HealthPill({ health, silent, name }: HealthPillProps) {
  if (silent) {
    return <span className="rs-pill off">Silent</span>;
  }

  const words: Record<BlockHealth, [string, string]> = {
    Warning: ["Warning", "bad"],
    Caution: ["Caution", "warn"],
    Ok: ["Healthy", "ok"],
    Unknown: ["Unknown", "off"],
  };
  const [word, tone] = words[health];

  if (name && (health === "Warning" || health === "Caution")) {
    return (
      <Link className={`rs-pill rs-pill-link ${tone}`} to={openAlertsOf(name)} title={`Open alerts of ${name}`}>
        {word}
      </Link>
    );
  }

  return <span className={`rs-pill ${tone}`}>{word}</span>;
}

/** Props for {@link Share}. */
interface ShareProps {
  /** The share, from 0 to 1, or null when not known. */
  ratio: number | null;
}

/** A share as a percentage with a small bar: amber from 70 %, red from 85 %. */
export function Share({ ratio }: ShareProps) {
  if (ratio === null) {
    return <span className="rs-none">—</span>;
  }

  const percent = Math.round(ratio * 100);
  const tone = percent >= 85 ? "bad" : percent >= 70 ? "warn" : "";

  return (
    <span className="rs-share">
      {percent}%
      <span className={["rs-bar", tone].filter(Boolean).join(" ")} aria-hidden="true">
        <i style={{ width: `${Math.min(100, percent)}%` }} />
      </span>
    </span>
  );
}
