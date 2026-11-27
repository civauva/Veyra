import { api } from "../../api/client";
import type { AlertListItem, BlockKind } from "../../api/types";
import { useMetricData } from "../useMetricData";

/** How often a screen reads its open alerts again: a rule runs every minute at the most. */
const alertsRefreshMilliseconds = 60_000;

/** The most open alerts a page of the API holds; a block has one alert a rule and part at the most. */
const maxOpenAlerts = 200;

/** What {@link useOpenAlerts} returns. */
export interface OpenAlerts {
  /** The alerts open now, or null until they are read or when they could not be. */
  alerts: AlertListItem[] | null;
  /** True when they could not be read, so null means unknown rather than not yet read. */
  failed: boolean;
}

/**
 * The open alerts of one host, service, or database server, kept fresh every minute (VEY-62). A screen reads them
 * once and hands them to everything that tells from them, such as the banner and the database server's overview, so
 * the two never disagree.
 *
 * @param kind - Whether the block is a host, a service, or a database server.
 * @param name - Its block name; for a database server, its engine and server, as `microsoft.sql_server sql-prod-01`.
 * @returns The alerts open now, and whether they could not be read.
 */
export function useOpenAlerts(kind: BlockKind, name: string): OpenAlerts {
  const { data, error } = useMetricData(
    `open-alerts-${kind}-${name}`,
    async (signal) => {
      // The server keeps exactly this block's alerts, the worst first (VEY-68).
      const page = await api.alerts(
        {
          days: 1,
          state: "open",
          rules: [],
          text: "",
          environment: null,
          kind,
          block: name,
          order: "worst",
          limit: maxOpenAlerts,
        },
        null,
        signal,
      );

      return page.items;
    },
    { refreshMs: alertsRefreshMilliseconds },
  );

  return { alerts: data, failed: data === null && error !== null };
}
