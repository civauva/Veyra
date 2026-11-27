import { type KeyboardEvent, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useNavigate } from "react-router";

import { api } from "../../api/client";
import type { BlockHealth, DatabaseCard, HomeBlock, MetricRange } from "../../api/types";
import { engineName } from "../../alerts/ruleWords";
import { useShell } from "../../app/shellContext";
import { rangeSearch } from "../metricRange";
import { useMetricData } from "../useMetricData";
import { connectionsRatio, connectionsTone, wraparoundTone } from "./postgres";
import { tabsOf } from "./tabs";
import "./switcher.css";

/** How often the list of servers is read again; servers come and go rarely. */
const serversRefreshMilliseconds = 60_000;

/** How far the menu keeps from the window's edges, in pixels. */
const margin = 12;

/** Props for {@link ServerSwitcher}. */
export interface ServerSwitcherProps {
  /** The engine of the server shown. */
  engine: string;
  /** The server shown. */
  server: string;
  /** Its health from its own open alerts, or null when they are not known. */
  health: BlockHealth | null;
  /** The tab shown, by its name in the address, which another server keeps when it has it. */
  tab: string;
  /** The range shown, which another server keeps. */
  range: MetricRange;
  /** Whether the Advanced view is on, which decides the tabs another server has. */
  advanced: boolean;
  /**
   * The shown server's own card, from its screen's data, so the menu lists it even when the overview of the range
   * does not, or has not answered yet; null until the screen has loaded.
   */
  card?: DatabaseCard | null;
}

/**
 * The server's name in the screen's trail, as a switcher to every other database server (VEY-62): a button with the
 * server's health and name that opens a menu of the servers on the Metrics overview, each with its health, engine,
 * and environment. Choosing one opens its screen on the same tab when it has that tab. It works with the keyboard as a
 * menu button: Enter, Space, or the arrows open it; the arrows, Home, and End move; Escape closes it.
 *
 * The menu is drawn on the page itself, under the button and inside the window, since the trail clips what overflows.
 *
 * @param props - The server shown, its health, and the tab, range, and view to keep.
 * @returns The button and, while open, its menu.
 */
