import type { AlertLatency } from "../api/types";
import { formatRatio } from "../home/homeFormat";
import { formatRate, formatSeconds } from "../metrics/metricFormat";

/** Props for {@link SlowRoutes}. */
interface SlowRoutesProps {
  /** The latency alert's request durations, with its routes. */
  latency: AlertLatency;
}

/** The routes are slow above this p95, in seconds, and drawn red. */
const slow = 1;

/** The service's routes while the alert was open, slowest first, so a person sees which requests were slow. */
export function SlowRoutes({ latency }: SlowRoutesProps) {
  return (
    <section className="glass alert-panel" aria-labelledby="alert-routes">
      <h2 id="alert-routes" className="cap">
        Slowest routes during the alert
      </h2>
      {latency.routes.length === 0 ? (
        <p className="fine">The service sent no request durations with routes in this period.</p>
      ) : (
        <div className="routes-scroll">
          <table className="slow-routes">
            <thead>
              <tr>
                <th scope="col">Route</th>
                <th scope="col">Requests</th>
                <th scope="col">p95</th>
                <th scope="col">5xx</th>
              </tr>
            </thead>
            <tbody>
              {latency.routes.map((route) => (
                <tr key={`${route.method ?? ""} ${route.route}`}>
                  <td>
                    <code>
                      {route.method ? `${route.method} ` : ""}
                      {route.route}
                    </code>
                  </td>
                  <td>{formatRate(route.requestsPerSecond)} /s</td>
                  <td className={route.p95Seconds !== null && route.p95Seconds >= slow ? "bad" : undefined}>
                    {formatSeconds(route.p95Seconds)}
                  </td>
                  <td>{formatRatio(route.serverErrorRatio)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
