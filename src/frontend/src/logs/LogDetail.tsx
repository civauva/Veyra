import { useEffect, useRef, useState } from "react";

import type { LogEntry } from "../api/types";
import { formatInstant } from "./format";
import { SeverityBadge } from "./SeverityBadge";

/** The OpenTelemetry attribute that holds an exception's stack trace; it is shown as a block, not as a row. */
const stackTraceAttribute = "exception.stacktrace";

/** When the panel is an overlay rather than a column; matches the breakpoint in logs.css. */
const overlayQuery = "(max-width: 1199px)";

/** Props for {@link LogDetail}. */
interface LogDetailProps {
  /** The record to show. */
  entry: LogEntry;
  /** Filters the list to the record's trace. */
  onSameTrace: (traceId: string) => void;
  /** Shows one minute either side of the record in its service. */
  onAround: (entry: LogEntry) => void;
  /** Closes the panel. */
  onClose: () => void;
}

/**
 * Everything about one record: message, stack trace, attributes, resource, and trace, with actions that pivot the
 * search from it. A column on wide screens, a drawer on tablets, and full screen on phones.
 */
export function LogDetail({ entry, onSameTrace, onAround, onClose }: LogDetailProps) {
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");
  const closeRef = useRef<HTMLButtonElement>(null);

  // On tablets and phones the panel covers the list, so the keyboard moves into it when it opens. On wide screens
  // focus stays in the list, where the arrow keys move the selection.
  useEffect(() => {
    if (window.matchMedia?.(overlayQuery).matches) {
      closeRef.current?.focus();
    }
  }, []);
  const stackTrace = entry.attributes[stackTraceAttribute];
  const attributes = Object.entries(entry.attributes).filter(([key]) => key !== stackTraceAttribute);
  const resource = entry.resourceAttributes;
  const context = [
    entry.serviceName,
    entry.environment,
    resource["host.name"] && `host ${resource["host.name"]}`,
    resource["service.version"] && `v${resource["service.version"]}`,
  ]
    .filter(Boolean)
    .join(" · ");

  async function copy() {
    try {
      // The clipboard exists only in secure contexts, so plain-HTTP installations land in the catch.
      await navigator.clipboard.writeText(JSON.stringify(entry, null, 2));
      setCopyState("copied");
    } catch {
      setCopyState("failed");
    }
  }

  return (
    <section className="detail" aria-label="Selected record">
      <div className="detail-top">
        <SeverityBadge severity={entry.severity} />
        <span className="mono detail-time">{formatInstant(entry.timestamp)}</span>
        <button
          ref={closeRef}
          type="button"
          className="button detail-close"
          aria-label="Close the record"
          onClick={onClose}
        >
          ×
        </button>
      </div>

      <h2 className="detail-message">{entry.body}</h2>
      <p className="detail-context">{context}</p>

      {stackTrace && <pre className="detail-stack mono">{stackTrace}</pre>}

      <Fields title="Attributes" fields={attributes} empty="This record has no attributes." />
      <Fields title="Resource" fields={Object.entries(resource)} empty="The sender described no resource." />
      {entry.traceId && (
        <Fields
          title="Trace"
          fields={[
            ["trace_id", entry.traceId],
            ...(entry.spanId ? ([["span_id", entry.spanId]] as [string, string][]) : []),
          ]}
        />
      )}

      <div className="detail-actions">
        {entry.traceId && (
          <button type="button" className="button" onClick={() => onSameTrace(entry.traceId ?? "")}>
            Same trace
          </button>
        )}
        <button type="button" className="button" onClick={() => onAround(entry)}>
          ± 1 minute, this service
        </button>
        <button type="button" className="button" onClick={() => void copy()}>
          Copy JSON
        </button>
        <span role="status" className="detail-copied">
          {copyState === "copied" ? "Copied" : copyState === "failed" ? "Copying needs HTTPS" : ""}
        </span>
      </div>
    </section>
  );
}

/** Props for {@link Fields}. */
interface FieldsProps {
  /** The section heading. */
  title: string;
  /** Name and value pairs. */
  fields: [string, string][];
  /** What to say when there are none; omitted sections with no fields are hidden. */
  empty?: string;
}

/** One titled list of name and value pairs. */
function Fields({ title, fields, empty }: FieldsProps) {
  if (fields.length === 0 && !empty) {
    return null;
  }

  return (
    <section className="detail-fields">
      <h3 className="section-title">{title}</h3>
      {fields.length === 0 ? (
        <p className="filter-note">{empty}</p>
      ) : (
        <dl>
          {fields.map(([name, value]) => (
            <div key={name}>
              <dt>{name}</dt>
              <dd className="mono">{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </section>
  );
}
