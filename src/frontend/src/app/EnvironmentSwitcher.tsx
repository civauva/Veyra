import { type KeyboardEvent, useEffect, useId, useRef, useState } from "react";

import type { ConditionKind, HomeSnapshot } from "../api/types";
import { formatRatio } from "../home/homeFormat";
import { conditionIcon, WeatherIcon } from "../home/WeatherIcon";
import { NavIcon } from "./NavIcon";
import { usePopover } from "./usePopover";

/** Props for {@link EnvironmentSwitcher}. */
interface EnvironmentSwitcherProps {
  /** The latest snapshot, for the environments and the weather of the chosen one; null before it arrives. */
  snapshot: HomeSnapshot | null;
  /** The chosen environment, or null for all of them. */
  environment: string | null;
  /**
   * Called when the person picks another environment.
   *
   * @param environment - The environment, or null for all.
   */
  onChoose(environment: string | null): void;
  /** Drawn small, for the phone's top bar: the name and the error rate only. */
  compact?: boolean;
}

/** One line of the list. */
interface Option {
  /** The environment, or null for all of them. */
  name: string | null;
  /** What the line says. */
  label: string;
  /** Its weather. */
  condition: ConditionKind;
  /** Its error ratio, when known. */
  errorRatio: number | null;
}

const severity: ConditionKind[] = ["Clear", "Showers", "Storm"];

/**
 * The environment at the top of the sidebar (VEY-26), like a workspace switcher: it says which environment every
 * screen below shows, with its weather and error rate, and opens a list to choose another, with a search field for
 * installations with many environments.
 *
 * "All environments" carries the worst weather of any of them, so a storm elsewhere is never hidden.
 */
export function EnvironmentSwitcher({ snapshot, environment, onChoose, compact = false }: EnvironmentSwitcherProps) {
  const { open, toggle, close, rootRef, buttonRef } = usePopover();
  const [query, setQuery] = useState("");
  const findRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  useEffect(() => {
    if (open) {
      findRef.current?.focus();
    }
  }, [open]);

  const environments = snapshot?.environments ?? [];
  const worst = environments.reduce<ConditionKind>(
    (current, next) => (severity.indexOf(next.condition) > severity.indexOf(current) ? next.condition : current),
    "Clear",
  );
  const options: Option[] = [
    { name: null, label: "All environments", condition: worst, errorRatio: null },
    ...environments.map((item) => ({
      name: item.name,
      label: item.name,
      condition: item.condition,
      errorRatio: item.errorRatio,
    })),
  ];
  const needle = query.trim().toLowerCase();
  const listed = needle === "" ? options : options.filter((option) => option.label.toLowerCase().includes(needle));

  const condition = snapshot?.condition;
  const name = environment ?? "All environments";
  const weather = condition
    ? `${condition.kind} · ${condition.errorRatio === null ? "no records" : `${formatRatio(condition.errorRatio)} errors`}`
    : "Reading the weather…";
  const tone = condition ? `env-${condition.kind.toLowerCase()}` : "";

  function choose(option: Option) {
    onChoose(option.name);
    setQuery("");
    close(true);
  }

  // Up and down move between the search field and the lines, as in any list.
  function moved(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
      return;
    }

    const stops = Array.from(event.currentTarget.querySelectorAll<HTMLElement>("input, button"));
    const at = stops.indexOf(document.activeElement as HTMLElement);
    const next = stops[(at + (event.key === "ArrowDown" ? 1 : stops.length - 1)) % stops.length];
    event.preventDefault();
    next?.focus();
  }

  return (
    <div className={compact ? "env-switch-wrap compact" : "env-switch-wrap"} ref={rootRef}>
      <button
        ref={buttonRef}
        type="button"
        className={["env-switch", tone].filter(Boolean).join(" ")}
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        onClick={toggle}
      >
        <span className="visually-hidden">Environment:</span>{" "}
        <WeatherIcon
          kind={condition ? conditionIcon(condition.kind) : "cloud"}
          size={compact ? 22 : 28}
          className="env-switch-icon"
        />
        <span className="env-switch-text sb-label">
          <b>{name}</b> <small>{compact && condition ? formatRatio(condition.errorRatio) : weather}</small>
        </span>
        <NavIcon name="choose" size={18} className="env-switch-chevron" />
      </button>
      {open && (
        <div id={listId} className="sb-pop env-menu" role="dialog" aria-label="Choose an environment" onKeyDown={moved}>
          <input
            ref={findRef}
            type="search"
            className="env-find"
            aria-label="Find an environment"
            placeholder="Find an environment"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          {listed.length === 0 ? (
            <p className="env-none">No environment is called that.</p>
          ) : (
            <ul>
              {listed.map((option) => (
                <li key={option.name ?? ""}>
                  <button
                    type="button"
                    className="env-option"
                    aria-pressed={option.name === environment}
                    onClick={() => choose(option)}
                  >
                    <WeatherIcon kind={conditionIcon(option.condition)} size={22} />
                    <span>
                      <b>{option.label}</b>{" "}
                      <small>
                        {option.name === null
                          ? "every environment together"
                          : option.errorRatio === null
                            ? "no records"
                            : `${formatRatio(option.errorRatio)} errors`}
                      </small>
                    </span>
                    {option.name === environment && <NavIcon name="check" size={18} className="env-tick" />}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <p className="env-note">
            Environments come from the <code>deployment.environment</code> attribute of the telemetry.
          </p>
        </div>
      )}
    </div>
  );
}
