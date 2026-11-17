import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";

import { api, ApiError } from "../api/client";
import { useViewMode, ViewToggle } from "../app/ViewToggle";
import type { SettingChangeInput, SettingsFile, SettingsPage, SettingsReview } from "../api/types";
import { AgentsPanel } from "./AgentsPanel";
import { ClearTelemetry } from "./ClearTelemetry";
import { DatabaseMonitoring } from "./DatabaseMonitoring";
import { ReviewDialog } from "./ReviewDialog";
import { SettingCard } from "./SettingCard";
import { fieldName, type SettingEdit } from "./SettingField";
import {
  pageSections,
  sectionFrom,
  sectionInfo,
  sections,
  sectionSummary,
  type SectionId,
  type Watched,
} from "./settingSections";
import { SettingsHistoryPanel } from "./SettingsHistoryPanel";
import { describeValue, matchesSearch } from "./settingsFormat";
import "./settings.css";

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** A problem's message without the key it starts with, which the field already shows: "Must be at least 1." */
function plainProblem(message: string): string {
  const rest = message.replace(/^Veyra(:[A-Za-z0-9]+)+\s+/, "");

  return rest === message ? message : rest.charAt(0).toUpperCase() + rest.slice(1);
}

/** What the dialog reviews: edits made on the screen, or the changes of an imported file. */
interface Pending {
  /** The review from the API. */
  review: SettingsReview;
  /** The changes to save. */
  changes: SettingChangeInput[];
  /** The imported file's name; null for edits. */
  origin: string | null;
}

/**
 * Every module's settings for Admins (ADR 0017): sections with a search, a field per setting with its default and who
 * saved it, edits collected in a bar, a review before saving, the history with revert, and export and import. Saved
 * changes apply at once. On a phone the sections are a list that opens each one.
 */
