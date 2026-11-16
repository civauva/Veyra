# 0017. Settings in the admin UI

Date: 2026-11-13
Status: Accepted

## Context

Every setting of Veyra lives in environment variables and `appsettings.json`: 56 settings in eleven options classes,
from retention and size budgets to alert rule thresholds, the email server, sign-in lockout, and queue lengths. To
change one, an operator edits `deploy/.env` and restarts the container. The only setting kept in the database is who
must use a second factor (`access.setting`, ADR 0011).

v0.3 adds containers, databases, Windows hosts, and IIS, each with rules and settings of its own. People running Veyra
want to change all of them on a screen, see what they changed, and trust that the change took effect.

What the code does today:

- Each module binds its section (`Veyra:Storage`, `Veyra:Alerting`, …) with `AddOptions<T>().Bind(…)`, checks it with
  inline rules, and validates on start. Every consumer reads `IOptions<T>`, fixed at startup.
- Most consumers read the value on every call, so they can follow a change cheaply. A few capture it once: the rule
  and dispatch timers build their interval once, the ingestion queues size their channels once, and the session
  cookie's lifetime is set at startup.
- Some values must be known before the database can be read: the connection string, the listen addresses, and where
  the Data Protection keys are kept.
- ADR 0009 keeps secrets out of the database, so the SMTP password only comes from the environment. Webhook URLs are
  stored encrypted with ASP.NET Data Protection, whose keys sit in the `veyra-keys` volume, apart from the database.

## Decision

Every setting a module has is **kept in the database and changed on a Settings screen** by Admins. Changes **apply
without a restart**. The environment and `appsettings.json` give the **first values**; a value saved in Settings wins.
Only what is needed before the database is reachable stays outside.

### Where a value comes from

- Code default, then `appsettings.json`, then environment variables, then the database. The last one that sets a
  value wins.
- Settings shows each value with its default and where that default comes from, for example "Default 14, from
  `VEYRA_LOG_RETENTION_DAYS`". **Reset** removes the saved value, so the default applies again.
- `deploy/.env` keeps its variables as first values. An installation that set `VEYRA_LOG_RETENTION_DAYS=30` keeps 30
  days after the upgrade, and can change it on the screen from then on.

### What stays outside

- The connection string, the listen addresses, and the Data Protection key location. Settings shows them read-only
  in a **System** section, the connection string without its password, with the variable that sets each one.
- `Access:RemoveSecondFactor` stays an environment variable. It is a recovery switch for an Admin who lost both the
  authenticator and the recovery codes, so it must work when nobody can sign in.

### How modules read settings

- The Access module stores the values in `access.setting`, extended with the value as JSON, who changed it, and when.
  It already holds the second-factor setting and is where administration lives. Modules do not reference Access; they
  read their own configuration section as before.
- Access provides a **configuration source** that the Api host adds last. It reads the saved values into
  `IConfiguration` under the same keys, for example `Veyra:Storage:LogRetentionDays`, so every module keeps its
  binding and checks.
- The source reloads right after a save and every 15 seconds, so a second Veyra instance follows within that time.
- Modules read **`IOptionsMonitor<T>`** instead of `IOptions<T>`. The values captured once are reworked:
  - Timers read their interval again on every tick.
  - The ingestion queues check their length when an item is added, instead of fixing the channel's capacity.
  - The session lifetime is set on every sign-in, instead of once on the cookie options.
- Hard-coded values that people may need to change become settings when they are touched. The sign-in rate limit (10
  a minute) and the maintenance intervals stay constants for now.

### Describing and checking settings

- Each module describes its settings in code with a `SettingDefinition` from `Veyra.Core`, through
  `ISettingDefinitions`. A definition holds:
  - the key, the section, a label, and help in plain words;
  - the type and unit, the limits, and the default;
  - whether the value is a secret;
  - what changing it does, for example "deletes data" or "may lock people out".
- Each module's checks become `IValidateOptions<T>` classes, so the Settings API can check a whole candidate
  configuration, rules that compare two values included (caution ≤ warning), before anything is saved. These are the
  same checks as at startup.
