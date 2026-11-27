import { useState } from "react";

import { presetNames, timePresets, wallOf, instantOfWall, windowOf, type TimeChoice } from "../../app/time";
import type { ShellTimeControls } from "../../app/useShellTime";
import { refreshChoices } from "../../app/useShellTime";
import { cityOf } from "../../time/zone";
import { ConsoleIcon } from "./ConsoleIcon";
import { ConsoleMenu } from "./ConsoleMenu";

/** Props for {@link ConsoleTime} and {@link ConsoleRefresh}. */
interface TimeProps {
  /** The shared time and the ways to change it. */
  time: ShellTimeControls;
  /** The zone times are shown in. */
  zone: string;
}

/** A custom range in short words, such as "25 Nov 09:00 – 26 Nov 09:00". */
function customWords(choice: TimeChoice, zone: string): string {
  if (choice.from === null || choice.to === null) {
    return presetNames["24h"];
  }

  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  const short = (instant: string) => {
    // A wall time reads YYYY-MM-DDTHH:mm, so the pieces are at fixed places.
    const wall = wallOf(instant, zone);

    return `${Number(wall.slice(8, 10))} ${months[Number(wall.slice(5, 7)) - 1] ?? ""} ${wall.slice(11, 16)}`;
  };

  return `${short(choice.from)} – ${short(choice.to)}`;
}

/**
 * The time range of every screen, at the top bar (ADR 0022): the presets ending now and a range of one's own, typed in
 * the zone the person sees times in. The pick goes into the address, so a link carries it.
 */
export function ConsoleTime({ time, zone }: TimeProps) {
  const { choice, setChoice } = time;
  // The custom fields start from the range on screen, read once when the bar is drawn.
  const [from, setFrom] = useState(() => wallOf(windowOf(choice, Date.now()).from, zone));
  const [to, setTo] = useState(() => wallOf(windowOf(choice, Date.now()).to, zone));
  const start = instantOfWall(from, zone);
  const end = instantOfWall(to, zone);
  const valid = start !== null && end !== null && Date.parse(start) < Date.parse(end);
  const words = choice.preset !== null ? presetNames[choice.preset] : customWords(choice, zone);

  return (
    <ConsoleMenu
      button={
        <>
          <ConsoleIcon name="calendar" size={14} />
          <span className="visually-hidden">Time range:</span> <span className="cn-control-words">{words}</span>
          <span className="cn-control-short" aria-hidden="true">
            {choice.preset ?? "Custom"}
          </span>
          <ConsoleIcon name="chevron" size={14} />
        </>
      }
      hotkey="t"
      menuLabel="Time range"
      menuClass="cn-menu-time"
    >
      {(close) => (
        <>
          <p className="cn-menu-cap">Time range · every screen</p>
          {timePresets.map((preset) => (
            <button
              key={preset}
              type="button"
              className="cn-menu-option"
              aria-pressed={choice.preset === preset}
              onClick={() => {
                setChoice({ preset, from: null, to: null });
                close();
              }}
            >
              {presetNames[preset]}
            </button>
          ))}
          <p className="cn-menu-cap">Custom</p>
          <div className="cn-menu-pair">
            <label>
              From
              <input type="datetime-local" value={from} onChange={(event) => setFrom(event.target.value)} />
            </label>
            <label>
              To
              <input type="datetime-local" value={to} onChange={(event) => setTo(event.target.value)} />
            </label>
          </div>
          <button
            type="button"
            className="cn-button"
            disabled={!valid}
            onClick={() => {
              if (start !== null && end !== null) {
                setChoice({ preset: null, from: start, to: end });
                close();
              }
            }}
          >
            Show this range
          </button>
          <p className="cn-menu-note">Times in {zone === "UTC" ? "UTC" : cityOf(zone)}</p>
        </>
      )}
    </ConsoleMenu>
  );
}

/** Each refresh interval in words. */
function refreshWords(every: number | null): string {
  if (every === null) {
    return "Off";
  }

  return every < 60_000 ? `${every / 1000} s` : every === 60_000 ? "1 min" : `${every / 60_000} min`;
}

/**
 * How often every screen asks the server again (ADR 0022), kept in this browser. While Live is on, screens follow the
 * stream instead, and the button says so.
 */
export function ConsoleRefresh({ time }: { time: ShellTimeControls }) {
  return (
    <ConsoleMenu
      button={
        <>
          <ConsoleIcon name="refresh" size={14} />
          <span className="visually-hidden">Refresh:</span>{" "}
          <span className="cn-control-words">{time.live ? "Live" : refreshWords(time.refreshChoice)}</span>
          <ConsoleIcon name="chevron" size={14} />
        </>
      }
      wrapClass="cn-refresh"
      menuLabel="Refresh"
      menuClass="cn-menu-refresh"
    >
      {(close) => (
        <>
          <p className="cn-menu-cap">Refresh</p>
          {refreshChoices.map((every) => (
            <button
              key={String(every)}
              type="button"
              className="cn-menu-option"
              aria-pressed={time.refreshChoice === every}
              onClick={() => {
                time.setRefresh(every);
                close();
              }}
            >
              {every === null ? "Off" : `Every ${refreshWords(every)}`}
            </button>
          ))}
          <p className="cn-menu-note">Live replaces it with a stream.</p>
        </>
      )}
    </ConsoleMenu>
  );
}
