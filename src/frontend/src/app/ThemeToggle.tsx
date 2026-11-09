import { useEffect, useState } from "react";

/** The theme a person can choose. "system" follows the operating system. */
type ThemeChoice = "system" | "light" | "dark";

const storageKey = "veyra.theme";
const order: ThemeChoice[] = ["system", "light", "dark"];
const labels: Record<ThemeChoice, string> = { system: "Theme: system", light: "Theme: light", dark: "Theme: dark" };

/** Reads the saved choice. Storage can be blocked, for example in private windows, so failures mean "system". */
function readChoice(): ThemeChoice {
  try {
    const saved = localStorage.getItem(storageKey);

    return order.includes(saved as ThemeChoice) ? (saved as ThemeChoice) : "system";
  } catch {
    return "system";
  }
}

/** Applies a choice to the page, through the data-theme attribute that tokens.css reads. */
function applyChoice(choice: ThemeChoice): void {
  if (choice === "system") {
    document.documentElement.removeAttribute("data-theme");
  } else {
    document.documentElement.setAttribute("data-theme", choice);
  }
}

/**
 * Cycles the theme between system, light, and dark, and remembers the choice in this browser only. It is a
 * convenience, so a failure to save is ignored.
 */
export function ThemeToggle() {
  const [choice, setChoice] = useState<ThemeChoice>(readChoice);

  useEffect(() => {
    applyChoice(choice);

    try {
      localStorage.setItem(storageKey, choice);
    } catch {
      // Storage blocked: the choice still applies until the page is reloaded.
    }
  }, [choice]);

  function next() {
    setChoice(order[(order.indexOf(choice) + 1) % order.length] ?? "system");
  }

  return (
    <button
      type="button"
      className="button shell-theme"
      onClick={next}
      aria-label={labels[choice]}
      title={labels[choice]}
    >
      {choice === "dark" ? "☾" : choice === "light" ? "☀" : "◐"}
    </button>
  );
}
