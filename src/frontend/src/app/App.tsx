import { Navigate, Route, Routes } from "react-router";

import { SetupScreen } from "../auth/SetupScreen";
import { SignInScreen } from "../auth/SignInScreen";
import { KeysScreen } from "../keys/KeysScreen";
import { LogExplorer } from "../logs/LogExplorer";
import { useSession } from "../session/useSession";
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
            <Route path="/logs" element={<LogExplorer />} />
            {/* Only administrators manage keys (ADR 0008); for others the path falls through to the Log Explorer. */}
            {state.session.role === "Admin" && <Route path="/keys" element={<KeysScreen />} />}
            <Route path="*" element={<Navigate to="/logs" replace />} />
          </Route>
        </Routes>
      );
  }
}
