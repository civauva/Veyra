# 0013. Left sidebar navigation

Date: 2026-11-11
Status: Accepted

## Context

Since VEY-23 every screen has shared one top bar: the logo, the environment pills, the navigation, master warning and
master caution, the clock, the theme, the avatar, and sign-out, as ADR 0007 asked of the shell. In review (VEY-26)
that bar had four problems:

- **It mixed two kinds of choice.** The environment pills and the screen tabs sat side by side, although one says
  *what* every screen shows and the other *which* screen. An installation with many environments would push the
  tabs off the bar.
- **The right end was crowded.** The lights, the clock, the theme, the avatar, and sign-out competed for one corner.
- **The logo did not lead home**, where people look first for a way back.
- **Height is the scarce dimension.** Every log list, chart, and dashboard wants rows; wide screens have width to
  spare. The bar took 56 px from every screen, and the home scrolled on 1440 × 900 laptops.

The roadmap adds traces, incidents, and dashboards, so the number of screens will grow.

Six navigation mockups (A to F) and a combined sidebar (G1 to G5) were reviewed on the work item, and then nine ways
to mark the current screen (H, H2).

## Decision

The main navigation is a **left sidebar** on every screen after sign-in, and there is no top bar on screens wider
than 1023 px.

1. **Contents, top to bottom.**
   - The logo and the name, leading home.
   - The **environment switcher**: the chosen environment with its weather and error rate. It opens a list with a
     search field; "All environments" carries the worst weather of any of them. Everything below it shows that
     environment.
   - The screens: Home, Logs, Metrics, Alerts. **Alerts carries the counts** of open warnings (red) and cautions
     (amber), which replace the master lights.
   - **Settings**, for administrators only (ADR 0008): ingestion keys, users, notifications.
   - **The person**: initials, name, and role, opening a menu with their account, the theme, and signing out.
2. **The current screen is marked in its own colour** (highlight 6 of the review): Home periwinkle, Logs cyan,
   Metrics green, Alerts violet. The colours keep clear of the warning red and the caution amber, so an open Alerts
   screen is never read as an alert. The light theme uses deeper shades of the same colours.
3. **Three forms by width.**
   - From 1600 px: labelled, 232 px wide.
   - From 1024 px: icons only, 64 px wide, with the names as labels on hover and focus.
   - Below 1024 px: a slim top bar with the menu button, the logo, a compact environment switcher, and the alerts;
     the sidebar opens from the menu button as a drawer.

   Above phone width, the person can collapse or expand the sidebar; the choice is kept in that browser.
4. **The weather lies behind the sidebar**, as it lay behind the bar, so the scene stays one piece. Menus and the
   drawer sit on a solid panel so they stay readable in every scene.
5. **The clock goes.** The home's caption gives the time of its snapshot; other screens state their own time ranges.
6. **The home choice**, when City arrives (ADR 0007, point 9), is a personal preference and joins the account menu.

This replaces "the top bar" in point 6 of ADR 0007. The rest of ADR 0007 stands: the shell still owns what must look
the same on every home, now in the sidebar.

## Alternatives considered

- **Keep the top bar, with the environment as a scope switcher before the tabs** (mockup A). It fixes the mixing and
  the crowding, but keeps the bar's height and runs out of room as screens are added.
- **Two rows: environments above, screens below** (C). Clear, but costs 96 px of height on every screen.
- **Environments as cards on the home** (D). The choice would be missing on every other screen.
- **Search first, with a command palette** (E). Good as an addition later; as the only way around it hides the
  screens.
- **An environment dock beside a labelled sidebar** (F). Best for installations with dozens of environments, at the
  cost of 76 px more width. The switcher's search field covers many environments well enough until that is needed.

## Consequences

- Screens get the full height of the window. The home fits without scrolling on 1440 × 900 laptops as well as on
  1080p, 1440p, and 4K screens.
- New screens add a line to the sidebar instead of competing for room in a bar.
- On large screens the labelled sidebar takes 232 px of width; the wide home layout gives its Needs you column a little
  less room in exchange. People who want the width collapse it to icons.
- The master lights are gone as a separate element. The counts beside Alerts and the Needs you pane carry the same
  information, and the sidebar is on every screen, as the lights were.
- The frontend commits to the sidebar's forms and breakpoints; the end-to-end tests check them at desktop, tablet, and
  phone sizes.