export function SettingsScreen() {
  const [params] = useSearchParams();
  const section = sectionFrom(params.get("section"));
  const chosen = params.has("section");
  const [page, setPage] = useState<SettingsPage | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [edits, setEdits] = useState<Map<string, SettingEdit>>(new Map());
  const [problems, setProblems] = useState<Map<string, string>>(new Map());
  const [loose, setLoose] = useState<string[]>([]);
  const [pending, setPending] = useState<Pending | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [history, setHistory] = useState(false);
  const [watched, setWatched] = useState<Watched | null>(null);
  const [view] = useViewMode();
  const search = useRef<HTMLInputElement>(null);
  const importer = useRef<HTMLInputElement>(null);

  const load = useCallback(() => {
    api
      .settings()
      .then((next) => {
        setPage(next);
        setLoadError(null);
      })
      .catch((failure: unknown) => setLoadError(messageOf(failure)));
  }, []);

  useEffect(load, [load]);

  // The servers and agents give the Database monitoring and Agents sections their lines in the list. Refreshed when
  // the section changes, so a server just added is counted on the way back.
  useEffect(() => {
    Promise.all([api.sqlServers(), api.postgresServers(), api.agents()])
      .then(([servers, postgres, agents]) => setWatched({ servers, postgres, agents }))
      .catch(() => setWatched(null));
  }, [section]);

  // Ctrl+K or Cmd+K finds a setting, as the search box says.
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        search.current?.focus();
      }
    }

    window.addEventListener("keydown", onKey);

    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const byKey = useMemo(() => new Map(page?.settings.map((setting) => [setting.key, setting]) ?? []), [page]);

  // A search looks through every setting in either view. A section shows its basics in the Simple view, unless it
  // has none, as when a link from elsewhere opens an advanced-only section.
  const shown = useMemo(() => {
    const settings = page?.settings ?? [];

    if (query.trim()) {
      return settings.filter((setting) => matchesSearch(setting, query));
    }

    const inSection = settings.filter((setting) => setting.section === section);
    const basics = inSection.filter((setting) => !setting.advanced);

    return view === "advanced" || basics.length === 0 ? inSection : basics;
  }, [page, query, section, view]);

  // The Simple view lists only sections with basics, and the pages of what Veyra watches, and leaves out System.
  const listed = sections.filter(
    (entry) =>
      view === "advanced" ||
      entry.id === section ||
      pageSections.includes(entry.id) ||
      page?.settings.some((setting) => setting.section === entry.id && !setting.advanced),
  );

  const editList: SettingChangeInput[] = [...edits.entries()]
    .filter((entry): entry is [string, string | null] => entry[1] !== undefined)
    .map(([key, value]) => ({ key, value }));

  function edit(key: string, value: SettingEdit) {
    setEdits((current) => {
      const next = new Map(current);

      if (value === undefined) {
        next.delete(key);
      } else {
        next.set(key, value);
      }

      return next;
    });
    setProblems((current) => {
      const next = new Map(current);
      next.delete(key);

      return next;
    });
    setStatus(null);
  }

  function showProblems(review: SettingsReview) {
    setProblems(
      new Map(
        review.problems.flatMap((p) => (p.key && byKey.has(p.key) ? [[p.key, plainProblem(p.message)] as const] : [])),
      ),
    );
    setLoose(review.problems.filter((p) => !p.key || !byKey.has(p.key)).map((p) => p.message));
  }

  async function review(changes: SettingChangeInput[], origin: string | null, from?: SettingsReview) {
    if (!page) {
      return;
    }

    setConflict(null);
    setSaveError(null);

    try {
      const answer = from ?? (await api.reviewSettings(page.version, changes));

      if (answer.problems.length > 0) {
        showProblems(answer);

        return;
      }

      setProblems(new Map());
      setLoose([]);
      setPending({ review: answer, changes, origin });
    } catch (failure) {
      setLoose([messageOf(failure)]);
    }
  }

  async function save(confirmed: boolean) {
    if (!page || !pending) {
      return;
    }

    setBusy(true);

    try {
      const next = await api.saveSettings(page.version, pending.changes, confirmed, pending.origin);
      setPage(next);
      setEdits(new Map());
      setPending(null);
      setStatus("Saved. Veyra uses the new values now.");
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setPending(null);
        setConflict(failure.message);
      } else if (failure instanceof ApiError && failure.status === 400 && failure.body) {
        setPending(null);
        showProblems(failure.body as SettingsReview);
      } else {
        setSaveError(messageOf(failure));
      }
    } finally {
      setBusy(false);
    }
  }

  async function exportFile() {
    try {
      const file = await api.exportSettings();
      const url = URL.createObjectURL(new Blob([JSON.stringify(file, null, 2)], { type: "application/json" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = `veyra-settings-${(file.exportedAt ?? new Date().toISOString()).slice(0, 10)}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (failure) {
      setLoose([messageOf(failure)]);
    }
  }

  async function importFile(file: File) {
    try {
      const document = JSON.parse(await file.text()) as SettingsFile;
      const answer = await api.importSettings(document);

      if (answer.problems.length === 0 && answer.changes.length === 0) {
        setStatus(`${file.name} matches the settings in effect. Nothing to change.`);

        return;
      }

      await review(
        answer.changes.map((change) => ({ key: change.key, value: change.to })),
        file.name,
        answer,
      );
    } catch (failure) {
      setLoose([failure instanceof SyntaxError ? `${file.name} is not a settings file.` : messageOf(failure)]);
    }
  }

  const title = sectionInfo(section);
  const groups = [...new Set(shown.map((setting) => setting.group))];
  const counts = (id: SectionId) => editList.filter((change) => byKey.get(change.key)?.section === id).length;

  return (
    <main className={`settings-screen${chosen ? " chosen" : ""}${history ? " with-history" : ""}`}>
      <header className="settings-head">
        <div>
          <h1>Settings</h1>
          <p>
            How Veyra keeps data, raises alerts, sends messages, and signs people in. Changes apply at once and are kept
            in the history.
          </p>
        </div>
        <div className="settings-tools">
          <ViewToggle />
          <label className="settings-search">
            <span className="visually-hidden">Find a setting</span>
            <input
              ref={search}
              type="search"
              placeholder="Find a setting"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
            <kbd aria-hidden="true">Ctrl K</kbd>
          </label>
          <button type="button" className="button" aria-pressed={history} onClick={() => setHistory(!history)}>
            History
          </button>
          <button type="button" className="button" onClick={() => void exportFile()}>
            Export
          </button>
          <button type="button" className="button" onClick={() => importer.current?.click()}>
            Import
          </button>
          <input
            ref={importer}
            type="file"
            accept="application/json,.json"
            hidden
            onChange={(event) => {
              const file = event.target.files?.[0];
              event.target.value = "";

              if (file) {
                void importFile(file);
              }
            }}
          />
        </div>
      </header>

      {loadError && (
        <p className="settings-error" role="alert">
          {loadError}
        </p>
      )}

      {page && (
        <div className="settings-body">
          <nav className="settings-sections" aria-label="Settings sections">
            {listed.map((entry) => {
              const count = entry.id === "System" ? 0 : counts(entry.id);

              return (
                <Link
                  key={entry.id}
                  to={`?section=${entry.id}`}
                  className={`settings-section-link${entry.id === "System" ? " system" : ""}${entry.id === section && !query ? " on" : ""}`}
                  aria-current={entry.id === section && !query ? "page" : undefined}
                  onClick={() => setQuery("")}
                >
                  <span>
                    <b>{entry.title}</b>
                    <small>{sectionSummary(entry.id, page.settings, watched)}</small>
                  </span>
                  {count > 0 && (
                    <span className="settings-count" aria-label={`${count} unsaved`}>
                      {count}
                    </span>
                  )}
                </Link>
              );
            })}
            <p className="settings-sections-note">
              Only Admins see Settings. Values saved here win over deploy/.env, which gives the first values.
            </p>
          </nav>

          <section className="settings-content" aria-labelledby="settings-content-title">
            <Link className="settings-back" to="/settings">
              ‹ All settings
            </Link>
            <div className="settings-content-head">
              <h2 id="settings-content-title">{query ? `Settings matching “${query}”` : title.title}</h2>
              {!query && <p>{title.intro}</p>}
            </div>

            {status && (
              <p className="settings-status" role="status">
                {status}
              </p>
            )}
            {loose.map((message) => (
              <p key={message} className="settings-error" role="alert">
                {message}
              </p>
            ))}
            {conflict && (
              <div className="settings-error" role="alert">
                {conflict}{" "}
                <button
                  type="button"
                  className="link-button"
                  onClick={() => {
                    setConflict(null);
                    load();
                  }}
                >
                  Load the latest values
                </button>
              </div>
            )}

            {section === "Databases" && !query ? (
              <DatabaseMonitoring />
            ) : section === "Agents" && !query ? (
              <AgentsPanel />
            ) : section === "System" && !query ? (
              <div className="settings-card">
                <dl className="settings-system">
                  {page.system.map((item) => (
                    <div key={item.label}>
                      <dt>{item.label}</dt>
                      <dd>
                        {item.value}
                        <small>{item.source}</small>
                      </dd>
                    </div>
                  ))}
                </dl>
              </div>
            ) : (
              groups.map((group) => (
                <SettingCard
                  key={group}
                  group={group}
                  settings={shown.filter((setting) => setting.group === group)}
                  edits={edits}
                  problems={problems}
                  tagAdvanced={view === "simple"}
                  onEdit={edit}
                />
              ))
            )}
            {section === "Data" && !query && <ClearTelemetry />}
            {query && shown.length === 0 && <p className="settings-empty">No setting matches “{query}”.</p>}
          </section>
        </div>
      )}

      {editList.length > 0 && (
        <section className="settings-savebar" aria-label="Unsaved changes">
          <i aria-hidden="true" />
          <div>
            <b>{editList.length === 1 ? "1 unsaved change" : `${editList.length} unsaved changes`}</b>
            <ul>
              {editList.map((change) => {
                const setting = byKey.get(change.key);

                if (!setting) {
                  return null;
                }

                return (
                  <li key={change.key}>
                    {setting.kind === "Secret"
                      ? `${fieldName(setting)}: changed`
                      : `${fieldName(setting)}: ${describeValue(setting, setting.value)} to ${
                          change.value === null
                            ? `the default, ${describeValue(setting, setting.default)}`
                            : describeValue(setting, change.value)
                        }`}
                  </li>
                );
              })}
            </ul>
          </div>
          <button type="button" className="button quiet" onClick={() => setEdits(new Map())}>
            Discard
          </button>
          <button type="button" className="button primary" onClick={() => void review(editList, null)}>
            Review and save
          </button>
        </section>
      )}

      {pending && page && (
        <ReviewDialog
          review={pending.review}
          settings={byKey}
          count={pending.changes.length}
          origin={pending.origin}
          busy={busy}
          error={saveError}
          onSave={(confirmed) => void save(confirmed)}
          onClose={() => setPending(null)}
        />
      )}

      {history && page && (
        <SettingsHistoryPanel
          settings={byKey}
          version={page.version}
          onReverted={(next) => {
            setPage(next);
            setStatus("Reverted. Veyra uses the earlier value now.");
          }}
          onClose={() => setHistory(false)}
        />
      )}
    </main>
  );
}
