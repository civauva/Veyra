import { useEffect } from "react";
import { useLocation, useSearchParams } from "react-router";

import { ScreenBar } from "../ui/ScreenBar";
import { useShell } from "../app/shellContext";
import { useViewMode } from "../ui/ViewToggle";
import type { MetricsOverview, MetricRange } from "../api/types";
import { ContainerCardView, DatabaseCardView, HostCardView, ServiceCardView } from "./MetricCards";
import { MetricBrowser } from "./MetricBrowser";
import { formatAmount, formatRate } from "./metricFormat";
import { rangeOf, rangeWords, withRange } from "./metricRange";
import { RangePicker } from "./RangePicker";
import { FigureName, PanelName } from "./TileName";
import { useMetricsOverview } from "./useMetricsOverview";
import "./metrics.css";

/**
 * The Metrics screen: every host, database server, container, and service that sends metrics, as cards, and a browser
 * of every metric name. The range, the tab, and the search are kept in the URL, so a link shows the same view.
 */
export function MetricsScreen() {
  const [params, setParams] = useSearchParams();
  const range = rangeOf(params);
  const tab = params.get("tab") === "all" ? "all" : "overview";
  const [mode] = useViewMode();
  const search = params.get("search") ?? "";
  const { status } = useShell();
  // The server keeps only the cards of the search and the environment (VEY-67); a change asks again at once.
  const { data, error } = useMetricsOverview(range, search, status.environment);
  const { hash } = useLocation();
  const loaded = data !== null;

  // A trail's group, such as Database servers, leads back here to its section (VEY-62), once the cards are drawn.
  useEffect(() => {
    if (loaded && hash.length > 1) {
      document.getElementById(decodeURIComponent(hash.slice(1)))?.scrollIntoView?.({ block: "start" });
    }
  }, [hash, loaded]);

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params);

    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    setParams(next, { replace: true });
  }

  return (
    <main className="metrics">
      <ScreenBar trail={[{ label: "Metrics" }]} view>
        <RangePicker value={range} onChange={(next) => setParams(withRange(params, next), { replace: true })} />
      </ScreenBar>
      <div className="metrics-head">
        <div>
          <h1>Metrics</h1>
          <p>
            {tab === "all"
              ? "Every metric Veyra has received, by name. Pick one to chart it and split it by an attribute."
              : "Hosts and services that send metrics over OTLP. Numbers are the last 5 minutes; " +
                `lines are ${rangeWords(range)}.`}
          </p>
        </div>
        {tab === "overview" && (
          <label className="metrics-search">
            <span className="visually-hidden">Find a host or service</span>
            <input
              type="search"
              value={search}
              placeholder="Find a host or service"
              onChange={(event) => update({ search: event.target.value })}
            />
          </label>
        )}
      </div>

      {mode === "advanced" && (
        <div className="metrics-tabs" role="tablist" aria-label="Views">
          <button
            type="button"
            role="tab"
            aria-selected={tab === "overview"}
            onClick={() => update({ tab: null, metric: null })}
          >
            Overview
          </button>
          <button type="button" role="tab" aria-selected={tab === "all"} onClick={() => update({ tab: "all" })}>
            All metrics {data && <b>{data.usage.names}</b>}
          </button>
        </div>
      )}

      {error && (
        <p className="metrics-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `Metrics could not be loaded: ${error}`}
        </p>
      )}

      {tab === "all" ? (
        <MetricBrowser range={range} />
      ) : data ? (
        <Overview overview={data} range={range} search={search} advanced={mode === "advanced"} />
      ) : (
        !error && <p className="metrics-empty">Loading metrics…</p>
      )}
    </main>
  );
}

/** Props for {@link Overview}. */
interface OverviewProps {
  /** The cards the server kept for the search and the environment, with its counts over them. */
  overview: MetricsOverview;
  /** The window of the cards' lines. */
  range: MetricRange;
  /** The search the cards were asked for, to name in the message when none match. */
  search: string;
  /** Whether to show the usage counts of the Advanced view. */
  advanced: boolean;
}

/**
 * The usage strip and the cards the server kept. The server filters by the search and the chosen environment, where
 * hosts whose metrics name no environment belong to every one, and counts the strip's figures (VEY-67); this only
 * lays them out.
 */
