import { Navigate, Route, Routes } from "react-router";

import { SetupScreen } from "../auth/SetupScreen";
import { SignInScreen } from "../auth/SignInScreen";
import { useSession } from "../session/useSession";
import { mayOpen, screenOf } from "./look";
import { currentLook } from "./looks";
import { routes } from "./routes";

/**
 * The root of the Veyra UI. Which screens exist depends on the session: first-run setup, sign-in, or the app itself.
 * The URL is kept while signed out, so after signing in the person lands where they were going. Signed in, the
 * current look draws the frame and the home, and the route registry gives every other screen (ADR 0021).
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

    case "signedIn": {
      const look = currentLook(state.session);

      return (
        <Routes>
          <Route element={<look.Shell session={state.session} />}>
            {/* Routes a role may not open are left out, so their paths fall through to the home (ADR 0008). */}
            {routes
              .filter((route) => mayOpen(route, state.session.role))
              .map((route) => {
                const Screen = screenOf(look, route);

                return route.path === "/" ? (
                  <Route key={route.id} index element={<Screen />} />
                ) : (
                  <Route key={route.id} path={route.path} element={<Screen />} />
                );
              })}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      );
    }
  }
}
