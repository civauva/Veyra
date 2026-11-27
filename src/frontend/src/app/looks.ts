import { lookIdOf, type LookId } from "../api/looks";
import type { Session } from "../api/types";
import { consoleLook } from "../looks/console";
import { forecast } from "../looks/forecast";
import type { Look } from "./look";

/** Every look there is (ADR 0021). */
export const looks: Record<LookId, Look> = { console: consoleLook, forecast };

/**
 * The look to draw for a signed-in person.
 *
 * @param session - The session.
 * @returns The look.
 */
export function currentLook(session: Session): Look {
  return looks[lookIdOf(session.look)];
}
