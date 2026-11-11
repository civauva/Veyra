import { Navigate, Route, Routes } from "react-router";

import { AlertScreen } from "../alerts/AlertScreen";
import { AlertsScreen } from "../alerts/AlertsScreen";
import { SetupScreen } from "../auth/SetupScreen";
import { SignInScreen } from "../auth/SignInScreen";
import { HomeScreen } from "../home/HomeScreen";
import { KeysScreen } from "../keys/KeysScreen";
import { LogExplorer } from "../logs/LogExplorer";
import { HostScreen } from "../metrics/HostScreen";
import { MetricsScreen } from "../metrics/MetricsScreen";
import { ServiceScreen } from "../metrics/ServiceScreen";
import { NotificationsScreen } from "../notifications/NotificationsScreen";
import { useSession } from "../session/useSession";
import { AccountScreen } from "../account/AccountScreen";
import { UsersScreen } from "../users/UsersScreen";
import { Shell } from "./Shell";

/**
 * The root of the Veyra UI. Which screens exist depends on the session: first-run setup, sign-in, or the app itself.
 * The URL is kept while signed out, so after signing in the person lands where they were going.
 */
export function App() {
  const { state } = useSession();

  switch (state.status) {
    case "loading":
      return <p className="app-loading">Loading…</p>;

    case "error":
      return (
        <p className="app-loading" role="alert">
          Veyra could not be reached: {state.message}
        </p>
      );

    case "setup":
      return <SetupScreen />;

    case "signedOut":
      return <SignInScreen notice={state.notice} />;

    case "signedIn":
      return (
        <Routes>
          <Route element={<Shell session={state.session} />}>
            <Route index element={<HomeScreen />} />
            <Route path="/logs" element={<LogExplorer />} />
            <Route path="/alerts" element={<AlertsScreen />} />
            <Route path="/alerts/:id" element={<AlertScreen />} />
            <Route path="/metrics" element={<MetricsScreen />} />
            <Route path="/metrics/hosts/:host" element={<HostScreen />} />
            <Route path="/metrics/services/:service" element={<ServiceScreen />} />
            {/* Only administrators manage keys and notification channels (ADR 0008); for others the paths fall through
                to the home. */}
            {state.session.role === "Admin" && <Route path="/keys" element={<KeysScreen />} />}
            {state.session.role === "Admin" && <Route path="/users" element={<UsersScreen />} />}
            {state.session.role === "Admin" && <Route path="/notifications" element={<NotificationsScreen />} />}
            <Route path="/account" element={<AccountScreen />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      );
  }
}
