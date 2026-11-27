import { Link, useSearchParams } from "react-router";

import type { IntegrationItem } from "../../../api/types";
import { carryTime } from "../../../time/carryTime";
import {
  integrationPath,
  modulesWords,
  newIntegrationPath,
  versionWords,
} from "../../../integrations/integrationWords";
import { useIntegrations } from "../../../integrations/useIntegrations";
import { formatClockTime } from "../../../metrics/metricFormat";
import { serverStateWords } from "../../../settings/monitoringFormat";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { ConsoleIcon } from "../ConsoleIcon";
import "./consoleIntegrations.css";

/**
 * Console's integrations (VEY-78, ADR 0028): each monitored SQL Server with how it stands, its template version
 * against the current one, its modules, and its agent, each opening its package page, and Add SQL Server for the
 * wizard. The server orders the list.
 */
export function ConsoleIntegrations() {
  const { list, error } = useIntegrations();
  const [params] = useSearchParams();

  return (
    <main className="cn-screen ig">
      <ScreenBar trail={[{ label: "Manage" }, { label: "Integrations" }]} />
      <div className="cn-head">
        <span className="ig-icon" aria-hidden="true">
          <ConsoleIcon name="integrations" size={22} />
        </span>
        <div className="ig-head-text">
          <h1>Integrations</h1>
          <p>
            What Veyra&apos;s agents read for you, each from a versioned template with its own modules and intervals.
          </p>
        </div>
        <div className="cn-head-actions">
          <Link className="cn-button primary" to={newIntegrationPath}>
            + Add SQL Server
          </Link>
        </div>
      </div>

      {error && (
        <p className="cn-error" role="alert">
          Could not refresh: {error}
        </p>
      )}

      <section className="cn-card" aria-labelledby="ig-list">
        <header>
          <ConsoleIcon name="database" size={15} />
          <h2 id="ig-list">SQL Servers</h2>
          <HelpTip label="SQL Servers">
            Every SQL Server Veyra&apos;s agents read: how its monitoring stands, the template version it is read with
            against the current one, how many of its modules are on, and the agent that reads it. Open one for its
            package page with the checks, the Collector, and Update or Roll back.
          </HelpTip>
          {list && (
            <span className="cn-meta">
              {list.items.length} {list.items.length === 1 ? "server" : "servers"}
            </span>
          )}
        </header>
        {list === null ? (
          <p className="cn-empty">Reading the integrations…</p>
        ) : list.items.length === 0 ? (
          <div className="ig-empty">
            <p className="cn-empty">
              No SQL Server is monitored yet. Add one: the wizard tests the connection through an agent before anything
              is saved.
            </p>
            <Link className="cn-button primary" to={newIntegrationPath}>
              + Add SQL Server
            </Link>
          </div>
        ) : (
          <div className="cn-table-scroll">
            <table className="cn-table ig-table">
              <thead>
                <tr>
                  <th scope="col">Integration</th>
                  <th scope="col">State</th>
                  <th scope="col">Template</th>
                  <th scope="col" className="wide">
                    Modules
                  </th>
                  <th scope="col" className="wide">
                    Read by
                  </th>
                  <th scope="col" className="wide roomy">
                    Changed
                  </th>
                </tr>
              </thead>
              <tbody>
                {list.items.map((item) => (
                  <Row key={item.id} item={item} params={params} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </main>
  );
}

/** One integration in the table. */
function Row({ item, params }: { item: IntegrationItem; params: URLSearchParams }) {
  const state = serverStateWords(item.state);
  const version = versionWords(item);

  return (
    <tr className={state.tone === "bad" ? "ig-failing" : undefined}>
      <td>
        <Link className="cn-table-name" to={carryTime(integrationPath(item.id), params)}>
          {item.name}
        </Link>
        <span className="cn-table-sub">SQL Server · {item.environment}</span>
      </td>
      <td>
        <span className={`cn-pill ${state.tone === "wait" ? "info" : state.tone}`}>{state.label}</span>
      </td>
      <td>
        <span className="mono">{item.version}</span>
        <span className={version.behind ? "cn-table-sub ig-behind" : "cn-table-sub"}>
          {version.behind ? `${item.latestVersion} available` : "current"}
        </span>
      </td>
      <td className="wide">
        {modulesWords(item.modules)}
        <span className="cn-table-sub">modules on</span>
      </td>
      <td className="wide">{item.agent.name}</td>
      <td className="wide roomy sq-muted">{formatClockTime(item.changedAt)}</td>
    </tr>
  );
}
