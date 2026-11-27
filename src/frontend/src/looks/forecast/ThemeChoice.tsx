import { themeNames, type ThemeName } from "../../app/useTheme";

const labels: Record<ThemeName, string> = { system: "System", light: "Light", dark: "Dark" };

/** Props for {@link ThemeChoice}. */
interface ThemeChoiceProps {
  /** The current choice. */
  value: ThemeName;
  /**
   * Called with the theme the person picks.
   *
   * @param choice - The theme.
   */
  onChange(choice: ThemeName): void;
}

/** System, light, and dark side by side in the account menu (VEY-26), the current one pressed. */
export function ThemeChoice({ value, onChange }: ThemeChoiceProps) {
  return (
    <div className="theme-choice" role="group" aria-label="Theme">
      {themeNames.map((choice) => (
        <button key={choice} type="button" aria-pressed={value === choice} onClick={() => onChange(choice)}>
          {labels[choice]}
        </button>
      ))}
    </div>
  );
}
