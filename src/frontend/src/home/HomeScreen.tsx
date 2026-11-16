import { useState } from "react";

import { useShell } from "../app/shellContext";
import { ForecastVisual } from "./forecast/ForecastVisual";
import { BlocksPanel } from "./large/BlocksPanel";
import { HomeWall } from "./large/HomeWall";
import { NeedsYou } from "./NeedsYou";
import { useHomeLayout } from "./useHomeLayout";
import { formatClock } from "../logs/format";
import { zoneLabel } from "../time/zone";
import "./home.css";
import "./large/large.css";

/**
 * The home screen: the Forecast visual and the Needs you pane (ADR 0007), for the environment chosen in the top bar.
 * The shell owns the snapshot, the environment pills, the master lights, and the weather behind the frame
 * (VEY-23), so they look the same on every screen.
 *
 * Large screens get more to see, not bigger type (VEY-24): from 1800 px the estate of every service and host gets a
 * column of its own; from 2400 by 1200 px the wall adds the last 24 hours, the last 30 days, and the latest alerts.
 * Every question has one place on every layout (VEY-26).
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
          Could not refresh: {error}. Showing the picture from {formatClock(snapshot.at)}{" "}
          {zoneLabel(new Date(snapshot.at))}.
        </p>
      )}
      <div className="home-body" data-layout={layout}>
        {layout === "wall" ? (
          <HomeWall snapshot={snapshot} />
        ) : (
          <ForecastVisual snapshot={snapshot} estate={layout === "standard"} />
        )}
        {layout === "wide" && (
          <div className="home-side">
            <BlocksPanel snapshot={snapshot} />
          </div>
        )}
        <NeedsYou snapshot={snapshot} selectedId={selectedId} onSelect={setSelectedId} now={new Date(snapshot.at)} />
      </div>
    </main>
  );
}
