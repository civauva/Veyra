import { Link } from "react-router";

import type { BlockHealth, HomeSnapshot } from "../../api/types";
import { formatDays, formatDiskShare } from "../../alerts/ruleWords";
import { defaultFilters, filtersToUrl } from "../../logs/logFilters";
import { formatRatio } from "../homeFormat";
import { WeatherIcon, type WeatherKind } from "../WeatherIcon";

/** Props for {@link BlocksPanel}. */
interface BlocksPanelProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
}

/** One host tile: its name, its worst state, and a line about its fullest disk. */
interface HostTile {
  name: string;
  health: BlockHealth;
  note: string;
}

/** How many days ahead a filling disk is named on its host's tile, as in the outlook. */
const outlookDays = 30;

/**
 * Every service seen in the last hour and every host, as tiles (VEY-24): its weather from its open alerts only, its
 * records and error share of the last five minutes, and a link to its logs or metrics. Shown on wide screens, where
 * the standard home has only the dots at the foot of Needs you.
 *
 * Hosts are those with an open alert and those reporting disks, so a host that is quiet is listed too.
 */
export function BlocksPanel({ snapshot }: BlocksPanelProps) {
  const services = snapshot.blocks.filter((block) => block.kind === "service");
  const hosts = hostTiles(snapshot);
  const allEnvironments = snapshot.environment === null;

  return (
    <section className="glass lg-panel lg-blocks" aria-label="Services and hosts">
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
          {services.map((block) => (
            <li key={block.id}>
              <Link
                className={`lg-tile ${tone(block.health)}`}
                to={`/logs?${filtersToUrl({ ...defaultFilters, services: [block.name] })}`}
              >
                <WeatherIcon kind={weatherOf(block.health)} />
                <span className="lg-tile-what">
                  <b>{block.name}</b>
                  <small>
                    {block.records.toLocaleString("en-US")} records
                    {allEnvironments && block.environment ? ` · ${block.environment}` : ""}
                  </small>
                </span>
                <span className="lg-tile-value">{formatRatio(block.errorRatio)}</span>
              </Link>
            </li>
          ))}
        </ul>
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
                </span>
                <span className="lg-tile-value kind-tag">host</span>
              </Link>
            </li>
          ))}
        </ul>
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

      return { name, health, note };
    });
}

/** For example `6 of 8 dry`: the tiles without an open alert. */
function dryCount(healths: BlockHealth[], total: number): string {
  return `${healths.filter((health) => health === "Ok" || health === "Unknown").length} of ${total} dry`;
}

function tone(health: BlockHealth): string {
  return health === "Warning" ? "w" : health === "Caution" ? "c" : "";
}

function weatherOf(health: BlockHealth): WeatherKind {
  return health === "Warning" ? "storm" : health === "Caution" ? "shower" : "sun";
}
