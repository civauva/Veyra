import { Link } from "react-router";

import { ScreenBar } from "../ui/ScreenBar";
import "./integrations.css";

/**
 * Adding a SQL Server as every look shows it by default: the Settings screen's database monitoring adds servers there
 * (ADR 0028). The Console look draws the wizard instead.
 */
export function IntegrationNewScreen() {
  return (
    <main className="ints">
      <ScreenBar trail={[{ label: "Integrations", to: "/integrations" }, { label: "Add a SQL Server" }]} />
      <h1>Add a SQL Server</h1>
      <p className="ints-lead">
        In this look, SQL Servers are added on the Settings screen: where the server is, the login Veyra uses with its
        script, and a check that follows the agent&apos;s report. Its package page then shows the template, the modules,
        and the checks.
      </p>
      <p>
        <Link className="button primary" to="/settings?section=Databases">
          Open Database monitoring
        </Link>
      </p>
    </main>
  );
}
