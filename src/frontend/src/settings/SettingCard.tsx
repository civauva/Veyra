import type { Setting } from "../api/types";
import { SettingField, type SettingEdit } from "./SettingField";

/** Props for {@link SettingCard}. */
interface SettingCardProps {
  /** The card's name, such as "Error rate". */
  group: string;
  /** The card's settings, in order. */
  settings: Setting[];
  /** Unsaved edits by key. */
  edits: Map<string, SettingEdit>;
  /** Problems from the last review, by key. */
  problems: Map<string, string>;
  /** Whether to mark advanced settings, as when a search shows them in the Simple view. */
  tagAdvanced: boolean;
  /** Called with a setting's new edit. */
  onEdit: (key: string, edit: SettingEdit) => void;
}

/** The value a setting has now, with its edit applied, as a number; null when it has none. */
function numberOf(setting: Setting | undefined, edits: Map<string, SettingEdit>): number | null {
  if (!setting) {
    return null;
  }

  const edit = edits.get(setting.key);
  const value = edit === undefined ? setting.value : edit === null ? setting.default : edit;
  const number = Number(value);

  return value === null || !Number.isFinite(number) ? null : number;
}

/**
 * A card of settings that belong together, such as one alert rule. A rule's switch sits in the header; a caution and
 * a warning level are drawn on a scale, green below caution, amber to warning, red above (VEY-31 mockups).
 */
export function SettingCard({ group, settings, edits, problems, tagAdvanced, onEdit }: SettingCardProps) {
  const toggle = settings.find((setting) => setting.kind === "Switch" && setting.label === "On");
  const fields = settings.filter((setting) => setting !== toggle);
  const caution = settings.find((setting) => /Caution(Ratio|Seconds)$/.test(setting.key));
  const warning = settings.find((setting) => /Warning(Ratio|Seconds)$/.test(setting.key));

  return (
    <section className="settings-card" aria-label={group}>
      <header>
        <h3>{group}</h3>
        {toggle && (
          <SettingField
            setting={toggle}
            edit={edits.get(toggle.key)}
            problem={problems.get(toggle.key) ?? null}
            onEdit={(edit) => onEdit(toggle.key, edit)}
          />
        )}
      </header>
      {caution && warning && (
        <ThresholdScale
          caution={numberOf(caution, edits)}
          warning={numberOf(warning, edits)}
          was={edits.has(warning.key) ? numberOf(warning, new Map()) : null}
          ratio={caution.kind === "Ratio"}
          unit={caution.unit ?? ""}
        />
      )}
      <div className="settings-fields">
        {fields.map((setting) => (
          <SettingField
            key={setting.key}
            setting={setting}
            edit={edits.get(setting.key)}
            problem={problems.get(setting.key) ?? null}
            tagAdvanced={tagAdvanced}
            onEdit={(edit) => onEdit(setting.key, edit)}
          />
        ))}
      </div>
    </section>
  );
}

/** Props for {@link ThresholdScale}. */
interface ThresholdScaleProps {
  /** The caution level, as configuration stores it. */
  caution: number | null;
  /** The warning level. */
  warning: number | null;
  /** The warning level before an unsaved edit, drawn as a ghost; null when unchanged. */
  was: number | null;
  /** Whether the levels are ratios, shown as percentages. */
  ratio: boolean;
  /** The unit of the tick labels. */
  unit: string;
}

/** The levels of a rule on one scale. It repeats the fields beside it, so screen readers skip it. */
function ThresholdScale({ caution, warning, was, ratio, unit }: ThresholdScaleProps) {
  if (caution === null || warning === null) {
    return null;
  }

  const factor = ratio ? 100 : 1;
  // Room above the warning level to see where it sits, but a percentage never runs past 100 %.
  const room = Math.max(ratio ? 10 : 5, Math.ceil((Math.max(warning, was ?? 0) * factor * 1.6) / 5) * 5);
  const top = ratio ? Math.min(100, room) : room;
  const at = (value: number) => `${Math.min(100, ((value * factor) / top) * 100)}%`;
  const ticks = [0, 1, 2, 3, 4, 5].map((step) => (top / 5) * step);

  return (
    <div className="threshold-scale" aria-hidden="true">
      <div className="threshold-track">
        <i className="ok" style={{ width: at(caution) }} />
        <i className="caution" style={{ width: `calc(${at(warning)} - ${at(caution)})` }} />
        <i className="warning" />
      </div>
      {was !== null && <span className="threshold-was" style={{ left: at(was) }} />}
      <span className="threshold-handle caution" style={{ left: at(caution) }} />
      <span className={`threshold-handle warning${was !== null ? " edited" : ""}`} style={{ left: at(warning) }} />
      {ticks.map((tick) => (
        <span key={tick} className="threshold-tick" style={{ left: `${(tick / top) * 100}%` }}>
          {Number(tick.toPrecision(3))} {unit}
        </span>
      ))}
    </div>
  );
}
