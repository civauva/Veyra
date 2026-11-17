import { createContext } from "react";

/**
 * The session open in the Live tab's side panel, or null, so every row of that session can show it is the one open.
 */
export const SelectedSession = createContext<number | null>(null);
