import type { AlertDetails } from "../api/types";
import { CommandBlock } from "../ui/CommandBlock";
import { ruleGuidance } from "./ruleGuidance";

/** Props for {@link AlertGuidance}. */
interface AlertGuidanceProps {
  /** The alert. */
  alert: AlertDetails;
}

/**
 * What an alert means and how to resolve it (VEY-60): why it matters, then the steps in order, each script ready to
 * copy with the server and database filled in. Once resolved, the same steps keep it from coming back. Shown in the
 * Simple view too, since it is what a person needs most; nothing until a catalog rule's words have loaded.
 *
 * @param props - The alert.
 * @returns The section, or nothing.
 */
export function AlertGuidance({ alert }: AlertGuidanceProps) {
  const guidance = ruleGuidance(alert);

  if (!guidance) {
    return null;
  }

  const resolved = alert.resolvedAt !== null;

  return (
    <section className="glass alert-guide" aria-labelledby="alert-guide">
      <h2 id="alert-guide" className="cap">
        {resolved ? "What it meant and how to keep it away" : "What it means and how to fix it"}
      </h2>
      <p className="alert-guide-meaning">{guidance.meaning}</p>
      <ol className="alert-guide-steps">
        {guidance.steps.map((step) => (
          <li key={step.text}>
            <p>{step.text}</p>
            {step.script && <CommandBlock label={step.text} text={step.script} />}
          </li>
        ))}
      </ol>
    </section>
  );
}
