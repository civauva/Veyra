import type { AlertListItem, BlockHealth, DatabaseMetrics, MetricRange } from "../../api/types";
import { titleOnScreen } from "../../alerts/ruleWords";
import { HelpTip } from "../../ui/HelpTip";
import { WeatherIcon, type WeatherKind } from "../../home/WeatherIcon";
import { formatAmount, formatClockTime, formatRate, formatShare, formatSpan } from "../metricFormat";
import { rangeWords } from "../metricRange";
import type { AreaId, AreaState } from "./areas";
import "./overview.css";

/** Props for {@link OverviewTab}. */
export interface OverviewTabProps {
  /** The server's figures over the range, once loaded. */
  database: DatabaseMetrics | null;
  /**
   * The areas the view has, the worst first as the server ranks them from the open alerts (VEY-68), each with its
   * weather unknown when the alerts could not be read; null while they are read.
   */
  states: AreaState[] | null;
  /** The range the figures cover. */
  range: MetricRange;
  /**
   * Opens an area's tab.
   *
   * @param area - The area, by its tab's name in the address.
   */
  onOpen: (area: AreaId) => void;
}

/**
 * A SQL Server's Overview tab (VEY-62), the first in both views: one weather card for each area, the worst first, so
 * what is wrong with the server shows at a glance and one click opens it. The weather comes from the server's open
 * alerts only, as on the home (ADR 0007); the figure and the sentence come from the screen's figures.
 */
export function OverviewTab({ database, states, range, onOpen }: OverviewTabProps) {
  // The cards wait for the alerts, so they do not move about as the weather arrives.
  if (states === null) {
    return <p className="metrics-empty">Reading the server&apos;s alerts…</p>;
  }

  // The cards tell of the applications' databases, jobs, and statements unless System is on, as the server sent them
  // (VEY-64, VEY-66); the weather still comes from every alert.
  const figures = database;

  return (
    <ul className="db-areas" aria-label="Areas">
      {states.map((state) => (
        <AreaCard key={state.area} state={state} database={figures} range={range} onOpen={onOpen} />
      ))}
    </ul>
  );
}

/** Props for {@link AreaCard}. */
interface AreaCardProps {
  /** How the area is. */
  state: AreaState;
  /** The server's figures. */
  database: DatabaseMetrics | null;
  /** The range the figures cover. */
  range: MetricRange;
  /** Opens the area's tab. */
  onOpen: (area: AreaId) => void;
}

/**
 * One area as a weather card: its picture, its name, a key figure, and one sentence. The whole card opens the area's
 * tab through a button laid over it, under the name's row, since a button cannot hold the question mark's.
 */
function AreaCard({ state, database, range, onOpen }: AreaCardProps) {
  const { label, help } = areaWords[state.area];
  const { figure, sentence } = describe(state, database, range);
  const weather = weatherOf(state.health);

  return (
    <li className={`db-area ${weather.tone}`}>
      <WeatherIcon kind={weather.icon} size={30} className="db-area-icon" />
      <div className="db-area-name">
        <h2>{label}</h2>
        <HelpTip label={`the ${label} card`}>{help}</HelpTip>
      </div>
      <b className="db-area-figure">{figure}</b>
      <p>{sentence}</p>
      <button
        type="button"
        className="db-area-open"
        aria-label={`${label}, ${weather.word}: ${figure}. Open the tab`}
        onClick={() => onOpen(state.area)}
      >
        <span className="db-area-go" aria-hidden="true">
          ›
        </span>
      </button>
    </li>
  );
}

