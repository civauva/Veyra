import type { Look } from "../../app/look";
import { OverviewScreen } from "../../overview/OverviewScreen";
import { ConsoleAlert } from "./alerts/ConsoleAlert";
import { ConsoleAlerts } from "./ConsoleAlerts";
import { ConsoleLogs } from "./ConsoleLogs";
import { ConsoleDatabase } from "./database/ConsoleDatabase";
import { ConsoleIncident } from "./incidents/ConsoleIncident";
import { ConsoleIncidents } from "./incidents/ConsoleIncidents";
import { ConsoleIntegration } from "./integrations/ConsoleIntegration";
import { ConsoleIntegrationNew } from "./integrations/ConsoleIntegrationNew";
import { ConsoleIntegrations } from "./integrations/ConsoleIntegrations";
import { ConsoleJob } from "./jobs/ConsoleJob";
import { ConsoleJobs } from "./jobs/ConsoleJobs";
import { ConsoleMetrics } from "./metrics/ConsoleMetrics";
import { consoleNav } from "./nav";
import { ConsoleSettings } from "./settings/ConsoleSettings";
import { Shell } from "./Shell";
import { ConsoleTrace } from "./traces/ConsoleTrace";
import { ConsoleTraces } from "./traces/ConsoleTraces";

/**
 * Console: a dense operations console (VEY-69), the menu grouped by what people do, with the Overview as its home and
 * a Log explorer (VEY-74), a SQL Server screen (VEY-75), jobs and pipelines (ADR 0027), integrations with their wizard
 * and package page (ADR 0028), Alerts and an alert, Traces and a trace, Metrics, and Settings of its own (ADR 0029),
 * without Forecast's weather. It is the default look.
 */
export const consoleLook: Look = {
  id: "console",
  name: "Console",
  Shell,
  nav: consoleNav,
  screens: {
    home: OverviewScreen,
    logs: ConsoleLogs,
    traces: ConsoleTraces,
    trace: ConsoleTrace,
    metrics: ConsoleMetrics,
    database: ConsoleDatabase,
    jobs: ConsoleJobs,
    job: ConsoleJob,
    incidents: ConsoleIncidents,
    incident: ConsoleIncident,
    alerts: ConsoleAlerts,
    alert: ConsoleAlert,
    integrations: ConsoleIntegrations,
    "integration-new": ConsoleIntegrationNew,
    integration: ConsoleIntegration,
    settings: ConsoleSettings,
  },
};
