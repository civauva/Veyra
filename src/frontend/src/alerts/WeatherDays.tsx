import type { AlertSummary } from "../api/types";
import { WeatherIcon } from "../home/WeatherIcon";
import "./weatherDays.css";

/** Props for {@link WeatherDays}. */
interface WeatherDaysProps {
  /** One entry per UTC day, oldest first, as the alert summary gives them; the last is today. */
  days: AlertSummary["days"];
  /** An extra class for the list, to change how many cells a row holds. */
  className?: string;
}

/**
 * One cell per day with the weather of its worst alert and how many opened, today last. Shared by the weather log of
 * the Alerts screen and the month on the home wall (VEY-24), so both read the same.
 */
export function WeatherDays({ days, className }: WeatherDaysProps) {
  const last = days.length - 1;

  return (
    <ol className={className ? `log-days ${className}` : "log-days"}>
      {days.map((day, index) => {
        const date = new Date(`${day.date}T00:00:00Z`);
        const label =
          index === last
            ? "Now"
            : date.getUTCDate() === 1
              ? date.toLocaleDateString("en-US", { month: "short", timeZone: "UTC" })
              : String(date.getUTCDate());
        const tone = day.worst === "Warning" ? "w" : day.worst === "Caution" ? "c" : "";
        const kind = day.worst === "Warning" ? "storm" : day.worst === "Caution" ? "shower" : "sun";
        const words =
          day.count === 0
            ? "no alerts"
            : `${day.count} ${day.count === 1 ? "alert" : "alerts"}, worst ${day.worst === "Warning" ? "warning" : "caution"}`;

        return (
          <li
            key={day.date}
            className={`log-day ${tone}${index === last ? " today" : ""}`}
            aria-label={`${day.date}: ${words}`}
            title={`${day.date}: ${words}`}
          >
            <span className="log-date" aria-hidden="true">
              {label}
            </span>
            <WeatherIcon kind={kind} size={20} />
            <span className="log-count" aria-hidden="true">
              {day.count || ""}
            </span>
          </li>
        );
      })}
    </ol>
  );
}
