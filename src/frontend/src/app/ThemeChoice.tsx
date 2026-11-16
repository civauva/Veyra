import { useEffect, useState } from "react";

/** The theme a person can choose. "system" follows the operating system. */
export type ThemeName = "system" | "light" | "dark";

const storageKey = "veyra.theme";
const order: ThemeName[] = ["system", "light", "dark"];
const labels: Record<ThemeName, string> = { system: "System", light: "Light", dark: "Dark" };

/** Reads the saved choice. Storage can be blocked, for example in private windows, so failures mean "system". */
function readChoice(): ThemeName {
  try {
    const saved = localStorage.getItem(storageKey);

    return order.includes(saved as ThemeName) ? (saved as ThemeName) : "system";
  } catch {
    return "system";
  }
}

/** Applies a choice to the page, through the data-theme attribute that tokens.css reads. */
function applyChoice(choice: ThemeName): void {
  if (choice === "system") {
    document.documentElement.removeAttribute("data-theme");
  } else {
    document.documentElement.setAttribute("data-theme", choice);
  }
}

/**
 * The theme in this browser: applied to the page and remembered there only. The shell holds it, so the saved theme
 * applies as soon as someone is signed in, before the account menu is ever opened. A failure to save is ignored.
 *
 * @returns The current choice and the way to change it.
 */
export function useTheme(): [ThemeName, (choice: ThemeName) => void] {
  const [choice, setChoice] = useState<ThemeName>(readChoice);

  useEffect(() => {
    applyChoice(choice);

    try {
      localStorage.setItem(storageKey, choice);
    } catch {
      // Storage blocked: the choice still applies until the page is reloaded.
    }
  }, [choice]);

  return [choice, setChoice];
}

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
      {order.map((choice) => (
        <button key={choice} type="button" aria-pressed={value === choice} onClick={() => onChange(choice)}>
          {labels[choice]}
        </button>
      ))}
    </div>
  );
}
