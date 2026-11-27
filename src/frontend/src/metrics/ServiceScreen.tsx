import { useShell } from "../app/shellContext";
import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { ChartLine, MetricRange, ServiceMetrics } from "../api/types";
import { ScreenBar } from "../ui/ScreenBar";
import { useViewMode } from "../ui/ViewToggle";
import { defaultFilters, filtersToUrl } from "../logs/logFilters";
import { BlockPage } from "./BlockPage";
import { BlockStatus } from "./BlockStatus";
import { LineChart } from "./LineChart";
import { ServiceIcon } from "./MetricCards";
import { formatAmount, formatRate, formatSeconds, formatShare } from "./metricFormat";
import { parentCrumbs, rangeOf, rangeWords, withRange } from "./metricRange";
import { RangePicker } from "./RangePicker";
import { FigureName, PanelName } from "./TileName";
import { useOpenAlerts } from "./database/useOpenAlerts";
import { useMetricData } from "./useMetricData";
import "./metrics.css";

/**
 * One service over the chosen range. Simple shows how it is, requests, p95 latency, server errors, and its slowest
 * routes; Advanced adds request durations by percentile, requests by status class, every route, and CPU, memory, and
 * runtime where the SDK sends them (VEY-37).
 */
export function ServiceScreen() {
  const { trailParents } = useShell();
  const { service = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeOf(params);
  const environment = params.get("environment");
  const { data, notFound, error } = useMetricData(`service-${service}-${environment ?? ""}-${range}`, (signal) =>
    api.serviceMetrics(service, environment, range, signal),
  );
  const [mode] = useViewMode();
  // Read once for the banner and the rail beside the screen, so the two never disagree (VEY-63).
  const { alerts } = useOpenAlerts("service", service);

  return (
    <main className="metrics">
      <ScreenBar
        trail={[...parentCrumbs(trailParents?.service, range, "Services", "metrics-services"), { label: service }]}
        view
      >
        <RangePicker value={range} onChange={(next) => setParams(withRange(params, next), { replace: true })} />
      </ScreenBar>
      <BlockPage kind="service" name={service} alerts={alerts}>
        <div className="metrics-detail-head">
          <span className="metric-icon large service">
            <ServiceIcon size={26} />
          </span>
          <div>
            <h1>{service}</h1>
            {data && <Tags service={data} />}
          </div>
        </div>
        {data && <BlockStatus kind="service" name={service} alerts={alerts} />}

        {error && (
          <p className="metrics-error" role="alert">
            {data ? `Could not refresh: ${error}.` : `The service could not be loaded: ${error}`}
          </p>
        )}
        {notFound && (
          <p className="metrics-empty">
            {service} sent no HTTP or runtime metrics {rangeWords(range, "within")}.
          </p>
        )}
        {data &&
          (mode === "advanced" ? <ServicePanels service={data} range={range} /> : <ServiceBasics service={data} />)}
      </BlockPage>
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

/** The name of the requests figure, with its explanation. */
function RequestsName() {
  return (
    <FigureName name="Requests">
      HTTP requests the service answered per second over the last 5 minutes, over every instance, from the SDK&apos;s
      http.server.request.duration.
    </FigureName>
  );
}

/** The name of the p95 latency figure, with its explanation. */
function LatencyName() {
  return (
    <FigureName name="Latency p95">
      95 of every 100 requests over the last 5 minutes took this long or less, so it shows how the slow end of normal
      feels.
    </FigureName>
  );
}

/** The name of the server errors figure, with its explanation. */
function ServerErrorsName() {
  return (
    <FigureName name="5xx answers">
      The share of requests over the last 5 minutes answered with a 5xx status, an error on the server. It turns red
      from 1%.
    </FigureName>
  );
}

/** The Simple view: three figures and the five slowest routes. */
function ServiceBasics({ service }: { service: ServiceMetrics }) {
  const { card } = service;
  // The server picks them from every route, not only the busiest it lists (VEY-68).
  const slowest = service.slowestRoutes;

  return (
    <>
      <dl className="metrics-strip">
        <div>
          <RequestsName />
          <dd>
            {formatRate(card.requestsPerSecond)} <small>per second</small>
          </dd>
        </div>
        <div>
          <LatencyName />
          <dd>{formatSeconds(card.p95Seconds)}</dd>
        </div>
        <div>
          <ServerErrorsName />
          <dd className={card.serverErrorRatio !== null && card.serverErrorRatio >= 0.01 ? "bad" : undefined}>
            {formatShare(card.serverErrorRatio)}
          </dd>
        </div>
      </dl>
      <div className="metric-panels">
        <section className="metric-panel wide" aria-labelledby="service-slowest">
          <div className="metric-panel-head">
            <PanelName id="service-slowest" name="Slowest routes">
              Of the ten busiest routes over the last 5 minutes, the five whose 95th percentile of duration was highest,
              with their requests per second and share of 5xx answers. Start with these when the service feels slow.
            </PanelName>
            <span>p95, last 5 minutes</span>
          </div>
          {slowest.length === 0 ? (
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
                {slowest.map((route) => (
                  <tr key={`${route.method ?? ""} ${route.route}`}>
                    <th scope="row">
                      <code>
                        {route.method ? `${route.method} ` : ""}
                        {route.route}
                      </code>
                    </th>
                    <td data-label="Requests">{formatRate(route.requestsPerSecond)} /s</td>
                    <td data-label="p95" className="number">
                      {formatSeconds(route.p95Seconds)}
                    </td>
                    <td data-label="5xx" className="number">
                      {formatShare(route.serverErrorRatio)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <p className="metric-more">
          p50 and p99, requests by status class, every route, CPU and memory, and the runtime are in the Advanced view.
        </p>
      </div>
    </>
  );
}

/** Props for {@link ServicePanels}. */
interface ServicePanelsProps {
  /** The service's metrics. */
  service: ServiceMetrics;
  /** The range the screen shows, for the panels' words. */
  range: MetricRange;
}

/** The Advanced view: every figure and panel. */
function ServicePanels({ service, range }: ServicePanelsProps) {
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
          <RequestsName />
          <dd>
            {formatRate(card.requestsPerSecond)} <small>per second</small>
          </dd>
        </div>
        <div>
          <LatencyName />
          <dd>
            {formatSeconds(card.p95Seconds)}{" "}
            <small>p50 {formatSeconds(find(service.latency, "p50")?.latest ?? null)}</small>
          </dd>
        </div>
        <div>
          <ServerErrorsName />
          <dd className={card.serverErrorRatio !== null && card.serverErrorRatio >= 0.01 ? "bad" : undefined}>
            {formatShare(card.serverErrorRatio)}
          </dd>
        </div>
        <div>
          <FigureName name="CPU">
            CPU the service&apos;s processes use over the last 5 minutes, of the CPUs they may use, from the SDK&apos;s
            process or runtime metrics.
          </FigureName>
          <dd>{formatShare(card.cpuRatio)}</dd>
        </div>
        <div>
          <FigureName name="Memory">
            Memory the service&apos;s processes use now, added up over its instances, from the SDK&apos;s process or
            runtime metrics. Memory that only grows can be a leak.
          </FigureName>
          <dd>{formatAmount(card.memoryBytes)}</dd>
        </div>
      </dl>

      <div className="metric-panels">
        <section className="metric-panel" aria-labelledby="service-latency">
          <div className="metric-panel-head">
            <PanelName id="service-latency" name="Latency">
              How long requests took {rangeWords(range, "over")}: p50 is the typical request, p95 and p99 the slow ones.
              A p99 far above p95 means a few requests are much slower than the rest.
            </PanelName>
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
            <PanelName id="service-requests" name="Requests" label="the Requests chart">
              Requests per second {rangeWords(range, "over")}, by the class of their status: 2xx and 3xx answered well,
              4xx were refused, such as a page not found, and 5xx failed on the server. A rise in 5xx needs a look.
            </PanelName>
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
            <PanelName id="service-routes" name="Routes">
              The ten busiest routes over the last 5 minutes, with requests per second, the 95th percentile of their
              duration, and the share answered with a 5xx status, which turns red from 1%.
            </PanelName>
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
              <PanelName id="service-resources" name="CPU and memory">
                CPU the service&apos;s processes use, of the CPUs they may use, and their memory, added up over its
                instances, {rangeWords(range, "over")}. From the SDK&apos;s process or runtime metrics.
              </PanelName>
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
              <PanelName id="service-runtime" name={card.runtime ?? "Runtime"} label="Runtime">
                The runtime&apos;s own metrics {rangeWords(range, "over")}, where the SDK sends them: garbage
                collections per minute, work waiting in the thread pool queue, and the pool&apos;s threads. A queue that
                keeps growing means the service cannot keep up.
              </PanelName>
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
