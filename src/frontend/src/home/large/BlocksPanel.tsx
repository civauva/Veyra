import { Link } from "react-router";

import type { BlockHealth, HomeSnapshot } from "../../api/types";
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

/** One host tile: its name, its worst state, and the disk it shows, with a line about it. */
interface HostTile {
  /** The host name. */
  name: string;
  /** Its worst state from its open alerts. */
  health: BlockHealth;
  /** What its disk line says, such as `/data 58% full` or `/ full in 23 days`. */
  note: string;
  /** How full the disk it shows is, from 0 to 1, or null without disk metrics. */
  diskRatio: number | null;
}

/** How many days ahead a filling disk is named on its host's tile, as in the outlook. */
const outlookDays = 30;

/**
 * The estate: every service seen in the last hour and every host, as tiles (VEY-24, VEY-26). A tile has its weather
 * from its open alerts only, its records and error share of the last five minutes, and a link to its logs or metrics.
 * A host's tile draws the disk that fills soonest, or else its fullest, so the home needs no list of disks of its own.
 * Shown beside the outlook on the standard home, in a column of its own on wide screens, and in a band on the wall.
 *
 * Hosts are those with an open alert and those reporting disks, so a host that is quiet is listed too.
 */
export function BlocksPanel({ snapshot, limit }: BlocksPanelProps) {
  const services = snapshot.blocks.filter((block) => block.kind === "service");
  const shown =
    limit === undefined || services.length <= limit
      ? services
      : [...services].sort((a, b) => rank(a.health) - rank(b.health)).slice(0, limit);
  const hidden = services.length - shown.length;
  // Beside the outlook the tiles are narrow, so the error share moves under the name, which keeps its room.
  const compact = limit !== undefined;
  const hosts = hostTiles(snapshot);
  // Database servers show once one has an open alert (ADR 0018); their card on Metrics lists them all.
  const databases = snapshot.blocks.filter((block) => block.kind === "database");
  const allEnvironments = snapshot.environment === null;

  return (
    <section
      className="glass lg-panel lg-blocks"
      aria-label={databases.length > 0 ? "Services, hosts, and databases" : "Services and hosts"}
    >
      <div className="lg-head">
        <h2 className="cap">Services · last 5 minutes</h2>
        <span className="lg-count">
          {dryCount(
            services.map((block) => block.health),
            services.length,
          )}
        </span>
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
        <h3 className="cap">Hosts</h3>
        <span className="lg-count">
          {dryCount(
            hosts.map((host) => host.health),
            hosts.length,
          )}
        </span>
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
                  <small>{host.note}</small>
                  {host.diskRatio !== null && (
                    <span className="lg-disk-bar lg-tile-disk" data-testid="host-disk" aria-hidden="true">
                      <i style={{ width: `${Math.min(Math.max(host.diskRatio, 0), 1) * 100}%` }} />
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
            <h3 className="cap">Databases</h3>
            <span className="lg-count">
              {dryCount(
                databases.map((block) => block.health),
                databases.length,
              )}
            </span>
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

/** The hosts of the snapshot by name: their worst state from host blocks, and the note of their fullest disk. */
function hostTiles(snapshot: HomeSnapshot): HostTile[] {
  const names = new Set([
    ...snapshot.blocks.filter((block) => block.kind === "host").map((block) => block.name),
    ...snapshot.disks.map((disk) => disk.host),
  ]);

  return [...names]
    .sort((a, b) => a.localeCompare(b))
    .map((name) => {
      const healths = snapshot.blocks
        .filter((block) => block.kind === "host" && block.name === name)
        .map((block) => block.health);
      const health = healths.includes("Warning") ? "Warning" : healths.includes("Caution") ? "Caution" : "Ok";
      const disks = snapshot.disks.filter((disk) => disk.host === name);
      const filling = disks
        .filter((disk) => disk.daysUntilFull !== null && disk.daysUntilFull <= outlookDays)
        .sort((a, b) => (a.daysUntilFull ?? 0) - (b.daysUntilFull ?? 0))[0];
      const fullest = [...disks].sort((a, b) => b.ratio - a.ratio)[0];
      const note = filling
        ? `${filling.mountpoint} full in ${formatDays(filling.daysUntilFull ?? 0)}`
        : fullest
          ? `${fullest.mountpoint} ${formatDiskShare(fullest.ratio)} full`
          : "no disk metrics";
      const shown = filling ?? fullest;

      return { name, health, note, diskRatio: shown ? shown.ratio : null };
    });
}

/** For example `6 of 8 dry`: the tiles without an open alert. */
function dryCount(healths: BlockHealth[], total: number): string {
  return `${healths.filter((health) => health === "Ok" || health === "Unknown").length} of ${total} dry`;
}

/** Warnings first, then cautions, then the rest; the sort keeps their order within each. */
function rank(health: BlockHealth): number {
  return health === "Warning" ? 0 : health === "Caution" ? 1 : 2;
}

function tone(health: BlockHealth): string {
  return health === "Warning" ? "w" : health === "Caution" ? "c" : "";
}

function weatherOf(health: BlockHealth): WeatherKind {
  return health === "Warning" ? "storm" : health === "Caution" ? "shower" : "sun";
}
