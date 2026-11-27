import type { AlertDetails, ServerError } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { atMoment } from "./alertFormat";

/** Props for {@link ServerErrors}. */
interface ServerErrorsProps {
  /** The alert, with the errors its rule counted. */
  alert: AlertDetails;
  /** The errors, newest first. */
  errors: ServerError[];
}

/**
 * The errors a database server's error rule counted (VEY-61): each with when it was read, its number, its severity,
 * and what SQL Server said, so the alert says which errors set it off and not only how many. Shown in both views.
 *
 * @param props - The alert and its errors.
 * @returns The panel.
 */
export function ServerErrors({ alert, errors }: ServerErrorsProps) {
  const logins = alert.rule === "db-login-failures";
  const name = logins ? "Failures it counted" : "Errors it counted";

  return (
    <section className="glass alert-panel" aria-labelledby="alert-counted">
      <div className="alert-name">
        <h2 id="alert-counted" className="cap">
          {name}
        </h2>
        <HelpTip label={name}>
          What SQL Server&apos;s system_health session recorded and the rule counted, from 15 minutes before the alert
          opened until now or its end, newest first. Times are when Veyra read them, within a minute of when they
          happened. Most of these errors are not in SQL Server&apos;s error log.
        </HelpTip>
      </div>
      {errors.length === 0 ? (
        <p className="fine">None of them is stored any more; the server&apos;s Events tab shows what is left.</p>
      ) : (
        <ol className="server-errors">
          {errors.map((error) => (
            <li key={`${error.at}-${error.error}-${error.message}`}>
              <time dateTime={error.at}>{atMoment(error.at, alert.resolvedAt ?? alert.updatedAt)}</time>
              <b>
                Error {error.error} · severity {error.severity}
              </b>
              <span>{error.message}</span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
