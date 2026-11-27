import type { HomeSnapshot } from "../../api/types";
import { HelpTip } from "../../ui/HelpTip";
import { useAlertHistory } from "../../alerts/useAlertHistory";
import { WeatherDays } from "../../alerts/WeatherDays";
import { ForecastHero, KeyMetrics, Outlook } from "../forecast/ForecastVisual";
import { BlocksPanel } from "./BlocksPanel";
import { DayChart } from "./DayChart";
import { LatestAlerts } from "./LatestAlerts";

/** Props for {@link HomeWall}. */
interface HomeWallProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
}

/** The days the wall looks back for its month and its latest alerts. */
const wallDays = 30;

/**
 * The home on a wall-sized screen (VEY-24), at natural size: bands that fill the screen beside Needs you. On top the
 * hero and the key figures; in the middle every service and host, each host with its disk, and the last 30 days; at
 * the bottom the last 24 hours beside the latest alerts and the outlook.
 *
 * It reads the alert history of the environment the same way the Alerts screen does, and only while it is shown.
 */
export function HomeWall({ snapshot }: HomeWallProps) {
  const history = useAlertHistory({
    days: wallDays,
    state: "all",
    rules: [],
    text: "",
    environment: snapshot.environment,
  });
  const summary = history.summary;

  return (
    <div className="wall">
      <div className="wall-top">
        <ForecastHero snapshot={snapshot} />
        <KeyMetrics snapshot={snapshot} />
      </div>
      <div className="wall-mid">
        <BlocksPanel snapshot={snapshot} />
        <section className="glass lg-panel lg-month" aria-labelledby="wall-month">
          <div className="lg-head">
            <div className="home-name">
              <h2 id="wall-month" className="cap">
                Last 30 days
              </h2>
              <HelpTip label="Last 30 days">
                One cell per day with the worst alert that opened that day and how many opened, as on the Alerts screen.
                Days start at midnight in your time zone.
              </HelpTip>
            </div>
            {summary && (
              <span className="lg-count">
                {summary.total} {summary.total === 1 ? "alert" : "alerts"} · {summary.warnings}{" "}
                {summary.warnings === 1 ? "warning" : "warnings"}
              </span>
            )}
          </div>
          {summary ? (
            <WeatherDays days={summary.days} className="wall-days" />
          ) : (
            <p className="fine">Reading the last 30 days…</p>
          )}
        </section>
      </div>
      <div className="wall-low">
        <DayChart snapshot={snapshot} />
        <div className="wall-stack">
          <LatestAlerts items={history.items} error={history.error} now={new Date(snapshot.at)} />
          <Outlook snapshot={snapshot} />
        </div>
      </div>
    </div>
  );
}
