import { useEffect, useRef } from "react";
import { Link } from "react-router";

import { ScreenBar } from "../ui/ScreenBar";
import { LookPanel } from "../account/LookPanel";
import { AgentsPanel } from "./AgentsPanel";
import { ClearTelemetry } from "./ClearTelemetry";
import { DatabaseMonitoring } from "./DatabaseMonitoring";
import { ReviewDialog } from "./ReviewDialog";
import { SettingCard } from "./SettingCard";
import { sectionInfo, sectionSummary } from "./settingSections";
import { SettingsHistoryPanel } from "./SettingsHistoryPanel";
import { changeWords } from "./settingsFormat";
import { useSettingsEditor } from "./useSettingsEditor";
import "./settings.css";

/**
 * Every module's settings for Admins (ADR 0017): sections with a search, a field per setting with its default and who
 * saved it, edits collected in a bar, a review before saving, the history with revert, and export and import. Saved
 * changes apply at once. On a phone the sections are a list that opens each one. The state and the actions are the
 * shared {@link useSettingsEditor}, which other looks draw in their own frame.
 */
export function SettingsScreen() {
  const editor = useSettingsEditor();
  const {
    section,
    chosen,
    page,
    loadError,
    edits,
    problems,
    loose,
    pending,
    saveError,
    conflict,
    busy,
    status,
    query,
    history,
    watched,
    view,
    byKey,
    shown,
    listed,
    editList,
    groups,
    counts,
  } = editor;
  const search = useRef<HTMLInputElement>(null);
  const importer = useRef<HTMLInputElement>(null);

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

  const title = sectionInfo(section);

  return (
    <main className={`settings-screen${chosen ? " chosen" : ""}${history ? " with-history" : ""}`}>
      <ScreenBar
        trail={chosen ? [{ label: "Settings", to: "/settings" }, { label: title.title }] : [{ label: "Settings" }]}
        view
      />
      <header className="settings-head">
        <div>
          <h1>Settings</h1>
          <p>
            How Veyra keeps data, raises alerts, sends messages, and signs people in. Changes apply at once and are kept
            in the history.
          </p>
        </div>
        <div className="settings-tools">
          <label className="settings-search">
            <span className="visually-hidden">Find a setting</span>
            <input
              ref={search}
              type="search"
              placeholder="Find a setting"
              value={query}
              onChange={(event) => editor.setQuery(event.target.value)}
            />
            <kbd aria-hidden="true">Ctrl K</kbd>
          </label>
          <button type="button" className="button" aria-pressed={history} onClick={() => editor.setHistory(!history)}>
            History
          </button>
          <button type="button" className="button" onClick={() => void editor.exportFile()}>
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
                void editor.importFile(file);
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
                  onClick={() => editor.setQuery("")}
                >
                  <span>
                    <b>{entry.title}</b>
                    <small>{sectionSummary(entry.id, page, watched)}</small>
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
                <button type="button" className="link-button" onClick={editor.reload}>
                  Load the latest values
                </button>
              </div>
            )}

            {section === "Appearance" && !query && <LookPanel />}
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
                  onEdit={editor.edit}
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

                return <li key={change.key}>{changeWords(setting, change.value)}</li>;
              })}
            </ul>
          </div>
          <button type="button" className="button quiet" onClick={editor.discard}>
            Discard
          </button>
          <button type="button" className="button primary" onClick={() => void editor.reviewEdits()}>
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
          onSave={(confirmed) => void editor.save(confirmed)}
          onClose={editor.closeReview}
        />
      )}

      {history && page && (
        <SettingsHistoryPanel
          settings={byKey}
          version={page.version}
          onReverted={editor.reverted}
          onClose={() => editor.setHistory(false)}
        />
      )}
    </main>
  );
}