/** Each area's name and what its card tells, behind its question mark. */
const areaWords: Record<AreaId, { label: string; help: string }> = {
  live: {
    label: "Live",
    help:
      "What runs now: batch requests a second and the requests that have run for over 10 seconds. It turns " +
      "showery or stormy when a session fills tempdb. Opens the Live tab, read every 10 seconds.",
  },
  queries: {
    label: "Queries",
    help:
      "The statements over the range. It turns showery or stormy when a statement's plan got slower or a query " +
      "runs long. Opens the Queries tab, with the statements worth a look.",
  },
  waits: {
    label: "Waits",
    help:
      "What sessions waited on most over the range, by category. No rule watches the waits; they explain why " +
      "the other areas are slow. Opens the Waits tab.",
  },
  blocking: {
    label: "Blocking",
    help:
      "Sessions held up by others now, and deadlocks in the last 15 minutes. It turns showery or stormy on the " +
      "blocking, deadlock, head blocker, and open transaction rules. Opens the Blocking tab.",
  },
  databases: {
    label: "Databases",
    help:
      "The databases' backups, logs, files, volumes, and integrity checks. It turns showery or stormy on the " +
      "backup, log, volume, CHECKDB, damaged page, and offline rules. Opens the Databases tab.",
  },
  jobs: {
    label: "Jobs",
    help:
      "SQL Server Agent and its jobs' last runs. It turns showery or stormy when a job fails, runs long, does not " +
      "run, or the Agent stops. Opens the Jobs tab.",
  },
  events: {
    label: "Events",
    help:
      "What SQL Server reported: severe and memory errors, memory dumps, and failed logins. It turns showery or " +
      "stormy on their rules. Opens the Events tab.",
  },
  server: {
    label: "Server",
    help:
      "The machine and the instance: CPU, memory, worker threads, memory grants, restarts, support, connections, " +
      "availability groups, and log shipping. It turns showery or stormy on their rules. Opens the Server tab.",
  },
};

/** The weather of a health: its picture, its word, and its colour. */
function weatherOf(health: BlockHealth): { icon: WeatherKind; word: string; tone: string } {
  switch (health) {
    case "Warning":
      return { icon: "storm", word: "storm", tone: "warning" };
    case "Caution":
      return { icon: "shower", word: "showers", tone: "caution" };
    case "Ok":
      return { icon: "sun", word: "fine", tone: "ok" };
    case "Unknown":
      return { icon: "cloud", word: "not known", tone: "unknown" };
  }
}

/** An area's key figure and its sentence. */
interface Description {
  /** A few words, large. */
  figure: string;
  /** One plain sentence. */
  sentence: string;
}

/**
 * What an area's card says: from its worst alert when it has one, from the server's figures when not.
 *
 * @param state - How the area is.
 * @param database - The server's figures, or null before they arrive.
 * @param range - The range they cover.
 * @returns The figure and the sentence.
 */
