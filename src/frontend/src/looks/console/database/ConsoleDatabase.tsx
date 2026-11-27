import { Link, useParams, useSearchParams } from "react-router";

import type { ServerMachine } from "../../../api/types";
import { formatBytes } from "../../../home/homeFormat";
import { DatabaseScreen } from "../../../metrics/DatabaseScreen";
import { BlockingTab } from "../../../metrics/database/BlockingTab";
import { EventsTab } from "../../../metrics/database/EventsTab";
import { JobsTab } from "../../../metrics/database/JobsTab";
import { LiveTab } from "../../../metrics/database/LiveTab";
import { tabsLeftOut } from "../../../metrics/database/platforms";
import { isPostgres } from "../../../metrics/database/postgres";
import { QueriesTab } from "../../../metrics/database/QueriesTab";
import { ServerTab } from "../../../metrics/database/ServerTab";
import { StorageTab } from "../../../metrics/database/StorageTab";
import { useSystemShown } from "../../../metrics/database/SystemFilter";
import { useOpenAlerts } from "../../../metrics/database/useOpenAlerts";
import { useServerBackups, useServerOverview } from "../../../metrics/database/useServerOverview";
import { rangeOf } from "../../../metrics/metricRange";
import { ScreenBar } from "../../../ui/ScreenBar";
import { useViewMode } from "../../../ui/ViewToggle";
import { SqlBackupsTab } from "./SqlBackupsTab";
import { SqlBlockingTab } from "./SqlBlockingTab";
import { SqlOverviewTab } from "./SqlOverviewTab";
import { SqlStorageTab } from "./SqlStorageTab";
import { productName, timeWords } from "./sqlParts";
import "./consoleDatabase.css";

/** Console's tabs of a SQL Server, in the order of reference screenshots 1 and 3. */
const tabs = [
  { id: "overview", label: "Overview" },
  { id: "blocking", label: "Blocking" },
  { id: "backups", label: "Backups" },
  { id: "storage", label: "Storage" },
  { id: "live", label: "Live" },
  { id: "queries", label: "Queries" },
  { id: "jobs", label: "Jobs" },
  { id: "events", label: "Events" },
  { id: "server", label: "Server" },
] as const;

/** One of {@link tabs}. */
type TabId = (typeof tabs)[number]["id"];

/**
 * Console's SQL Server screen (VEY-75, reference screenshots 1, 3, and 8): the machine in the header, then Overview,
 * Blocking, Backups, and Storage drawn from one overview read and the backup history (ADR 0025), and the tabs every
 * look shares for Live, Queries, Jobs, Events, and Server. A PostgreSQL server keeps the shared screen.
 */
export function ConsoleDatabase() {
  const { engine = "" } = useParams();

  return isPostgres(engine) ? <DatabaseScreen /> : <SqlServerScreen />;
}

