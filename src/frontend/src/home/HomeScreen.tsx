import { useState } from "react";

import { useShell } from "../app/shellContext";
import { ForecastVisual } from "./forecast/ForecastVisual";
import { BlocksPanel } from "./large/BlocksPanel";
import { DisksPanel } from "./large/DisksPanel";
import { HomeWall } from "./large/HomeWall";
import { NeedsYou } from "./NeedsYou";
import { useHomeLayout } from "./useHomeLayout";
import "./home.css";
import "./large/large.css";

/**
 * The home screen: the Forecast visual and the Needs you pane (ADR 0007), for the environment chosen in the top bar.
 * The shell owns the snapshot, the environment pills, the master lights, and the weather behind the frame
 * (VEY-23), so they look the same on every screen.
 *
 * Large screens get more to see, not bigger type (VEY-24): from 1800 px a column lists every service, host, and disk;
 * from 2400 by 1200 px the wall adds the last 24 hours, the last 30 days, and the latest alerts.
 */
export function HomeScreen() {
  const { status } = useShell();
  const { snapshot, error } = status;
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const layout = useHomeLayout();

  if (!snapshot) {
    return (
      <main className="home">
        {error ? (
          <p className="home-message" role="alert">
            The home could not be loaded: {error}
          </p>
        ) : (
          <p className="home-message">Reading the weather…</p>
        )}
      </main>
    );
  }

  return (
    <main className="home">
      {error && (
        <p className="home-stale" role="alert">
          Could not refresh: {error}. Showing the picture from {new Date(snapshot.at).toISOString().slice(11, 16)} UTC.
        </p>
      )}
      <div className={`home-body ${layout}`}>
        {layout === "wall" ? <HomeWall snapshot={snapshot} /> : <ForecastVisual snapshot={snapshot} />}
        {layout === "wide" && (
          <div className="home-side">
            <BlocksPanel snapshot={snapshot} />
            <DisksPanel snapshot={snapshot} />
          </div>
        )}
        <NeedsYou snapshot={snapshot} selectedId={selectedId} onSelect={setSelectedId} now={new Date(snapshot.at)} />
      </div>
    </main>
  );
}
