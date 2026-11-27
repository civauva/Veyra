# 0021. Looks: one app, several ways to show it

Date: 2026-11-26
Status: Accepted

## Context

VEY-69 asks for three looks of the whole app. Each person picks one in Settings:

- **Console** becomes the default. It is a dense operations console.
- **Forecast** is today's weather look.
- **City** is the city of buildings from ADR 0007.

A look is more than colours. Console groups the menu into Observe, Resources, Respond, and Manage. It gives
Services, Hosts & containers, and Databases their own entries, which Forecast keeps together under Metrics. Its pages
are laid out differently: a strip of figures, panels side by side, and dense tables.

Today's code was written for one look:

- `App.tsx` lists the routes, and `Sidebar.tsx` lists the menu again by hand.
- `Shell.tsx` is Forecast's shell. It imports the weather and paints it behind every screen.
- Feature screens import shared pieces such as `HelpTip`, `ScreenBar`, and `ViewToggle` from `app/`, the same folder
  as the shell. Nothing tells a shared piece apart from a part of one look.

If a second look is added by asking "which look is this?" inside components, every screen gains branches. The looks
also drift apart: a screen shows in one menu and is missing from another, and a figure is worked out twice in two
ways. Veyra already decided against that last risk: every figure comes from the server (VEY-66 to VEY-68).

## Decision

A **look** arranges the app. It never owns data or behaviour. Looks are kept apart by layers whose import direction
a lint check enforces, by one registry of routes, and by a small typed contract.

1. **What a look is.** A look supplies:
   - its shell: the menu, the bars, and the frame around the screens
   - its menu, as groups of route ids
   - its home
   - optionally, its own layout for some screens
   - its stylesheet

   A look fetches nothing and works nothing out. It composes the hooks and pieces of the features. Every list,
   count, ranking, and filter still comes from the server.

2. **Layers.** Imports point one way only:

   | Layer | Folder | May import |
   | --- | --- | --- |
   | Utilities | `src/time/` | utilities; every layer may use them |
   | API | `src/api/` | API, utilities |
   | UI pieces | `src/ui/` | UI, API types, the shell contract |
   | Features | `src/<feature>/` such as `logs/`, `metrics/` | features, UI, API, the shell contract |
   | Looks | `src/looks/<look>/` | its own look, features, UI, API types, `app/` |
   | App | `src/app/`, `src/main.tsx` | everything; it is the composition root |

   The rules that matter most:
   - A feature never imports a look, nor anything in `app/` except the shell contract.
   - A look never imports another look.
   - A look never calls the API client.

   `scripts/check-boundaries.mjs` checks every import as part of `npm run lint`. Tests are exempt, because they put
   the whole app together.

3. **One route registry.**
   - `app/routes.tsx` lists every destination once: its id, path, title, icon, the role it needs, and its screen.
   - URLs are the same in every look, so a shared link opens in whatever look the reader uses.
   - A look's `nav.ts` only groups route ids. It may give an entry its own label, such as Console's "Overview" for
     the home, but it cannot add a route.
   - A test checks every look: each menu route appears exactly once in its menu, and every route resolves to a
     screen. A new screen cannot be missing from one look.

4. **The look contract** (`app/look.ts`):

   ```ts
   interface Look {
     id: LookId;
     name: string;
     Shell: ComponentType<{ session: Session }>;
     nav: NavGroup[];
     screens: { home: ComponentType } & Partial<Record<RouteId, ComponentType>>;
   }
   ```

   - The home belongs to the look.
   - Any other screen the look does not list uses the registry's screen.
   - Nothing outside `app/` and `looks/` can ask which look is active, because the boundary check stops them from
     importing it.

5. **The shell contract** (`app/shellContext.ts`) is what every look's shell must give the screen inside it:
   - the chosen environment and its home snapshot
   - the condition the screen is about (today called its scene), which each look may show in its own way or not at all
   - the element a screen's `ScreenBar` fills

   Behaviour every shell needs lives once in `app/`: the snapshot, the times and zone, the theme, and the rule
   catalog.

6. **Styling.**
   - Shared pieces use only the tokens in `styles/tokens.css`.
   - A look's stylesheet styles its own classes and may set tokens for itself.
   - The CSS owner check treats each look folder as its own owner, so two looks can never style the same class.

7. **Choosing a look.** Each person picks a look, and Admins set the instance default and whether people may choose.
   Both are settings under ADR 0017. Each look loads on its own, so people download only the look they use.

8. **Features stay where they are.** The feature folders are not moved under a new parent, which keeps their history
   and keeps each change small. A feature screen that a look lays out again first moves its data into a hook, with its
   tests, in the same change.

9. **Relation to earlier decisions.**
   - ADR 0007 still binds every look: the block and group model, health from alerting only, and the decent-citizen
     rules. Point 6, the split between shell and dashboard, now reads: the look owns the shell. The Needs you list
     and the alert words stay the same in every look.
   - ADR 0013 now describes the Forecast look's shell.

## Alternatives considered

- **Only a theme, in CSS.** This is cheap, but it cannot change the menu's structure, split Metrics into its own
  entries, or lay pages out differently, which is what VEY-69 asks for.
- **A branch on the look inside components.** It is quick for the first screen, but every screen collects branches.
  Nothing would stop a screen from vanishing from one look's menu, or a figure from being worked out in one look only.
- **Separate apps per look.** Each look would be free, but the data code would be copied three times and the numbers
  would drift.
- **A layout engine with layouts described in JSON.** It is flexible, but it is a framework of its own that is harder
  to read, test, and make accessible than plain components. Three looks do not need it.

## Consequences

- A new look is a new folder under `src/looks/` with a shell, a menu, a home, and a stylesheet. Features are not
  edited to add it.
- A new screen is one entry in the registry. The completeness test then asks each look to place it in its menu.
- A screen whose layout differs per look keeps its data in one hook, and each look draws that hook its own way.
- The boundary check is code we maintain, and its rules change only with this ADR.
- End-to-end tests run every viewport for the default look, plus a smaller set for the other looks.