/** The SQL Server screen itself. */
function SqlServerScreen() {
  const { engine = "", server = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeOf(params);
  const [system] = useSystemShown();
  const [mode, setMode] = useViewMode();
  const advanced = mode === "advanced";
  const overview = useServerOverview(engine, server, range, system);
  const { alerts } = useOpenAlerts("database", `${engine} ${server}`);
  const leftOut = tabsLeftOut(overview.data?.machine.platform);
  const shown = tabs.filter((one) => !leftOut?.tabs.includes(one.id));
  const tab: TabId = shown.find((one) => one.id === params.get("tab"))?.id ?? "overview";
  const backups = useServerBackups(engine, server, range, system);
  const warnings = alerts?.filter((alert) => alert.severity === "Warning").length ?? 0;
  const open = alerts?.length ?? 0;

  function go(next: TabId, extra: Record<string, string | null> = {}) {
    setParams(
      (current) => {
        const changed = new URLSearchParams(current);

        if (next === "overview") {
          changed.delete("tab");
        } else {
          changed.set("tab", next);
        }

        Object.entries(extra).forEach(([key, value]) =>
          value === null ? changed.delete(key) : changed.set(key, value),
        );

        return changed;
      },
      { replace: true },
    );
  }

  return (
    <main className="cn-screen sq" data-view={mode}>
      <ScreenBar
        trail={[
          { label: "Databases", to: "/resources/databases" },
          // The open tab ends the trail, and the server's name leads back to its overview (VEY-83).
          tab === "overview"
            ? { label: server }
            : {
                label: server,
                to: `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}`,
              },
          ...(tab === "overview" ? [] : [{ label: tabs.find((one) => one.id === tab)?.label ?? tab }]),
        ]}
        view
      />
      <div className="sq-head">
        <span className="sq-icon" aria-hidden="true">
          ▤
        </span>
        <div>
          <h1>
            {server}
            {alerts &&
              (open === 0 ? (
                <span className="cn-pill ok">Healthy</span>
              ) : (
                // The server's open alerts, each with what it means and how to fix it (VEY-83).
                <Link
                  className={warnings > 0 ? "cn-pill bad" : "cn-pill warn"}
                  to={`/alerts?${new URLSearchParams({ state: "open", q: server }).toString()}`}
                >
                  {`${warnings > 0 ? "Warning" : "Caution"} · ${open} open`}
                </Link>
              ))}
          </h1>
          {overview.data && <MachineFacts machine={overview.data.machine} />}
        </div>
        <Link className="cn-button sq-monitoring" to="/settings?section=Databases">
          Monitoring settings
        </Link>
      </div>

      <nav className="cn-tabs sq-tabs" aria-label="Server">
        {shown.map((one) => (
          <button
            key={one.id}
            type="button"
            aria-current={tab === one.id ? "page" : undefined}
            onClick={() => go(one.id)}
          >
            {one.label}
          </button>
        ))}
      </nav>

      {overview.error && (
        <p className="cn-error" role="alert">
          Could not refresh: {overview.error}
        </p>
      )}
      {overview.notFound && <p className="cn-empty">{server} has sent nothing yet. Its agent reads it every minute.</p>}

      {tab === "overview" && overview.data && (
        <SqlOverviewTab overview={overview.data} backups={backups.data} alerts={alerts} onTab={(next) => go(next)} />
      )}
      {tab === "blocking" && overview.data && (
        <>
          <SqlBlockingTab overview={overview.data} onLive={() => go("live")} />
          <BlockingTab
            engine={engine}
            server={server}
            range={range}
            advanced={advanced}
            onMoment={(at) => go("live", { at })}
          />
        </>
      )}
      {tab === "backups" && (
        <SqlBackupsTab backups={backups.data} error={backups.error} block={`${engine} ${server}`} />
      )}
      {tab === "storage" && overview.data && (
        <>
          <SqlStorageTab overview={overview.data} block={`${engine} ${server}`} />
          <StorageTab engine={engine} server={server} advanced={advanced} blind={false} />
        </>
      )}
      {tab === "live" && (
        <LiveTab
          engine={engine}
          server={server}
          at={params.get("at")}
          advanced={advanced}
          onNow={() => go("live", { at: null })}
          onAdvanced={() => setMode("advanced")}
        />
      )}
      {tab === "queries" && <QueriesTab engine={engine} server={server} range={range} advanced={advanced} />}
      {tab === "jobs" && <JobsTab engine={engine} server={server} advanced={advanced} blind={false} />}
      {tab === "events" && <EventsTab engine={engine} server={server} range={range} advanced={advanced} />}
      {tab === "server" && (
        <ServerTab engine={engine} server={server} range={range} advanced={advanced} blind={false} />
      )}
    </main>
  );
}

/** The instance and its machine in one line, as the reference's header: version, edition, OS, CPUs, memory, uptime. */
function MachineFacts({ machine }: { machine: ServerMachine }) {
  const facts = [
    machine.version && productName(machine.version, machine.updateLevel),
    machine.edition,
    machine.osName && [machine.osName, machine.osRelease && `(${machine.osRelease})`].filter(Boolean).join(" "),
    [machine.cpus && `${machine.cpus} vCPU`, machine.memoryBytes && `${formatBytes(machine.memoryBytes)} RAM`]
      .filter(Boolean)
      .join(" · "),
    machine.uptimeSeconds !== null && `Up ${timeWords(machine.uptimeSeconds)}`,
    `${machine.databases} ${machine.databases === 1 ? "database" : "databases"}`,
  ].filter((fact): fact is string => typeof fact === "string" && fact.length > 0);

  return (
    <p className="sq-facts">
      {facts.map((fact, index) => (
        <span key={index}>{fact}</span>
      ))}
    </p>
  );
}