function Overview({ overview, range, search, advanced }: OverviewProps) {
  const now = new Date(overview.at);
  const { usage, summary, hosts, services, databases, containers } = overview;
  const filling = summary.fillingDisks;
  const restarting = summary.restarting;

  if (!summary.anySent) {
    return (
      <div className="metrics-empty">
        <b>No metrics yet</b>
        <p>
          Point the OpenTelemetry SDK of a service, or a Collector with the <code>hostmetrics</code> receiver, at Veyra.
          The setup is in <code>docs/integrations</code>. Hosts and services appear here within a minute.
        </p>
      </div>
    );
  }

  return (
    <>
      <dl className="metrics-strip">
        <div>
          <FigureName name="Hosts">
            Hosts sending metrics from the Collector&apos;s hostmetrics receiver. Under the count: how many have a disk
            that is full within 30 days at the pace of its last day.
          </FigureName>
          <dd>
            {summary.hosts}{" "}
            <small>{filling === 0 ? "disks steady" : `${filling} ${filling === 1 ? "disk" : "disks"} filling`}</small>
          </dd>
        </div>
        <div>
          <FigureName name="Services">
            Services sending HTTP server or runtime metrics from their OpenTelemetry SDK.
          </FigureName>
          <dd>
            {summary.services} <small>with metrics</small>
          </dd>
        </div>
        {summary.databases > 0 && (
          <div>
            <FigureName name="Databases">
              Database servers a Collector watches with its database receivers, such as SQL Server and PostgreSQL.
            </FigureName>
            <dd>
              {summary.databases} <small>{summary.databases === 1 ? "server" : "servers"}</small>
            </dd>
          </div>
        )}
        {summary.containers > 0 && (
          <div>
            <FigureName name="Containers">
              Docker containers from the Collector&apos;s docker_stats receiver. Under the count: how many of them
              restarted {rangeWords(range, "in")}.
            </FigureName>
            <dd>
              {summary.containers} <small>{restarting === 0 ? "none restarting" : `${restarting} restarting`}</small>
            </dd>
          </div>
        )}
        {advanced && (
          <>
            <div>
              <FigureName name="Series">
                Each metric name with one set of attributes is one series. Veyra keeps at most{" "}
                {usage.maxSeries.toLocaleString("en-US")}; points of new series beyond that are dropped, so a count near
                the limit needs a look, often at an attribute with too many values.
              </FigureName>
              <dd>
                {usage.series.toLocaleString("en-US")} <small>of {usage.maxSeries.toLocaleString("en-US")}</small>
              </dd>
            </div>
            <div>
              <FigureName name="Points">
                Metric points received per second over the last 5 minutes, from every sender. A sudden fall can mean a
                sender stopped.
              </FigureName>
              <dd>
                {formatRate(usage.pointsPerSecond)} <small>per second</small>
              </dd>
            </div>
            <div>
              <FigureName name="Storage">
                The disk space metrics take in PostgreSQL: series and points with their indexes. Points stay
                {usage.retentionDays} days, then are dropped.
              </FigureName>
              <dd>
                {formatAmount(usage.usedBytes)} <small>{usage.retentionDays} days kept</small>
              </dd>
            </div>
          </>
        )}
      </dl>

      {hosts.length > 0 && (
        <section aria-labelledby="metrics-hosts">
          <div className="metrics-section">
            <PanelName id="metrics-hosts" name="Hosts" label="a host card">
              Each card is a host: CPU over the last 5 minutes, memory, and its fullest disk, as rings that turn amber
              from 75% and red from 90%. The line is CPU {rangeWords(range, "over")}; the foot names a disk that is full
              within 30 days at the pace of its last day.
            </PanelName>
            <span>from the Collector&apos;s hostmetrics receiver</span>
          </div>
          <div className="metric-cards">
            {hosts.map((host) => (
              <HostCardView key={host.name} host={host} range={range} now={now} />
            ))}
          </div>
        </section>
      )}

      {databases.length > 0 && (
        <section aria-labelledby="metrics-databases">
          <div className="metrics-section">
            <PanelName id="metrics-databases" name="Database servers" label="a database server card">
              Each card is a database server. SQL Server shows batch requests per second, sessions blocked now, and the
              longest query running now, then failed SQL Agent jobs or a full backup older than 26 hours. PostgreSQL
              shows its connections of the maximum instead, and how close it is to transaction ID wraparound.
            </PanelName>
            <span>from the Collector&apos;s database receivers</span>
          </div>
          <div className="metric-cards">
            {databases.map((database) => (
              <DatabaseCardView
                key={`${database.engine}-${database.server}`}
                database={database}
                range={range}
                now={now}
              />
            ))}
          </div>
        </section>
      )}

      {containers.length > 0 && (
        <section aria-labelledby="metrics-containers">
          <div className="metrics-section">
            <PanelName id="metrics-containers" name="Containers" label="a container card">
              Each card is a container: CPU over the last 5 minutes as a share of one CPU, so two busy CPUs read 200%,
              memory of its limit, and restarts {rangeWords(range, "in")}. A card is marked from 90% of its memory limit
              or two restarts.
            </PanelName>
            <span>from the Collector&apos;s docker_stats receiver</span>
          </div>
          <div className="metric-cards">
            {containers.map((container) => (
              <ContainerCardView
                key={`${container.host ?? ""}-${container.name}`}
                container={container}
                range={range}
                now={now}
              />
            ))}
          </div>
        </section>
      )}

      {services.length > 0 && (
        <section aria-labelledby="metrics-services">
          <div className="metrics-section">
            <PanelName id="metrics-services" name="Services" label="a service card">
              Each card is a service: HTTP requests per second, the 95th percentile of their duration, and the share
              answered with a 5xx status, over the last 5 minutes. They come from http.server.request.duration. The line
              is p95 {rangeWords(range, "over")}.
            </PanelName>
            <span>from the OpenTelemetry SDKs: HTTP server and runtime metrics</span>
          </div>
          <div className="metric-cards">
            {services.map((service) => (
              <ServiceCardView
                key={`${service.name}-${service.environment ?? ""}`}
                service={service}
                range={range}
                now={now}
              />
            ))}
          </div>
        </section>
      )}

      {hosts.length === 0 && services.length === 0 && databases.length === 0 && containers.length === 0 && (
        <p className="metrics-empty">No host or service matches “{search}”.</p>
      )}
    </>
  );
}
