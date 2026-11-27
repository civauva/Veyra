import { Link } from "react-router";

import type { BlockHealth, HealthCounts, HomeHostTile, HomeSnapshot } from "../../api/types";
import { HelpTip } from "../../ui/HelpTip";
import { databasePath, formatDays, formatDiskShare, serverName } from "../../alerts/ruleWords";
import { defaultFilters, filtersToUrl } from "../../logs/logFilters";
import { formatRatio } from "../homeFormat";
import { WeatherIcon, type WeatherKind } from "../WeatherIcon";

/** Props for {@link BlocksPanel}. */
interface BlocksPanelProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
  /**
   * The most services to draw, for the standard home, where the estate shares the screen. The alerted ones come first
   * and the rest are counted with a way to all of them, so no service is dropped silently (ADR 0007). The tiles are
   * narrower there, so each tile says its error share under its name.
   */
  limit?: number;
}

/**
 * The estate: every service seen in the last hour and every host, as tiles (VEY-24, VEY-26). A tile has its weather
 * from its open alerts only, its records and error share of the last five minutes, and a link to its logs or metrics.
 * A host's tile draws the disk that fills soonest, or else its fullest, so the home needs no list of disks of its own.
 * Shown beside the outlook on the standard home, in a column of its own on wide screens, and in a band on the wall.
 *
 * Hosts are those with an open alert and those reporting disks, so a host that is quiet is listed too.
 */
export function BlocksPanel({ snapshot, limit }: BlocksPanelProps) {
  // The server sorts, groups, and counts (VEY-68); the panel only fits what it has room for.
  const { estate } = snapshot;
  const services = estate.services;
  const shown = limit === undefined || services.length <= limit ? services : estate.servicesWorstFirst.slice(0, limit);
  const hidden = services.length - shown.length;
  // Beside the outlook the tiles are narrow, so the error share moves under the name, which keeps its room.
  const compact = limit !== undefined;
  const hosts = estate.hosts;
  // Database servers show once one has an open alert (ADR 0018); their card on Metrics lists them all.
  const databases = estate.databases;
  const allEnvironments = snapshot.environment === null;

  return (
    <section
      className="glass lg-panel lg-blocks"
      aria-label={databases.length > 0 ? "Services, hosts, and databases" : "Services and hosts"}
    >
      <div className="lg-head">
        <div className="home-name">
          <h2 className="cap">Services · last 5 minutes</h2>
          <HelpTip label="Services">
            Every service that sent logs in the last hour or has an open alert, with its log records and share of errors
            of the last 5 minutes. Only an open alert changes its weather; dry counts the services without one. A tile
            leads to the service&apos;s logs.
          </HelpTip>
        </div>
        <span className="lg-count">{dryCount(estate.serviceCounts, services.length)}</span>
      </div>
      {services.length === 0 ? (
        <p className="fine">No service has sent logs in the last hour.</p>
      ) : (
        <ul className="lg-tiles" aria-label="Services">
          {shown.map((block) => (
            <li key={block.id}>
              <Link
                className={`lg-tile ${tone(block.health)}`}
                to={`/logs?${filtersToUrl({ ...defaultFilters, services: [block.name] })}`}
                title={block.name}
              >
                <WeatherIcon kind={weatherOf(block.health)} />
                <span className="lg-tile-what">
                  <b>{block.name}</b>
                  <small>
                    {compact && `${formatRatio(block.errorRatio)} · `}
                    {block.records.toLocaleString("en-US")} records
                    {allEnvironments && block.environment ? ` · ${block.environment}` : ""}
                  </small>
                </span>
                {!compact && <span className="lg-tile-value">{formatRatio(block.errorRatio)}</span>}
              </Link>
            </li>
          ))}
        </ul>
      )}
      {hidden > 0 && (
        <Link className="lg-more lg-more-services" to="/logs">
          +{hidden} more {hidden === 1 ? "service" : "services"}
        </Link>
      )}

      <div className="lg-head lg-subhead">
        <div className="home-name">
          <h3 className="cap">Hosts</h3>
          <HelpTip label="Hosts">
            Every host that reports disks or has an open alert, with the disk that fills up soonest within 30 days, or
            else its fullest. Only an open alert changes its weather. A tile leads to the host&apos;s metrics.
          </HelpTip>
        </div>
        <span className="lg-count">{dryCount(estate.hostCounts, hosts.length)}</span>
      </div>
      {hosts.length === 0 ? (
        <p className="fine">Hosts appear with host metrics from the OpenTelemetry Collector.</p>
      ) : (
        <ul className="lg-tiles" aria-label="Hosts">
          {hosts.map((host) => (
            <li key={host.name}>
              <Link className={`lg-tile ${tone(host.health)}`} to={`/metrics/hosts/${encodeURIComponent(host.name)}`}>
                <WeatherIcon kind={weatherOf(host.health)} />
                <span className="lg-tile-what">
                  <b>{host.name}</b>
                  <small>{hostNote(host)}</small>
                  {host.disk !== null && (
                    <span className="lg-disk-bar lg-tile-disk" data-testid="host-disk" aria-hidden="true">
                      <i style={{ width: `${Math.min(Math.max(host.disk.ratio, 0), 1) * 100}%` }} />
                    </span>
                  )}
                </span>
                <span className="lg-tile-value kind-tag">host</span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      {databases.length > 0 && (
        <>
          <div className="lg-head lg-subhead">
            <div className="home-name">
              <h3 className="cap">Databases</h3>
              <HelpTip label="Databases">
                Database servers with an open alert, and how many alerts each has open. Every server is on the Metrics
                screen; a tile leads to its page there.
              </HelpTip>
            </div>
            <span className="lg-count">{dryCount(estate.databaseCounts, databases.length)}</span>
          </div>
          <ul className="lg-tiles" aria-label="Databases">
            {databases.map((block) => (
              <li key={block.id}>
                <Link className={`lg-tile ${tone(block.health)}`} to={databasePath(block.name)}>
                  <WeatherIcon kind={weatherOf(block.health)} />
                  <span className="lg-tile-what">
                    <b>{serverName(block.name)}</b>
                    <small>
                      {block.alertIds.length === 1 ? "1 alert open" : `${block.alertIds.length} alerts open`}
                      {allEnvironments && block.environment ? ` · ${block.environment}` : ""}
                    </small>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

/**
 * What a host tile's disk line says, from the disk the server chose for it.
 *
 * @param host - The tile.
 * @returns For example `/ full in 23 days` or `/data 58% full`.
 */
function hostNote(host: HomeHostTile): string {
  if (host.disk === null) {
    return "no disk metrics";
  }

  return host.filling
    ? `${host.disk.mountpoint} full in ${formatDays(host.disk.daysUntilFull ?? 0)}`
    : `${host.disk.mountpoint} ${formatDiskShare(host.disk.ratio)} full`;
}

/** For example `6 of 8 dry`: the tiles without an open alert, from the server's counts. */
function dryCount(counts: HealthCounts, total: number): string {
  return `${counts.ok + counts.unknown} of ${total} dry`;
}

function tone(health: BlockHealth): string {
  return health === "Warning" ? "w" : health === "Caution" ? "c" : "";
}

function weatherOf(health: BlockHealth): WeatherKind {
  return health === "Warning" ? "storm" : health === "Caution" ? "shower" : "sun";
}
