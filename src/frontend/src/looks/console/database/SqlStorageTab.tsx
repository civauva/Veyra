import { Link } from "react-router";

import { rulePath } from "../../../alerts/rulePath";
import { showPart } from "../../../metrics/database/drill";
import type { ServerOverview } from "../../../api/types";
import { formatBytes } from "../../../home/homeFormat";
import { LineChart } from "../../../metrics/LineChart";
import { formatAmount } from "../../../metrics/metricFormat";
import { HelpTip } from "../../../ui/HelpTip";
import { growthWords, lineOver, SqlFigure, timeWords } from "./sqlParts";

/**
 * A SQL Server's storage (VEY-75, reference screenshots 3 and 8): size and its growth over 24 hours, 7 days, and 30
 * days, the total over 30 days, the volumes, and the largest databases. Files, settings, and checks per database
 * follow below, from the tab every look shares.
 */
export function SqlStorageTab({ overview, block }: { overview: ServerOverview; block: string }) {
  const storage = overview.storage;
  const fullest = [...storage.volumes].sort((a, b) => a.freeRatio - b.freeRatio)[0];
  const monthStart = new Date(Date.parse(overview.to) - 30 * 86_400_000).toISOString();

  return (
    <>
      <section className="sq-figures sq-figures-five" aria-label="Figures">
        <SqlFigure
          label="Database size"
          value={formatAmount(storage.totalBytes)}
          detail={`${overview.inventory.length} databases`}
          line={storage.line}
          help="Data and log files of every database together."
          onOpen={() => showPart("sq-largest-card")}
        />
        <SqlFigure
          label="Growth, 24 h"
          value={growthWords(storage.growth24hBytes, formatBytes)}
          detail="since yesterday"
          help="How much the databases grew in the last 24 hours."
          onOpen={() => showPart("sq-growth-card")}
        />
        <SqlFigure
          label="Growth, 7 days"
          value={growthWords(storage.growth7dBytes, formatBytes)}
          detail={storage.growth7dBytes === null ? undefined : `${formatBytes(storage.growth7dBytes / 7)} a day`}
          help="How much the databases grew in the last 7 days, and what that is a day."
          onOpen={() => showPart("sq-growth-card")}
        />
        <SqlFigure
          label="Growth, 30 days"
          value={growthWords(storage.growth30dBytes, formatBytes)}
          detail="kept 30 days"
          help="How much the databases grew in the last 30 days, as long as sizes are kept."
          onOpen={() => showPart("sq-growth-card")}
        />
        <SqlFigure
          label={fullest ? `Volume ${fullest.name} free` : "Volumes"}
          value={fullest ? `${Math.round(fullest.freeRatio * 100)}%` : "—"}
          detail={
            fullest?.daysUntilFull == null ? "not filling" : `full in ~${timeWords(fullest.daysUntilFull * 86_400)}`
          }
          tone={fullest && fullest.freeRatio < 0.15 ? "bad" : fullest && fullest.freeRatio < 0.25 ? "warn" : undefined}
          help="The fullest volume that holds database files, and when it fills at the pace of the last days."
          onOpen={() => showPart("sq-volumes-card")}
        />
      </section>

      <div className="sq-row sq-row-main">
        <section id="sq-growth-card" className="cn-card" aria-labelledby="sq-growth" tabIndex={-1}>
          <header>
            <h2 id="sq-growth">Database storage growth</h2>
            <HelpTip label="Database storage growth">
              The size of every database together at the end of each of the last 30 days.
            </HelpTip>
            <span className="cn-meta">last 30 days</span>
          </header>
          <div className="sq-chart">
            <LineChart
              title="Database size over 30 days"
              format={(value) => formatBytes(value)}
              height={200}
              series={[
                {
                  line: lineOver("Total size", storage.line, monthStart, overview.to),
                  color: "var(--accent)",
                  filled: true,
                },
              ]}
            />
          </div>
        </section>
        <section id="sq-volumes-card" className="cn-card" aria-labelledby="sq-volumes" tabIndex={-1}>
          <header>
            <h2 id="sq-volumes">Volume capacity</h2>
            <HelpTip label="Volume capacity">
              Each volume that holds database files: its free space, and when it fills at the pace of the last days.
            </HelpTip>
            <span className="cn-meta">{storage.volumes.length} volumes</span>
          </header>
          <ul className="sq-volumes">
            {storage.volumes.map((volume) => (
              <li
                key={volume.name}
                className={volume.freeRatio < 0.15 ? "bad" : volume.freeRatio < 0.25 ? "warn" : undefined}
              >
                {volume.freeRatio < 0.25 ? (
                  // A volume running out leads to what it means and how to fix it (VEY-83).
                  <Link
                    className="sq-volume-name"
                    to={rulePath("db-volume-space", { kind: "database", name: block, part: volume.name }) + "#fix"}
                  >
                    {volume.name}
                  </Link>
                ) : (
                  <span className="sq-volume-name">{volume.name}</span>
                )}
                <span className="sq-volume-free">{Math.round(volume.freeRatio * 100)}% free</span>
                <span className="sq-volume-bar" aria-hidden="true">
                  <i style={{ width: `${Math.round((1 - volume.freeRatio) * 100)}%` }} />
                </span>
                <span className="sq-muted sq-small">
                  {formatBytes(volume.freeBytes)} free of {formatBytes(volume.sizeBytes)}
                  {volume.daysUntilFull !== null && ` · full in ~${timeWords(volume.daysUntilFull * 86_400)}`}
                </span>
              </li>
            ))}
          </ul>
        </section>
      </div>

      <section id="sq-largest-card" className="cn-card" aria-labelledby="sq-largest" tabIndex={-1}>
        <header>
          <h2 id="sq-largest">Largest databases</h2>
          <HelpTip label="Largest databases">
            The largest databases with their growth over 24 hours and 7 days. Each database&apos;s files, settings, and
            checks follow below.
          </HelpTip>
        </header>
        <div className="cn-table-scroll">
          <table className="cn-table">
            <thead>
              <tr>
                <th scope="col">Database</th>
                <th scope="col" className="num">
                  Size
                </th>
                <th scope="col" className="num">
                  Growth 24 h
                </th>
                <th scope="col" className="num">
                  Growth 7 d
                </th>
              </tr>
            </thead>
            <tbody>
              {storage.largest.map((one) => (
                <tr key={one.database}>
                  <td>{one.database}</td>
                  <td className="num">{formatBytes(one.bytes)}</td>
                  <td className="num">{growthWords(one.growth24hBytes, formatBytes)}</td>
                  <td className="num">{growthWords(one.growth7dBytes, formatBytes)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}