export function ServerSwitcher({ engine, server, health, tab, range, advanced, card = null }: ServerSwitcherProps) {
  const [open, setOpen] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const menuId = useId();
  const navigate = useNavigate();
  const blocks = useShell().status.snapshot?.blocks;
  const { data } = useMetricData(
    `database-servers-${range}`,
    (signal) => api.metricsOverview(range, { search: "", environment: null }, signal),
    {
      refreshMs: serversRefreshMilliseconds,
    },
  );

  const listed = data?.databases ?? [];
  // The server sends them by name (VEY-68); the shown server, when the overview leaves it out, leads the menu.
  const servers = [...(card && !listed.some((one) => isShown(one, engine, server)) ? [card] : []), ...listed];
  const current = health ?? healthOf(servers.find((card) => isShown(card, engine, server)) ?? null, blocks);

  function close(refocus: boolean) {
    setOpen(false);

    if (refocus) {
      button.current?.focus();
    }
  }

  function choose(card: DatabaseCard) {
    const params = new URLSearchParams(rangeSearch(range));
    const tabs = tabsOf(card.engine, advanced);

    // Another server keeps the tab when it has it; its first tab is its default, which the address leaves out.
    if (tabs.some((one) => one.id === tab) && tabs[0]?.id !== tab) {
      params.set("tab", tab);
    }

    close(true);
    navigate(`/metrics/databases/${encodeURIComponent(card.engine)}/${encodeURIComponent(card.server)}?${params}`);
  }

  // The menu goes under the button and inside the window, and follows it while the page scrolls or resizes.
  useLayoutEffect(() => {
    if (!open) {
      return undefined;
    }

    const place = () => {
      const anchor = button.current?.getBoundingClientRect();
      const element = menu.current;

      if (!anchor || !element) {
        return;
      }

      const width = element.getBoundingClientRect().width;
      const left = Math.max(margin, Math.min(anchor.left, document.documentElement.clientWidth - margin - width));

      element.style.top = `${anchor.bottom + 6}px`;
      element.style.left = `${left}px`;
    };

    place();
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, { capture: true, passive: true });

    return () => {
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, { capture: true });
    };
  }, [open]);

  // Opening puts the focus on the server shown, or on the first while the list is still read.
  useEffect(() => {
    if (!open) {
      return;
    }

    const items = menuItems(menu.current);
    (items.find((item) => item.getAttribute("aria-current") === "true") ?? items[0])?.focus();
  }, [open, servers.length]);

  // A press outside the button and the menu closes it; so does Escape, which gives the focus back to the button.
  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const pressed = (event: PointerEvent) => {
      const target = event.target as Node;

      if (!button.current?.contains(target) && !menu.current?.contains(target)) {
        setOpen(false);
      }
    };
    const keyed = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") {
        event.stopPropagation();
        setOpen(false);
        button.current?.focus();
      }
    };

    document.addEventListener("pointerdown", pressed);
    document.addEventListener("keydown", keyed, true);

    return () => {
      document.removeEventListener("pointerdown", pressed);
      document.removeEventListener("keydown", keyed, true);
    };
  }, [open]);

  function opened(event: KeyboardEvent<HTMLButtonElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      setOpen(true);
    }
  }

  function moved(event: KeyboardEvent<HTMLDivElement>) {
    const items = menuItems(menu.current);
    const at = items.indexOf(document.activeElement as HTMLElement);
    const next: Record<string, number> = {
      ArrowDown: at < 0 ? 0 : (at + 1) % items.length,
      ArrowUp: at < 0 ? items.length - 1 : (at + items.length - 1) % items.length,
      Home: 0,
      End: items.length - 1,
    };

    if (event.key === "Tab") {
      setOpen(false);

      return;
    }

    if (event.key in next) {
      event.preventDefault();
      items[next[event.key] ?? 0]?.focus();
    }
  }

  const weather = weatherOf(current);

  return (
    <>
      <button
        ref={button}
        type="button"
        className="db-switch"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((now) => !now)}
        onKeyDown={opened}
      >
        <i className={`db-switch-dot ${weather.tone}`} aria-hidden="true" />
        <span className="db-switch-name">{server}</span>
        <span className="visually-hidden">, {weather.word}. Choose another server</span>
        <span className="db-switch-caret" aria-hidden="true">
          ▾
        </span>
      </button>
      {open &&
        createPortal(
          <div
            ref={menu}
            id={menuId}
            className="db-switch-menu"
            role="menu"
            aria-label="Database servers"
            onKeyDown={moved}
          >
            <div className="db-switch-head" aria-hidden="true">
              Database servers{data ? ` · ${servers.length}` : ""}
            </div>
            {data === null && <p className="db-switch-empty">Reading the servers…</p>}
            {servers.map((card) => {
              const shown = isShown(card, engine, server);
              const tone = weatherOf(shown ? current : healthOf(card, blocks));

              return (
                <button
                  key={`${card.engine} ${card.server}`}
                  type="button"
                  role="menuitem"
                  tabIndex={-1}
                  className="db-switch-item"
                  aria-current={shown ? "true" : undefined}
                  onClick={() => (shown ? close(true) : choose(card))}
                >
                  <i className={`db-switch-dot ${tone.tone}`} aria-hidden="true" />
                  <b>{card.server}</b>
                  <small>{[engineName(card.engine), card.environment].filter(Boolean).join(" · ")}</small>
                  <span className="visually-hidden">, {tone.word}</span>
                </button>
              );
            })}
          </div>,
          document.body,
        )}
    </>
  );
}

/** Whether a card is the server shown. */
function isShown(card: DatabaseCard, engine: string, server: string): boolean {
  return card.engine === engine && card.server === server;
}

/** The items of the menu, in order. */
function menuItems(menu: HTMLElement | null): HTMLElement[] {
  return Array.from(menu?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
}

/**
 * A server's health: from its open alerts when the shell's snapshot has its block, as the home does (ADR 0007);
 * otherwise from its card, amber where its card on the Metrics overview turns amber.
 *
 * @param card - The server's card, or null when it is not listed.
 * @param blocks - The blocks of the shell's snapshot, when it has arrived.
 * @returns Its health.
 */
function healthOf(card: DatabaseCard | null, blocks: HomeBlock[] | undefined): BlockHealth {
  if (card === null) {
    return "Unknown";
  }

  const block = blocks?.find((one) => one.kind === "database" && one.name === `${card.engine} ${card.server}`);

  if (block) {
    return block.health;
  }

  const hot =
    (card.blocked ?? 0) > 0 ||
    (card.longestQuerySeconds ?? 0) >= 300 ||
    (card.oldestBackupSeconds ?? 0) >= 26 * 3600 ||
    card.failedJobs > 0 ||
    connectionsTone(connectionsRatio(card.connections, card.maxConnections)) !== "ok" ||
    wraparoundTone(card.wraparoundRatio) !== "ok";

  return hot ? "Caution" : "Ok";
}

/** A health's colour and word. */
function weatherOf(health: BlockHealth): { tone: string; word: string } {
  switch (health) {
    case "Warning":
      return { tone: "warning", word: "storm" };
    case "Caution":
      return { tone: "caution", word: "showers" };
    case "Ok":
      return { tone: "ok", word: "fine" };
    case "Unknown":
      return { tone: "unknown", word: "health not known" };
  }
}
