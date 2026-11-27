import type { ComponentType } from "react";

import type { LookId } from "../api/looks";
import type { Role, Session } from "../api/types";
import { routeById, type AppRoute, type RouteId } from "./routes";

/** One entry of a look's menu: a route, optionally under the look's own name for it. */
export interface NavEntry {
  /** The route it leads to. */
  route: RouteId;
  /** The look's name for it, such as "Overview" for the home; the route's title when left out. */
  label?: string;
}

/** A group of a look's menu, such as Observe or Admin. */
export interface NavGroup {
  /** Its heading, or null for a group without one. */
  title: string | null;
  /** Its entries, in order. */
  entries: NavEntry[];
}

/**
 * A look: one way to show the whole app (ADR 0021). It arranges what the features offer and never fetches or works
 * out data itself. Screens a look does not list in `screens` are the registry's own.
 */
export interface Look {
  /** Its stable name. */
  id: LookId;
  /** Its name on screen. */
  name: string;
  /** The frame around every screen: menu, bars, and whatever it paints. It gives each screen the shell context. */
  Shell: ComponentType<{ session: Session }>;
  /** Its menu, as groups of routes. */
  nav: NavGroup[];
  /** Its home, and its own layout of any other screen. */
  screens: { home: ComponentType } & Partial<Record<RouteId, ComponentType>>;
}

/** A menu entry ready to draw. */
export interface NavItem {
  /** The route it leads to. */
  route: AppRoute;
  /** What it says. */
  label: string;
}

/** A menu group ready to draw. */
export interface NavItemGroup {
  /** Its heading, or null for a group without one. */
  title: string | null;
  /** Its entries the person may open. */
  items: NavItem[];
}

/**
 * Whether a role may open a route.
 *
 * @param route - The route.
 * @param role - The signed-in person's role.
 * @returns False for an Admin route and anyone but an Admin.
 */
export function mayOpen(route: AppRoute, role: Role): boolean {
  return !route.admin || role === "Admin";
}

/**
 * A look's menu for a role: the routes the role may open, under the look's labels, without groups left empty.
 *
 * @param look - The look, of which only its menu is read.
 * @param role - The signed-in person's role.
 * @returns The groups to draw.
 */
export function navFor(look: Pick<Look, "nav">, role: Role): NavItemGroup[] {
  return look.nav
    .map((group) => ({
      title: group.title,
      items: group.entries
        .map((entry) => ({ route: routeById(entry.route), label: entry.label ?? routeById(entry.route).title }))
        .filter((item) => mayOpen(item.route, role)),
    }))
    .filter((group) => group.items.length > 0);
}

/**
 * The screen a look shows for a route: its own when it has one, otherwise the registry's.
 *
 * @param look - The look.
 * @param route - The route.
 * @returns The screen.
 */
export function screenOf(look: Look, route: AppRoute): ComponentType {
  const screen = look.screens[route.id] ?? route.screen;

  if (!screen) {
    throw new Error(`The ${look.id} look has no screen for ${route.id}.`);
  }

  return screen;
}
