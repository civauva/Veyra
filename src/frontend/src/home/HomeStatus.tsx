import type { ConditionKind, HomeSnapshot } from "../api/types";
import { formatClock } from "../logs/format";
import { formatRatio } from "./homeFormat";
import { conditionIcon, WeatherIcon } from "./WeatherIcon";

/** Props for {@link EnvironmentPills}. */
interface EnvironmentPillsProps {
  /** The latest snapshot, for the environments and their weather. */
  snapshot: HomeSnapshot;
  /** The environment shown, or null for all. */
  environment: string | null;
  /**
   * Called when the person picks another environment.
   *
   * @param environment - The environment, or null for all.
   */
  onChoose(environment: string | null): void;
}

const order: ConditionKind[] = ["Clear", "Showers", "Storm"];

/**
 * The environment pills of the top bar, each with its weather and error rate. On tablets and phones they fold into
 * one picker, as the home dashboards design asks.
 */
export function EnvironmentPills({ snapshot, environment, onChoose }: EnvironmentPillsProps) {
  const environments = snapshot.environments;

  // "All" carries the worst weather of any environment, so a storm elsewhere is never hidden.
  const worst = environments.reduce<ConditionKind>(
    (current, next) => (order.indexOf(next.condition) > order.indexOf(current) ? next.condition : current),
    "Clear",
  );

  return (
    <>
      <div className="env-pills" role="group" aria-label="Environment">
        <button
          type="button"
          className={pillClass(worst, environment === null)}
          aria-pressed={environment === null}
          onClick={() => onChoose(null)}
        >
          <WeatherIcon kind={conditionIcon(worst)} size={18} />
          <b>All</b>
        </button>
        {environments.map((item) => (
          <button
            key={item.name}
            type="button"
            className={pillClass(item.condition, environment === item.name)}
            aria-pressed={environment === item.name}
            onClick={() => onChoose(item.name)}
          >
            <WeatherIcon kind={conditionIcon(item.condition)} size={18} />
            <b>{item.name}</b>
            <span>{formatRatio(item.errorRatio)}</span>
          </button>
        ))}
      </div>
      <label className="env-picker">
        <span className="visually-hidden">Environment</span>
        <select
          value={environment ?? ""}
          onChange={(event) => onChoose(event.target.value === "" ? null : event.target.value)}
        >
          <option value="">All environments</option>
          {environments.map((item) => (
            <option key={item.name} value={item.name}>
              {item.name} · {formatRatio(item.errorRatio)}
            </option>
          ))}
        </select>
      </label>
    </>
  );
}

/** Props for {@link MasterLights}. */
interface MasterLightsProps {
  /** The latest snapshot, for the counts and the time. */
  snapshot: HomeSnapshot;
}

/**
 * Master warning and master caution, as in a cockpit: lit while anything is in that state, with the count, and the
 * time of the snapshot in UTC beside them.
 */
export function MasterLights({ snapshot }: MasterLightsProps) {
  const { warning, caution } = snapshot.counts;

  return (
    <>
      <span
        role="status"
        aria-label="Master warning"
        className={warning > 0 ? "master-light warning lit" : "master-light warning"}
      >
        <i aria-hidden="true" />
        {warning}
        <em> {warning === 1 ? "warning" : "warnings"}</em>
      </span>
      <span
        role="status"
        aria-label="Master caution"
        className={caution > 0 ? "master-light caution lit" : "master-light caution"}
      >
        <i aria-hidden="true" />
        {caution}
        <em> {caution === 1 ? "caution" : "cautions"}</em>
      </span>
      <time className="home-clock" dateTime={snapshot.at} title="Time of the snapshot, UTC">
        {formatClock(snapshot.at)}
        <small>UTC</small>
      </time>
    </>
  );
}

function pillClass(condition: ConditionKind, current: boolean): string {
  return ["env-pill", `env-${condition.toLowerCase()}`, current ? "current" : ""].filter(Boolean).join(" ");
}
