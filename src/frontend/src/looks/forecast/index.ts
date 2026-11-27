import type { Look } from "../../app/look";
import { HomeScreen } from "../../home/HomeScreen";
import { forecastNav } from "./nav";
import { Shell } from "./Shell";

/**
 * Forecast: the environment read like a weather report (ADR 0007), with the weather behind a left sidebar
 * (ADR 0013). Every screen but the home is the registry's own.
 */
export const forecast: Look = {
  id: "forecast",
  name: "Forecast",
  Shell,
  nav: forecastNav,
  screens: { home: HomeScreen },
};