function describe(state: AreaState, database: DatabaseMetrics | null, range: MetricRange): Description {
  const alerts = state.alerts;
  const told = tellAlerts(alerts);

  if (database === null) {
    return {
      figure: alerts.length > 0 ? count(alerts.length, "alert") : "–",
      sentence: told ?? "Reading the server…",
    };
  }

  const { card } = database;

  switch (state.area) {
    case "live": {
      const [longest] = database.running;

      return {
        figure: `${formatRate(card.batchRequests)} req/s`,
        sentence:
          told ??
          (longest
            ? `${count(database.running.length, "request")} running over 10 s; the longest for ` +
              `${formatSpan(longest.elapsedSeconds)}${longest.database ? ` in ${longest.database}` : ""}.`
            : "Quiet; nothing runs long."),
      };
    }
    case "queries": {
      const [busiest] = database.topQueries;
      const inDatabase = busiest?.database ? `, in ${busiest.database}` : "";

      return {
        figure: alerts.length > 0 ? `${alerts.length} to look at` : "no regression",
        sentence:
          told ??
          (busiest
            ? `The busiest statement ran ${count(busiest.executions, "time")} ` +
              `for ${formatSpan(busiest.totalSeconds)} ${rangeWords(range, "in")}${inDatabase}.`
            : `No statement stood out ${rangeWords(range, "in")}.`),
      };
    }
    case "waits": {
      // The server works out the wait that took the most time and its share (VEY-68).
      const most = database.overview.topWait;

      return most
        ? {
            figure: `mostly ${most.label}`,
            sentence: `${most.label} is ${formatShare(most.share)} of the time waited ${rangeWords(range, "in")}.`,
          }
        : { figure: "no waits", sentence: `Nothing waited ${rangeWords(range, "in")}.` };
    }
    case "blocking": {
      const blocked = card.blocked ?? 0;
      const deadlocks = card.deadlocks ?? 0;

      return {
        figure:
          blocked > 0
            ? `${blocked} blocked`
            : alerts.length > 0
              ? count(alerts.length, "alert")
              : deadlocks > 0
                ? count(deadlocks, "deadlock")
                : "none blocked",
        sentence:
          told ??
          (blocked > 0
            ? `${count(blocked, "session")} ${blocked === 1 ? "waits" : "wait"} for a lock now.`
            : deadlocks > 0
              ? `${count(deadlocks, "deadlock")} in the last 15 minutes; nobody waits now.`
              : "Nobody waits for a lock; no deadlocks in 15 minutes."),
      };
    }
    case "databases": {
      const late = database.overview.lateBackups;

      return {
        figure:
          alerts.length > 0
            ? `${alerts.length} ${alerts.length === 1 ? "needs" : "need"} doing`
            : count(database.databases.length, "database"),
        sentence:
          told ??
          (late.length > 0
            ? `No recent full backup of ${late.join(", ")}.`
            : "Every database has a recent full backup."),
      };
    }
    case "jobs": {
      const failed = database.overview.failedJobs;
      const [first] = failed;

      return {
        figure:
          failed.length > 0
            ? `${failed.length} failed`
            : alerts.length > 0
              ? count(alerts.length, "alert")
              : database.jobs.length > 0
                ? "all ran"
                : "no jobs",
        sentence:
          told ??
          (first
            ? `${first.name} failed${first.lastRun ? ` at ${formatClockTime(first.lastRun)}` : ""}` +
              `${failed.length > 1 ? `; ${failed.length - 1} more failed too` : ""}.`
            : database.jobs.length > 0
              ? `${count(database.jobs.length, "job")}; the last run of each succeeded.`
              : "SQL Server Agent runs no jobs here."),
      };
    }
    case "events":
      return {
        figure: alerts.length > 0 ? count(alerts.length, "alert") : "quiet",
        sentence: told ?? "No severe or memory errors, memory dumps, or bursts of failed logins.",
      };
    case "server": {
      const facts = [
        card.cpus ? `${card.cpus} CPUs` : null,
        card.memoryBytes ? `${formatAmount(card.memoryBytes)} in use` : null,
        card.uptimeSeconds ? `up ${formatSpan(card.uptimeSeconds)}` : null,
      ].filter(Boolean);

      return {
        figure: alerts.length > 0 ? count(alerts.length, "alert") : "fine",
        sentence: told ?? (facts.length > 0 ? `${facts.join(" · ")}.` : "Nothing out of the ordinary."),
      };
    }
  }
}

/**
 * An area's worst alert in a sentence, without the server's name, which the screen already says, and how many more
 * are open.
 *
 * @param alerts - The area's open alerts, the worst first.
 * @returns The sentence, or null without alerts.
 */
function tellAlerts(alerts: AlertListItem[]): string | null {
  const [worst] = alerts;

  if (!worst) {
    return null;
  }

  const sentence = titleOnScreen(worst);
  const more = alerts.length > 1 ? ` ${alerts.length - 1} more open.` : "";

  return `${sentence}.${more}`;
}

/** A count with its noun, `1 alert` or `3 alerts`. */
function count(value: number, noun: string): string {
  return `${value.toLocaleString("en-US")} ${noun}${value === 1 ? "" : "s"}`;
}
