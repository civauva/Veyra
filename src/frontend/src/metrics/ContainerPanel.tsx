import { useEffect, useState } from "react";
import { Link, useLocation } from "react-router";

import { api } from "../api/client";
import type { AlertListItem, ContainerCard, MetricRange } from "../api/types";
import { alertTitle, containerAnchor } from "../alerts/ruleWords";
import { defaultFilters, filtersToUrl } from "../logs/logFilters";
import { ContainerIcon } from "./MetricCards";
import { formatAmount, formatCpuPercent, formatShare, formatSpan, shareTone } from "./metricFormat";
import { Sparkline } from "./Sparkline";

/** Props for {@link ContainerPanel}. */
interface ContainerPanelProps {
  /** The host the containers run on. */
  host: string;
  /** Its containers, by name. */
  containers: ContainerCard[];
  /** The range the restarts are counted over. */
  range: MetricRange;
}

/**
 * The Docker containers of a host (ADR 0018): each with its image, CPU now and over the range, memory of its limit as
 * a bar, restarts, uptime, and a way into its logs. A container with an open alert says so and leads to it; one
 * reached by a link to its row is scrolled to and marked. A table on wide screens, a card per container on phones.
 */
export function ContainerPanel({ host, containers, range }: ContainerPanelProps) {
  const alerts = useContainerAlerts(host);
  const { hash } = useLocation();
  const picked = hash.length > 1 ? decodeURIComponent(hash.slice(1)) : null;
  const restarting = containers.filter((container) => (container.restarts ?? 0) > 0).length;

  useEffect(() => {
    if (!picked) {
      return undefined;
    }

    // Panels above the row may still be loading and push it away, so the row is kept in view while the screen
    // changes, until the person scrolls or clicks themselves, for at most ten seconds on a slow connection. The
    // changes are watched rather than the page's size: the app's frame never grows, its screen scrolls inside it.
    const show = () => document.getElementById(picked)?.scrollIntoView?.({ block: "center" });
    const changing = new MutationObserver(show);
    const stops = ["wheel", "touchmove", "keydown", "pointerdown"];
    const stop = () => {
      changing.disconnect();
      stops.forEach((name) => window.removeEventListener(name, stop));
    };

    show();
    changing.observe(document.body, { childList: true, subtree: true, characterData: true });
    stops.forEach((name) => window.addEventListener(name, stop, { passive: true }));
    const timer = window.setTimeout(stop, 10000);

    return () => {
      window.clearTimeout(timer);
      stop();
    };
  }, [picked]);

  return (
    <section className="metric-panel wide container-panel" aria-labelledby="host-containers">
      <div className="metric-panel-head">
        <h2 id="host-containers">Containers</h2>
        <span>
          {containers.length === 1 ? "1 container" : `${containers.length} containers`} ·{" "}
          {restarting === 0 ? "none restarted" : `${restarting} restarted`} in {range}
        </span>
      </div>
      <table className="container-table">
        <thead>
          <tr>
            <th scope="col">Container</th>
            <th scope="col">CPU</th>
            <th scope="col">Memory of its limit</th>
            <th scope="col">Restarts</th>
            <th scope="col">Up</th>
            <th scope="col">
              <span className="visually-hidden">Logs</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {containers.map((container) => (
            <ContainerRow
              key={container.name}
              container={container}
              range={range}
              alerts={alerts.filter((alert) => alert.block.name === container.name)}
              picked={picked === containerAnchor(container.name)}
            />
          ))}
        </tbody>
      </table>
      <p className="metric-note">
        CPU is a share of one CPU, so a container busy on two reads 200%. Without a memory limit of its own, Docker
        counts the host&apos;s memory as its limit.
      </p>
    </section>
  );
}

/** Props for {@link ContainerRow}. */
interface ContainerRowProps {
  /** The container. */
  container: ContainerCard;
  /** The range its restarts are counted over. */
  range: MetricRange;
  /** Its open alerts, the worst first. */
  alerts: AlertListItem[];
  /** Whether a link led to this row. */
  picked: boolean;
}

