import { useSearchParams } from "react-router";

import { api } from "../api/client";
import { useShell } from "../app/shellContext";
import type { MetricsOverview, MetricRange } from "../api/types";
import { HostCardView, ServiceCardView } from "./MetricCards";
import { MetricBrowser } from "./MetricBrowser";
import { formatAmount, formatRate } from "./metricFormat";
import { RangePicker, rangeFrom } from "./RangePicker";
import { useMetricData } from "./useMetricData";
import "./metrics.css";

/**
 * The Metrics screen: every host and service that sends metrics, as cards, and a browser of every metric name. The
 * range, the tab, and the search are kept in the URL, so a link shows the same view.
 */
export function MetricsScreen() {
  const [params, setParams] = useSearchParams();
  const range = rangeFrom(params.get("range"));
  const tab = params.get("tab") === "all" ? "all" : "overview";
  const search = params.get("search") ?? "";
  const { status } = useShell();
  const { data, error } = useMetricData(`overview-${range}`, (signal) => api.metricsOverview(range, signal));

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
      <div className="metrics-head">
        <div>
          <h1>Metrics</h1>
          <p>
            {tab === "all"
              ? "Every metric Veyra has received, by name. Pick one to chart it and split it by an attribute."
              : `Hosts and services that send metrics over OTLP. Numbers are the last 5 minutes; lines are the last ${range}.`}
          </p>
        </div>
        <RangePicker value={range} onChange={(next) => update({ range: next === "6h" ? null : next })} />
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

      {error && (
        <p className="metrics-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `Metrics could not be loaded: ${error}`}
        </p>
      )}

      {tab === "all" ? (
        <MetricBrowser range={range} />
      ) : data ? (
        <Overview overview={data} range={range} search={search} environment={status.environment} />
      ) : (
        !error && <p className="metrics-empty">Loading metrics…</p>
      )}
    </main>
  );
}

/** Props for {@link Overview}. */
interface OverviewProps {
  /** The hosts, services, and storage use. */
  overview: MetricsOverview;
  /** The window of the cards' lines. */
  range: MetricRange;
  /** Text a card's name must hold. */
  search: string;
  /** The environment chosen in the top bar, or null for all (VEY-23). */
  environment: string | null;
}

/**
 * The usage strip and the host and service cards of the chosen environment. Hosts whose metrics name no environment
 * belong to every environment, as on the home.
 */
function Overview({ overview, range, search, environment }: OverviewProps) {
  const now = new Date(overview.at);
  const needle = search.trim().toLowerCase();
  const inEnvironment = (named: string | null, hostLike: boolean) =>
    environment === null || named === environment || (hostLike && named === null);
  const hosts = overview.hosts.filter(
    (host) => inEnvironment(host.environment, true) && host.name.toLowerCase().includes(needle),
  );
  const services = overview.services.filter(
    (service) => inEnvironment(service.environment, false) && service.name.toLowerCase().includes(needle),
  );
  const filling = overview.hosts.filter((host) => host.fillingDisk).length;
  const { usage } = overview;

  if (overview.hosts.length === 0 && overview.services.length === 0) {
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
          <dt>Hosts</dt>
          <dd>
            {overview.hosts.length}{" "}
            <small>{filling === 0 ? "disks steady" : `${filling} ${filling === 1 ? "disk" : "disks"} filling`}</small>
          </dd>
        </div>
        <div>
          <dt>Services</dt>
          <dd>
            {overview.services.length} <small>with metrics</small>
          </dd>
        </div>
        <div>
          <dt>Series</dt>
          <dd>
            {usage.series.toLocaleString("en-US")} <small>of {usage.maxSeries.toLocaleString("en-US")}</small>
          </dd>
        </div>
        <div>
          <dt>Points</dt>
          <dd>
            {formatRate(usage.pointsPerSecond)} <small>per second</small>
          </dd>
        </div>
        <div>
          <dt>Storage</dt>
          <dd>
            {formatAmount(usage.usedBytes)} <small>{usage.retentionDays} days kept</small>
          </dd>
        </div>
      </dl>

      {hosts.length > 0 && (
        <section aria-labelledby="metrics-hosts">
          <div className="metrics-section">
            <h2 id="metrics-hosts">Hosts</h2>
            <span>from the Collector&apos;s hostmetrics receiver</span>
          </div>
          <div className="metric-cards">
            {hosts.map((host) => (
              <HostCardView key={host.name} host={host} range={range} now={now} />
            ))}
          </div>
        </section>
      )}

      {services.length > 0 && (
        <section aria-labelledby="metrics-services">
          <div className="metrics-section">
            <h2 id="metrics-services">Services</h2>
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

      {hosts.length === 0 && services.length === 0 && (
        <p className="metrics-empty">No host or service matches “{search}”.</p>
      )}
    </>
  );
}
