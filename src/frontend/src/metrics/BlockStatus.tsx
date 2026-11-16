import { useEffect, useState } from "react";
import { Link } from "react-router";

import { api } from "../api/client";
import type { AlertListItem, BlockKind } from "../api/types";
import { alertTitle } from "../alerts/ruleWords";

/** Props for {@link BlockStatus}. */
interface BlockStatusProps {
  /** Whether the screen is about a host, a service, or a database server. */
  kind: BlockKind;
  /** Its block name; for a database server, its engine and server. */
  name: string;
  /** The name in words, when it differs from the block name, such as `SQL Server sql-prod-01`. */
  label?: string;
}

/**
 * One sentence on how a host or service is, from its open alerts: the alert rules decide, never the screen (ADR 0007).
 * The first thing on its screen, in both views (VEY-37).
 */
export function BlockStatus({ kind, name, label }: BlockStatusProps) {
  const [open, setOpen] = useState<AlertListItem[] | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    api
      .alerts({ days: 1, state: "open", rules: [], text: name, environment: null }, null, controller.signal)
      .then((page) => setOpen(page.items.filter((item) => item.block.kind === kind && item.block.name === name)))
      .catch(() => {
        // Without alerts, the sentence is left out rather than claiming all is well.
        if (!controller.signal.aborted) {
          setOpen(null);
        }
      });

    return () => controller.abort();
  }, [kind, name]);

  if (open === null) {
    return null;
  }

  const worst = [...open].sort((a, b) => (a.severity === b.severity ? 0 : a.severity === "Warning" ? -1 : 1))[0];

  return (
    <p className={`block-status ${worst ? worst.severity.toLowerCase() : "ok"}`} role="status">
      <i aria-hidden="true" />
      {worst ? (
        <>
          <span>
            <b>{alertTitle(worst.rule, worst.block, worst.value, false)}.</b>
            {open.length > 1 && ` ${open.length - 1} more ${open.length === 2 ? "alert is" : "alerts are"} open.`}
          </span>
          <Link to={`/alerts/${encodeURIComponent(worst.id)}`}>Alert details</Link>
        </>
      ) : (
        <span>
          <b>{label ?? name} is fine.</b> No alert is open.
        </span>
      )}
    </p>
  );
}
