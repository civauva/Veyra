import { useContext } from "react";

import { SessionContext } from "./SessionContext";

/**
 * Whether the signed-in person is an Admin, for actions only Admins may take, such as adding a server (VEY-83).
 * Outside a session, as in a screen's own test, nobody is.
 *
 * @returns True for an Admin.
 */
export function useIsAdmin(): boolean {
  const value = useContext(SessionContext);

  return value?.state.status === "signedIn" && value.state.session.role === "Admin";
}