- A test fails when a bound options property has no definition, so no setting can stay environment-only by accident.
  New modules and integrations, starting with VEY-32 to VEY-35, ship their settings this way.

### The API

All endpoints are for Admins, under `/api/v1/settings`. Writes need the antiforgery header, like every other write.

| Request | Does |
| --- | --- |
| `GET /settings` | Every setting: value, default and its source, limits, unit, whether changed, and a version |
| `POST /settings/review` | Checks a batch of changes and says what each will do, such as the logs it deletes |
| `PUT /settings` | Saves a batch in one transaction, all or nothing, if the version still matches; otherwise `409` |
| `GET /settings/history` | Changes, newest first, filtered by section, person, and time |
| `POST /settings/history/{id}/revert` | Saves the old value again, as a new change |
| `GET /settings/export`, `POST /settings/import` | Saved values as JSON, without secrets; import goes through review |

### Secrets

- The SMTP password, and any later secret setting, is stored encrypted with Data Protection under its own purpose,
  as webhook URLs are. It is never sent back: the screen shows only that it is set, and the history records that it
  changed, not what to.
- This supersedes the part of ADR 0009 that keeps the SMTP password out of the database. A database backup alone does
  not reveal it, because the keys live in a separate volume.

### History

- Every save records, per setting, the old and new value, who saved it, and when, in `access.setting_change`. Changes
  are kept 365 days.
- Revert and import are changes like any other.

### The Settings screen

The design is in the VEY-31 mockups.

- **Where.** `/settings`, behind a gear in the sidebar's admin group, for Admins only. It has the sections Data and
  retention, Alert rules, Notifications and email, Sign-in and security, Ingestion, Advanced, and System.
- **Finding.** A search box finds any setting by its label, help, or key.
- **Each setting** shows its default, a mark when changed with Reset, inline checks, and help that says what it does.
  Alert thresholds sit on a scale with their caution and warning levels.
- **Saving.** Changes collect in a bar at the bottom. **Review and save** shows each change with what it will do.
  Changes that delete data or may lock people out need an explicit confirmation.
- **History.** A side panel lists every change, with Revert.
- **Elsewhere.** Other screens that show settings link to them: the email server on the Notifications screen, the
  rules on the Alerts screen, and the storage budget on the home.
- **Every size.** It works on desktop, tablet, and phone. On a phone, the sections become a list that opens each one.

## Alternatives considered

- **Keep the environment only.** No new code, but every change needs a file edit and a restart, and nobody can see
  what was changed or by whom.
- **The environment wins and locks the setting.** It suits installations run as code, but Compose always passes its
  variables, so nearly every setting would be locked. Installations that want this can pin settings later with an
  explicit list.
- **Write the changes to `appsettings.json`.** Restart-free reloads exist, but the file sits inside the container,
  is lost on upgrade, and is not shared between instances.
- **A separate Settings module.** A clean owner, but a seventh module for one table and one controller. Access already
  owns administration and a setting table.
- **A setting table per module.** Each module would own its rows, but the screen, the history, and the checks would
  have to join six tables, and a batch that spans modules could not be saved in one transaction.
- **Secrets only from the environment.** Safer in principle, but the email server would then be half on the screen
  and half in a file. Encryption with keys kept apart from the database is what webhook URLs already rely on.

## Consequences

- One Access migration extends `access.setting` and adds `access.setting_change`.
- Every module switches to `IOptionsMonitor<T>` and moves its checks into validator classes. The timers, the
  ingestion queues, and the session lifetime change how they read their settings.
- A wrong value can now be saved from the screen. The checks, the review step, and revert keep that safe, and the
  checks run again at startup.
- The Alerts screen's rules panel, the Notifications screen, and the home stop saying "set in deploy/.env" and link
  to Settings.
- `deploy/.env.example` and the deployment guide say that the variables are first values and Settings wins.
- New settings cost a definition and a validator, which the test enforces.
