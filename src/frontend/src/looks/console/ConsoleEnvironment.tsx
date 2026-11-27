import type { ConditionKind, HomeSnapshot } from "../../api/types";
import { useEnvironmentSwitcher } from "../../app/useEnvironmentSwitcher";
import { formatRatio } from "../../home/homeFormat";
import { ConsoleIcon } from "./ConsoleIcon";

/** Props for {@link ConsoleEnvironment}. */
interface ConsoleEnvironmentProps {
  /** The latest snapshot, for the chosen environment's condition; null before it arrives. */
  snapshot: HomeSnapshot | null;
  /** The chosen environment, or null for all of them. */
  environment: string | null;
  /**
   * Called when the person picks another environment.
   *
   * @param environment - The environment, or null for all.
   */
  onChoose(environment: string | null): void;
}

/** The dot of a condition: green when clear, amber in showers, red in a storm. */
const tones: Record<ConditionKind, string> = { Clear: "ok", Showers: "warn", Storm: "bad" };

/**
 * The environment at the left of the Console top bar: its name with a dot for its condition, opening a list to pick
 * another, with a find box the server answers (VEY-67). The behaviour is every look's (`app/useEnvironmentSwitcher.ts`).
 */
export function ConsoleEnvironment({ snapshot, environment, onChoose }: ConsoleEnvironmentProps) {
  const { open, toggle, rootRef, buttonRef, findRef, listId, query, setQuery, options, answered, choose, moved } =
    useEnvironmentSwitcher(onChoose);
  const condition = snapshot?.condition;
  const words = condition
    ? `${condition.kind} · ${condition.errorRatio === null ? "no records" : `${formatRatio(condition.errorRatio)} errors`}`
    : "Reading the condition…";

  return (
    <div className="cn-env" ref={rootRef}>
      <button
        ref={buttonRef}
        type="button"
        className="cn-control"
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        onClick={toggle}
      >
        <ConsoleIcon name="layers" size={14} />
        <span className="visually-hidden">Environment:</span>{" "}
        <b className="cn-env-name">{environment ?? "All environments"}</b>
        <span className={`cn-dot ${condition ? tones[condition.kind] : "off"}`} aria-hidden="true" />
        <span className="visually-hidden">{words}</span>
        <ConsoleIcon name="chevron" size={14} />
      </button>
      {open && (
        <div id={listId} className="cn-env-menu" role="dialog" aria-label="Choose an environment" onKeyDown={moved}>
          <input
            ref={findRef}
            type="search"
            className="cn-env-find"
            aria-label="Find an environment"
            placeholder="Find an environment"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          {answered && options.length === 0 ? (
            <p className="cn-env-note">No environment is called that.</p>
          ) : (
            <ul>
              {options.map((option) => (
                <li key={option.name ?? ""}>
                  <button
                    type="button"
                    className="cn-env-option"
                    aria-pressed={option.name === environment}
                    onClick={() => choose(option)}
                  >
                    <span className={`cn-dot ${tones[option.condition]}`} aria-hidden="true" />
                    <b>{option.label}</b>
                    <small>
                      {option.name === null
                        ? "every environment"
                        : option.errorRatio === null
                          ? "no records"
                          : `${formatRatio(option.errorRatio)} errors`}
                    </small>
                    {option.name === environment && <ConsoleIcon name="check" size={15} className="cn-env-tick" />}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <p className="cn-env-note">
            Environments come from the <code>deployment.environment</code> attribute of the telemetry.
          </p>
        </div>
      )}
    </div>
  );
}
