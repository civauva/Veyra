import type { ComponentType } from "react";

import { AccountScreen } from "../account/AccountScreen";
import { AlertScreen } from "../alerts/AlertScreen";
import { AlertsScreen } from "../alerts/AlertsScreen";
import { IncidentScreen } from "../incidents/IncidentScreen";
import { IncidentsScreen } from "../incidents/IncidentsScreen";
import { IntegrationNewScreen } from "../integrations/IntegrationNewScreen";
import { IntegrationScreen } from "../integrations/IntegrationScreen";
import { IntegrationsScreen } from "../integrations/IntegrationsScreen";
import { JobScreen } from "../jobs/JobScreen";
import { JobsScreen } from "../jobs/JobsScreen";
import { KeysScreen } from "../keys/KeysScreen";
import { LogExplorer } from "../logs/LogExplorer";
import { DatabaseScreen } from "../metrics/DatabaseScreen";
import { QueryScreen } from "../metrics/database/QueryScreen";
import { HostScreen } from "../metrics/HostScreen";
import { MetricsScreen } from "../metrics/MetricsScreen";
import { ServiceScreen } from "../metrics/ServiceScreen";
import { NotificationsScreen } from "../notifications/NotificationsScreen";
import { DatabasesScreen } from "../resources/DatabasesScreen";
import { HostsScreen } from "../resources/HostsScreen";
import { ServicesScreen } from "../resources/ServicesScreen";
import { SettingsScreen } from "../settings/SettingsScreen";
import { TraceScreen } from "../traces/TraceScreen";
import { TracesScreen } from "../traces/TracesScreen";
import type { NavIconName } from "../ui/NavIcon";
import { UsersScreen } from "../users/UsersScreen";

/** Every destination in the app, by a name that does not change when its path or title does. */
export type RouteId =
  | "home"
  | "logs"
  | "traces"
  | "trace"
  | "metrics"
  | "services"
  | "hosts"
  | "databases"
  | "jobs"
  | "job"
  | "host"
  | "database"
  | "query"
  | "service"
  | "incidents"
  | "incident"
  | "alerts"
  | "alert"
  | "integrations"
  | "integration-new"
  | "integration"
  | "keys"
  | "users"
  | "notifications"
  | "settings"
  | "account";

/**
 * Where a route is reached from. Every look puts each `menu` route in its menu exactly once (a test checks it);
 * `account` is reached from the person's account menu, and `detail` from a row or a link on another screen.
 */
export type RoutePlace = "menu" | "account" | "detail";

/** One destination (ADR 0021): the same path in every look, so a shared link opens in whatever look the reader uses. */
export interface AppRoute {
  /** Its stable name. */
  id: RouteId;
  /** Its path, with React Router parameters; "/" is the home. */
  path: string;
  /** Its name in a menu, unless a look names it otherwise. */
  title: string;
  /** Its picture in a menu. */
  icon: NavIconName;
  /** Where it is reached from. */
  place: RoutePlace;
  /** Whether only Admins may open it (ADR 0008); for others the path falls through to the home. */
  admin?: boolean;
  /** Its screen; left out only for the home, which each look brings. */
  screen?: ComponentType;
}

/** Every destination, in menu order. */
export const routes: AppRoute[] = [
  { id: "home", path: "/", title: "Home", icon: "home", place: "menu" },
  { id: "logs", path: "/logs", title: "Logs", icon: "logs", place: "menu", screen: LogExplorer },
  { id: "traces", path: "/traces", title: "Traces", icon: "traces", place: "menu", screen: TracesScreen },
  { id: "trace", path: "/traces/:traceId", title: "Trace", icon: "traces", place: "detail", screen: TraceScreen },
  { id: "metrics", path: "/metrics", title: "Metrics", icon: "metrics", place: "menu", screen: MetricsScreen },
  {
    id: "services",
    path: "/resources/services",
    title: "Services",
    icon: "services",
    place: "menu",
    screen: ServicesScreen,
  },
  {
    id: "hosts",
    path: "/resources/hosts",
    title: "Hosts & containers",
    icon: "hosts",
    place: "menu",
    screen: HostsScreen,
  },
  {
    id: "databases",
    path: "/resources/databases",
    title: "Databases",
    icon: "databases",
    place: "menu",
    screen: DatabasesScreen,
  },
  {
    id: "jobs",
    path: "/jobs",
    title: "Jobs & pipelines",
    icon: "jobs",
    place: "menu",
    screen: JobsScreen,
  },
  { id: "job", path: "/jobs/:id", title: "Job", icon: "jobs", place: "detail", screen: JobScreen },
  { id: "host", path: "/metrics/hosts/:host", title: "Host", icon: "metrics", place: "detail", screen: HostScreen },
  {
    id: "database",
    path: "/metrics/databases/:engine/:server",
    title: "Database server",
    icon: "metrics",
    place: "detail",
    screen: DatabaseScreen,
  },
  {
    id: "query",
    path: "/metrics/databases/:engine/:server/queries/:hash",
    title: "Query",
    icon: "metrics",
    place: "detail",
    screen: QueryScreen,
  },
  {
    id: "service",
    path: "/metrics/services/:service",
    title: "Service",
    icon: "metrics",
    place: "detail",
    screen: ServiceScreen,
  },
  {
    id: "incidents",
    path: "/incidents",
    title: "Incidents",
    icon: "incidents",
    place: "menu",
    screen: IncidentsScreen,
  },
  {
    id: "incident",
    path: "/incidents/:id",
    title: "Incident",
    icon: "incidents",
    place: "detail",
    screen: IncidentScreen,
  },
  { id: "alerts", path: "/alerts", title: "Alerts", icon: "alerts", place: "menu", screen: AlertsScreen },
  { id: "alert", path: "/alerts/:id", title: "Alert", icon: "alerts", place: "detail", screen: AlertScreen },
  {
    id: "integrations",
    path: "/integrations",
    title: "Integrations",
    icon: "integrations",
    place: "menu",
    admin: true,
    screen: IntegrationsScreen,
  },
  {
    id: "integration-new",
    path: "/integrations/sqlserver/new",
    title: "Add a SQL Server",
    icon: "integrations",
    place: "detail",
    admin: true,
    screen: IntegrationNewScreen,
  },
  {
    id: "integration",
    path: "/integrations/sqlserver/:id",
    title: "Integration",
    icon: "integrations",
    place: "detail",
    admin: true,
    screen: IntegrationScreen,
  },
  { id: "keys", path: "/keys", title: "Ingestion keys", icon: "keys", place: "menu", admin: true, screen: KeysScreen },
  { id: "users", path: "/users", title: "Users", icon: "users", place: "menu", admin: true, screen: UsersScreen },
  {
    id: "notifications",
    path: "/notifications",
    title: "Notifications",
    icon: "notifications",
    place: "menu",
    admin: true,
    screen: NotificationsScreen,
  },
  {
    id: "settings",
    path: "/settings",
    title: "Settings",
    icon: "settings",
    place: "menu",
    admin: true,
    screen: SettingsScreen,
  },
  { id: "account", path: "/account", title: "Account", icon: "users", place: "account", screen: AccountScreen },
];

/**
 * The route with a name.
 *
 * @param id - Its name.
 * @returns The route.
 */
export function routeById(id: RouteId): AppRoute {
  const route = routes.find((candidate) => candidate.id === id);

  if (!route) {
    throw new Error(`No route is named ${id}.`);
  }

  return route;
}
