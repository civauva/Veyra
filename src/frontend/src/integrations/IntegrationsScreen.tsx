import { Link } from "react-router";

import { serverStateWords } from "../settings/monitoringFormat";
import { HelpTip } from "../ui/HelpTip";
import { ScreenBar } from "../ui/ScreenBar";
import { integrationPath, modulesWords, versionWords } from "./integrationWords";
import { useIntegrations } from "./useIntegrations";
import "./integrations.css";

/**
 * The integrations (ADR 0028) as every look shows them by default: each monitored SQL Server with its state, its
 * template version against the current one, its modules, and its agent, leading to its package page. The Console look
 * draws its own, with the wizard.
 */
export function IntegrationsScreen() {
  const { list, error } = useIntegrations();

  return (
    <main className="ints">
      <ScreenBar trail={[{ label: "Integrations" }]} />
      <h1>Integrations</h1>
      <p className="ints-lead">
        Each SQL Server Veyra&apos;s agents read, with the template it is read with and its modules. Servers are added
        and their connections changed on <Link to="/settings?section=Databases">Settings → Database monitoring</Link>.
      </p>
      {error && (
        <p className="ints-error" role="alert">
          Could not refresh: {error}
        </p>
      )}
      <section className="ints-section" aria-labelledby="ints-list">
        <h2 id="ints-list">
          SQL Servers
          <HelpTip label="SQL Servers">
            Every SQL Server with how its monitoring stands, the template version it is read with against the current
            one, and how many of its modules are on. Open one for its package page.
          </HelpTip>
        </h2>
        {list === null ? (
          <p>Reading the integrations…</p>
        ) : list.items.length === 0 ? (
          <p>No SQL Server is monitored yet.</p>
        ) : (
          <ul className="ints-list">
            {list.items.map((item) => {
              const state = serverStateWords(item.state);
              const version = versionWords(item);

              return (
                <li key={item.id} className={state.tone === "bad" ? "bad" : undefined}>
                  <Link to={integrationPath(item.id)}>{item.name}</Link>
                  <small>
                    {state.label} · template {version.text} · {modulesWords(item.modules)} modules · read by{" "}
                    {item.agent.name} · {item.environment}
                  </small>
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </main>
  );
}
