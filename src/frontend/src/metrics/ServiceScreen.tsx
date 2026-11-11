import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { ChartLine, ServiceMetrics } from "../api/types";
import { defaultFilters, filtersToUrl } from "../logs/logFilters";
import { LineChart } from "./LineChart";
import { ServiceIcon } from "./MetricCards";
import { formatAmount, formatRate, formatSeconds, formatShare } from "./metricFormat";
import { RangePicker, rangeFrom } from "./RangePicker";
import { useMetricData } from "./useMetricData";
import "./metrics.css";

/**
 * One service: request durations by percentile, requests by status class, its busiest routes, and CPU, memory, and
 * runtime where the SDK sends them, over the chosen range.
 */
export function ServiceScreen() {
  const { service = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeFrom(params.get("range"));
  const environment = params.get("environment");
  const { data, notFound, error } = useMetricData(`service-${service}-${environment ?? ""}-${range}`, (signal) =>
    api.serviceMetrics(service, environment, range, signal),
  );

  function pick(next: string) {
    const changed = new URLSearchParams(params);
    changed.set("range", next);
    setParams(changed, { replace: true });
  }

  return (
    <main className="metrics">
      <Link className="metrics-back" to={`/metrics?range=${range}`}>
        ← Metrics
      </Link>
      <div className="metrics-detail-head">
        <span className="metric-icon large service">
          <ServiceIcon size={26} />
        </span>
        <div>
          <h1>{service}</h1>
          {data && <Tags service={data} />}
        </div>
        <RangePicker value={range} onChange={pick} />
      </div>

      {error && (
        <p className="metrics-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `The service could not be loaded: ${error}`}
        </p>
      )}
      {notFound && (
        <p className="metrics-empty">
          {service} sent no HTTP or runtime metrics within the last {range}.
        </p>
      )}
      {data && <ServicePanels service={data} />}
    </main>
  );
}

function Tags({ service }: { service: ServiceMetrics }) {
  const { card } = service;
  const logs = filtersToUrl({ ...defaultFilters, services: [card.name] });
  const instances =
    service.instanceNames.length === 0
      ? null
      : service.instanceNames.length <= 3
        ? `${service.instanceNames.length} ${service.instanceNames.length === 1 ? "instance" : "instances"}: ${service.instanceNames.join(", ")}`
        : `${service.instanceNames.length} instances`;

  return (
    <ul className="metric-tags">
      {[card.environment, card.runtime, instances]
        .filter((tag): tag is string => Boolean(tag))
        .map((tag) => (
          <li key={tag}>{tag}</li>
        ))}
      <li>
        <Link to={`/logs?${logs}`}>Open logs</Link>
      </li>
    </ul>
  );
}

function ServicePanels({ service }: { service: ServiceMetrics }) {
  const { card } = service;
  const find = (lines: ChartLine[], label: string) => lines.find((line) => line.label === label);
  const pick = (lines: ChartLine[], picks: [string, string, string?][]) =>
    picks.flatMap(([label, color, name]) => {
      const line = find(lines, label);

      return line ? [{ line, color, name: name ?? label }] : [];
    });
  const mostRequests = Math.max(1e-9, ...service.routes.map((route) => route.requestsPerSecond));
  const hasRuntime = service.runtime.some((line) => line.latest !== null);
  const hasResources = service.resources.some((line) => line.latest !== null);

  return (
    <>
      <dl className="metrics-strip">
        <div>
          <dt>Requests</dt>
          <dd>
            {formatRate(card.requestsPerSecond)} <small>per second</small>
          </dd>
        </div>
        <div>
          <dt>Latency p95</dt>
          <dd>
            {formatSeconds(card.p95Seconds)}{" "}
            <small>p50 {formatSeconds(find(service.latency, "p50")?.latest ?? null)}</small>
          </dd>
        </div>
        <div>
          <dt>5xx answers</dt>
          <dd className={card.serverErrorRatio !== null && card.serverErrorRatio >= 0.01 ? "bad" : undefined}>
            {formatShare(card.serverErrorRatio)}
          </dd>
        </div>
        <div>
          <dt>CPU</dt>
          <dd>{formatShare(card.cpuRatio)}</dd>
        </div>
        <div>
          <dt>Memory</dt>
          <dd>{formatAmount(card.memoryBytes)}</dd>
        </div>
      </dl>

      <div className="metric-panels">
        <section className="metric-panel" aria-labelledby="service-latency">
          <div className="metric-panel-head">
            <h2 id="service-latency">Latency</h2>
            <span>from http.server.request.duration</span>
          </div>
          <LineChart
            title="Request duration"
            format={formatSeconds}
            series={[
              ...pick(service.latency, [["p95", "var(--metric-violet)"]]).map((one) => ({ ...one, filled: true })),
              ...pick(service.latency, [["p50", "var(--accent)"]]),
              ...pick(service.latency, [["p99", "var(--error-fg)"]]).map((one) => ({ ...one, dashed: true })),
            ]}
          />
        </section>

        <section className="metric-panel" aria-labelledby="service-requests">
          <div className="metric-panel-head">
            <h2 id="service-requests">Requests</h2>
            <span>per second</span>
          </div>
          <LineChart
            title="Requests per second"
            format={formatRate}
            series={[
              ...pick(service.requests, [["ok", "var(--ok-fg)", "2xx and 3xx"]]).map((one) => ({
                ...one,
                filled: true,
              })),
              ...pick(service.requests, [
                ["4xx", "var(--warning-fg)"],
                ["5xx", "var(--error-fg)"],
              ]),
            ]}
          />
        </section>

        <section className="metric-panel wide" aria-labelledby="service-routes">
          <div className="metric-panel-head">
            <h2 id="service-routes">Routes</h2>
            <span>busiest first, last 5 minutes</span>
          </div>
          {service.routes.length === 0 ? (
            <p className="metrics-empty">No requests in the last 5 minutes.</p>
          ) : (
            <table className="route-table">
              <thead>
                <tr>
                  <th scope="col">Route</th>
                  <th scope="col">Requests</th>
                  <th scope="col" className="number">
                    p95
                  </th>
                  <th scope="col" className="number">
                    5xx
                  </th>
                </tr>
              </thead>
              <tbody>
                {service.routes.map((route) => (
                  <tr key={`${route.method ?? ""} ${route.route}`}>
                    <th scope="row">
                      <code>
                        {route.method ? `${route.method} ` : ""}
                        {route.route}
                      </code>
                    </th>
                    <td data-label="Requests">
                      <span
                        className="route-bar"
                        style={{ width: `${(route.requestsPerSecond / mostRequests) * 120}px` }}
                        aria-hidden="true"
                      />
                      {formatRate(route.requestsPerSecond)} /s
                    </td>
                    <td data-label="p95" className="number">
                      {formatSeconds(route.p95Seconds)}
                    </td>
                    <td
                      data-label="5xx"
                      className={
                        route.serverErrorRatio !== null && route.serverErrorRatio >= 0.01 ? "number bad" : "number"
                      }
                    >
                      {formatShare(route.serverErrorRatio)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>

        {hasResources && (
          <section className="metric-panel" aria-labelledby="service-resources">
            <div className="metric-panel-head">
              <h2 id="service-resources">CPU and memory</h2>
            </div>
            <LineChart
              title="CPU in use"
              max={1}
              format={formatShare}
              series={pick(service.resources, [["cpu", "var(--accent)", "CPU"]])}
            />
            <LineChart
              title="Memory"
              height={120}
              format={(value) => formatAmount(value)}
              series={pick(service.resources, [["memory", "var(--ok-fg)", "memory"]]).map((one) => ({
                ...one,
                filled: true,
              }))}
            />
          </section>
        )}

        {hasRuntime && (
          <section className="metric-panel" aria-labelledby="service-runtime">
            <div className="metric-panel-head">
              <h2 id="service-runtime">{card.runtime ?? "Runtime"}</h2>
            </div>
            <LineChart
              title="Runtime"
              format={formatRate}
              series={pick(service.runtime, [
                ["gc", "var(--metric-violet)", "collections per minute"],
                ["queue", "var(--warning-fg)", "thread pool queue"],
                ["threads", "var(--metric-teal)", "threads"],
              ])}
            />
          </section>
        )}
      </div>
    </>
  );
}
