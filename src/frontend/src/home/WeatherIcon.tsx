import type { ConditionKind } from "../api/types";

/** The weather pictures the homes draw. Red and amber mark warning and caution, as everywhere in Veyra. */
export type WeatherKind = "sun" | "moon" | "cloud" | "shower" | "storm" | "wind";

/** Props for {@link WeatherIcon}. */
interface WeatherIconProps {
  /** Which picture. */
  kind: WeatherKind;
  /** The width and height in pixels. Defaults to 26. */
  size?: number;
  /** An extra class, for colour or opacity. */
  className?: string;
}

const cloudPath = "M7 18.5h10.2a4.1 4.1 0 0 0 .2-8.2 5.6 5.6 0 0 0-10.8 1.6A3.3 3.3 0 0 0 7 18.5z";
const raisedCloudPath = (bottom: number) =>
  `M7 ${bottom}h10.2a4.1 4.1 0 0 0 .2-8.2 5.6 5.6 0 0 0-10.8 1.6A3.3 3.3 0 0 0 7 ${bottom}z`;

/**
 * A weather picture. Decorative: the text beside it always says the same thing in words.
 */
export function WeatherIcon({ kind, size = 26, className }: WeatherIconProps) {
  return (
    <svg
      className={className ? `weather-icon ${className}` : "weather-icon"}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {kind === "sun" && (
        <g stroke="var(--fc-amber)">
          <circle cx="12" cy="12" r="4.2" />
          <path d="M12 2.5v2.2M12 19.3v2.2M2.5 12h2.2M19.3 12h2.2M5.3 5.3l1.6 1.6M17.1 17.1l1.6 1.6M5.3 18.7l1.6-1.6M17.1 6.9l1.6-1.6" />
        </g>
      )}
      {kind === "moon" && <path d="M19.5 14.6A7.6 7.6 0 0 1 9.4 4.5a7.6 7.6 0 1 0 10.1 10.1z" stroke="currentColor" />}
      {kind === "cloud" && <path d={cloudPath} stroke="currentColor" />}
      {kind === "shower" && (
        <>
          <path d={raisedCloudPath(14.5)} stroke="currentColor" />
          <path d="M9 17.5l-1 2.5M13 17.5l-1 2.5M17 17.5l-1 2.5" stroke="var(--fc-amber)" />
        </>
      )}
      {kind === "storm" && (
        <>
          <path d={raisedCloudPath(15.5)} stroke="currentColor" />
          <path d="M12.8 13.5l-2.6 4.3h3.2l-2.2 4" stroke="var(--fc-red)" strokeWidth="1.8" />
        </>
      )}
      {kind === "wind" && <path d="M3 9.5h11a3 3 0 1 0-3-3M3 14.5h15.5a3 3 0 1 1-3 3M3 12h7" stroke="currentColor" />}
    </svg>
  );
}

/**
 * The picture of a condition.
 *
 * @param condition - The weather of an environment.
 * @returns Storm, shower, or sun.
 */
export function conditionIcon(condition: ConditionKind): WeatherKind {
  switch (condition) {
    case "Storm":
      return "storm";
    case "Showers":
      return "shower";
    case "Clear":
      return "sun";
  }
}