/** One container: a row of the table, laid out as a card on phones. */
function ContainerRow({ container, range, alerts, picked }: ContainerRowProps) {
  const worst = alerts[0];
  const tone = shareTone(container.memoryRatio);
  const restarting = (container.restarts ?? 0) > 0;
  const classes = [worst ? worst.severity.toLowerCase() : null, picked ? "picked" : null].filter(Boolean).join(" ");
  const logs = filtersToUrl({ ...defaultFilters, services: [container.name] });

  return (
    <tr id={containerAnchor(container.name)} className={classes || undefined}>
      <th scope="row">
        <div className="container-name">
          <span className="metric-icon container">
            <ContainerIcon size={18} />
          </span>
          <span>
            <b>{container.name}</b>
            <small>{[container.image, container.id].filter(Boolean).join(" · ") || "image not reported"}</small>
            {worst && (
              <Link className={`container-alert ${worst.severity.toLowerCase()}`} to={`/alerts/${worst.id}`}>
                {alertTitle(worst.rule, worst.block, worst.value, false)}
              </Link>
            )}
          </span>
        </div>
      </th>
      <td className="container-cpu" data-label="CPU">
        <b>{formatCpuPercent(container.cpuPercent)}</b>
        <Sparkline line={container.cpu} color="var(--metric-pink)" />
      </td>
      <td className="container-memory" data-label="Memory">
        <span
          className="disk-bar"
          role="meter"
          aria-label={`${container.name} memory of its limit`}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round((container.memoryRatio ?? 0) * 100)}
        >
          <i className={tone} style={{ width: `${Math.min(1, container.memoryRatio ?? 0) * 100}%` }} />
        </span>
        <span className="disk-text">
          <b className={tone === "ok" ? undefined : tone}>{formatShare(container.memoryRatio)}</b>
          {container.memoryBytes !== null && container.memoryLimitBytes !== null && (
            <>
              {" "}
              · {formatAmount(container.memoryBytes)} of {formatAmount(container.memoryLimitBytes)}
            </>
          )}
        </span>
      </td>
      <td className="container-restarts" data-label="Restarts">
        {container.restarts === null ? (
          "—"
        ) : (
          <>
            <b className={restarting ? "warn" : undefined}>
              {container.restarts} in {range}
            </b>
            {container.restartsTotal !== null && <small>{container.restartsTotal} in all</small>}
          </>
        )}
      </td>
      <td className="container-up" data-label="Up">
        {container.uptimeSeconds === null ? "—" : formatSpan(container.uptimeSeconds)}
      </td>
      <td className="container-logs">
        <Link className="container-logs-link" to={`/logs?${logs}`} aria-label={`Logs of ${container.name}`}>
          Logs
        </Link>
      </td>
    </tr>
  );
}

/**
 * The open container alerts of a host, the warnings first. Without them, the rows are drawn without marks rather than
 * claiming all is well.
 *
 * @param host - The host the containers run on.
 * @returns The alerts; empty until they load or when they cannot.
 */
function useContainerAlerts(host: string): AlertListItem[] {
  const [alerts, setAlerts] = useState<AlertListItem[]>([]);

  useEffect(() => {
    const controller = new AbortController();

    api
      .alerts(
        { days: 1, state: "open", rules: ["container-restarts", "container-memory"], text: "", environment: null },
        null,
        controller.signal,
      )
      .then((page) =>
        setAlerts(
          page.items
            .filter((item) => item.block.kind === "container" && item.block.part === host)
            .sort((a, b) => (a.severity === b.severity ? 0 : a.severity === "Warning" ? -1 : 1)),
        ),
      )
      .catch(() => {
        if (!controller.signal.aborted) {
          setAlerts([]);
        }
      });

    return () => controller.abort();
  }, [host]);

  return alerts;
}
