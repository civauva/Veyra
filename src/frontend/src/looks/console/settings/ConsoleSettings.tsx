import { useId, useRef } from "react";
import { Link, useNavigate } from "react-router";

import { lookOptions, useLookChoice, type LookOption } from "../../../account/useLookChoice";
import type { Setting } from "../../../api/types";
import { AgentsPanel } from "../../../settings/AgentsPanel";
import { ClearTelemetry } from "../../../settings/ClearTelemetry";
import { DatabaseMonitoring } from "../../../settings/DatabaseMonitoring";
import { ReviewDialog } from "../../../settings/ReviewDialog";
import { SettingControl, SettingField, SettingNote, type SettingEdit } from "../../../settings/SettingField";
import { sectionInfo, sectionSummary } from "../../../settings/settingSections";
import { SettingsHistoryPanel } from "../../../settings/SettingsHistoryPanel";
import { changeWords } from "../../../settings/settingsFormat";
import "../../../settings/settings.css";
import { useSettingsEditor, type SettingsEditor } from "../../../settings/useSettingsEditor";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { ConsoleIcon } from "../ConsoleIcon";
import "./consoleSettings.css";

/**
 * Console's Settings (VEY-79, ADR 0029): the shared settings (`useSettingsEditor`) in Console's frame. The sections at
 * the left, a select on phones; the search, History, Export, and Import in the head; each card of settings as a panel
 * of rows with the name and what it does beside its control; and a save bar that stays in view with Discard and Review
 * and save. Look and feel offers every look as a card, then the defaults for everyone.
 */
export function ConsoleSettings() {
  const editor = useSettingsEditor();
  const { section, page, query, history, loadError } = editor;
  const navigate = useNavigate();
  const importer = useRef<HTMLInputElement>(null);
  const title = sectionInfo(section);

  return (
    <main className="cn-screen sq st">
      <ScreenBar trail={[{ label: "Settings", to: "/settings" }, { label: title.title }]} view />
      <div className="cn-head">
        <div>
          <h1>Settings</h1>
          <p>Every setting is stored in Veyra and applied without a restart.</p>
        </div>
        <div className="cn-head-actions st-tools">
          <input
            type="search"
            className="cn-find"
            aria-label="Find a setting"
            placeholder="Find a setting"
            value={query}
            onChange={(event) => editor.setQuery(event.target.value)}
          />
          <button
            type="button"
            className="cn-button"
            aria-pressed={history}
            onClick={() => editor.setHistory(!history)}
          >
            History
          </button>
          <button type="button" className="cn-button" onClick={() => void editor.exportFile()}>
            Export
          </button>
          <button type="button" className="cn-button" onClick={() => importer.current?.click()}>
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
      </div>

      {loadError && (
        <p className="cn-error" role="alert">
          {loadError}
        </p>
      )}

      {page && (
        <div className="st-body">
          <nav className="st-nav" aria-label="Settings sections">
            {editor.listed.map((entry) => {
              const count = entry.id === "System" ? 0 : editor.counts(entry.id);
              const on = entry.id === section && !query;

              return (
                <Link
                  key={entry.id}
                  to={`?section=${entry.id}`}
                  className={on ? "on" : undefined}
                  aria-current={on ? "page" : undefined}
                  title={sectionSummary(entry.id, page, editor.watched)}
                  onClick={() => editor.setQuery("")}
                >
                  {entry.title}
                  {count > 0 && (
                    <span className="st-count" aria-label={`${count} unsaved`}>
                      {count}
                    </span>
                  )}
                </Link>
              );
            })}
          </nav>
          <label className="cn-filter st-pick">
            <span>Section:</span>
            <select
              aria-label="Section"
              value={section}
              onChange={(event) => {
                editor.setQuery("");
                void navigate(`?section=${event.target.value}`);
              }}
            >
              {editor.listed.map((entry) => (
                <option key={entry.id} value={entry.id}>
                  {entry.title}
                </option>
              ))}
            </select>
          </label>

          <div className="st-content">
            <p className="st-intro">
              <b>{query ? `Settings matching “${query}”` : title.title}</b>
              {!query && <span>{title.intro}</span>}
            </p>
            <Notices editor={editor} />
            <SectionBody editor={editor} />
          </div>
        </div>
      )}

      {editor.editList.length > 0 && <SaveBar editor={editor} />}

      {editor.pending && page && (
        <ReviewDialog
          review={editor.pending.review}
          settings={editor.byKey}
          count={editor.pending.changes.length}
          origin={editor.pending.origin}
          busy={editor.busy}
          error={editor.saveError}
          onSave={(confirmed) => void editor.save(confirmed)}
          onClose={editor.closeReview}
        />
      )}

      {history && page && (
        <SettingsHistoryPanel
          settings={editor.byKey}
          version={page.version}
          onReverted={editor.reverted}
          onClose={() => editor.setHistory(false)}
        />
      )}
    </main>
  );
}

/** What the last save, revert, or import did, problems no field shows, and a save someone else beat. */
function Notices({ editor }: { editor: SettingsEditor }) {
  return (
    <>
      {editor.status && (
        <p className="st-status" role="status">
          {editor.status}
        </p>
      )}
      {editor.loose.map((message) => (
        <p key={message} className="cn-error" role="alert">
          {message}
        </p>
      ))}
      {editor.conflict && (
        <div className="cn-error" role="alert">
          {editor.conflict}{" "}
          <button type="button" className="link-button" onClick={editor.reload}>
            Load the latest values
          </button>
        </div>
      )}
    </>
  );
}

/** The open section: its own pages, the look cards, or its cards of settings as panels. */
function SectionBody({ editor }: { editor: SettingsEditor }) {
  const { section, query, page, shown } = editor;
  const plain = !query;

  if (plain && section === "Databases") {
    return <DatabaseMonitoring />;
  }

  if (plain && section === "Agents") {
    return <AgentsPanel />;
  }

  if (plain && section === "System" && page) {
    return (
      <section className="cn-card" aria-labelledby="st-system">
        <header>
          <h2 id="st-system">System</h2>
          <HelpTip label="System">
            What is set where Veyra is installed, before the database can be read: the database, the addresses, and the
            keys. Change these in deploy/.env and restart.
          </HelpTip>
        </header>
        <dl className="sq-pad tr-kv st-system">
          {page.system.map((item) => (
            <SystemRow key={item.label} label={item.label} value={item.value} source={item.source} />
          ))}
        </dl>
      </section>
    );
  }

  return (
    <>
      {plain && section === "Appearance" && <LookCards />}
      {editor.groups.map((group) => (
        <GroupPanel
          key={group}
          editor={editor}
          group={group}
          title={section === "Appearance" && group === "Look" ? "Look for everyone" : group}
          settings={shown.filter((setting) => setting.group === group)}
        />
      ))}
      {plain && section === "Data" && <ClearTelemetry />}
      {query && shown.length === 0 && <p className="cn-empty">No setting matches “{query}”.</p>}
    </>
  );
}

/** One fact of the installation, with where it is set. */
function SystemRow({ label, value, source }: { label: string; value: string; source: string }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>
        {value}
        <small className="st-source">{source}</small>
      </dd>
    </>
  );
}

/** Props for {@link GroupPanel}. */
interface GroupPanelProps {
  /** The editor, with the edits and problems. */
  editor: SettingsEditor;
  /** The card's name, as the server groups the settings. */
  group: string;
  /** The panel's title. */
  title: string;
  /** The card's settings the server listed, in its order. */
  settings: Setting[];
}

/** A card of settings as a panel: a rule's On switch in its header, then a row per setting. */
function GroupPanel({ editor, group, title, settings }: GroupPanelProps) {
  const id = useId();
  const toggle = settings.find((setting) => setting.kind === "Switch" && setting.label === "On");
  const rows = settings.filter((setting) => setting !== toggle);

  return (
    <section className="cn-card" aria-labelledby={id}>
      <header>
        <h2 id={id}>{title}</h2>
        <HelpTip label={title}>
          The settings of {group.toLowerCase()}: each says what it does, its default, and who saved it last. A change
          applies once it is reviewed and saved, without a restart.
        </HelpTip>
        {editor.section === "Appearance" && group === "Look" && <span className="cn-meta">Admins only</span>}
        {toggle && (
          <span className="st-toggle">
            <SettingField
              setting={toggle}
              edit={editor.edits.get(toggle.key)}
              problem={editor.problems.get(toggle.key) ?? null}
              onEdit={(edit) => editor.edit(toggle.key, edit)}
            />
          </span>
        )}
      </header>
      <div className="st-rows">
        {rows.map((setting) => (
          <SettingRow
            key={setting.key}
            setting={setting}
            edit={editor.edits.get(setting.key)}
            problem={editor.problems.get(setting.key) ?? null}
            tagAdvanced={editor.view === "simple"}
            onEdit={(edit) => editor.edit(setting.key, edit)}
          />
        ))}
      </div>
      {editor.section === "Appearance" && group === "Look" && (
        <footer>
          <span>Every change is kept in the history</span>
          <button type="button" className="link-button" onClick={() => editor.setHistory(true)}>
            History →
          </button>
        </footer>
      )}
    </section>
  );
}

/** Props for {@link SettingRow}. */
interface SettingRowProps {
  /** The setting. */
  setting: Setting;
  /** Its unsaved edit, if any. */
  edit: SettingEdit;
  /** Why the last review refused it, or null. */
  problem: string | null;
  /** Whether to tag it when it is an Advanced one, as when a search finds it in the Simple view. */
  tagAdvanced: boolean;
  /** Called with its new edit. */
  onEdit: (edit: SettingEdit) => void;
}

/** One setting as a row: its name and what it does, and its control with the note under it. */
function SettingRow({ setting, edit, problem, tagAdvanced, onEdit }: SettingRowProps) {
  const id = useId();
  const noteId = `${id}-note`;
  const edited = edit !== undefined;

  return (
    <div className={edited ? "st-row changed" : "st-row"}>
      <div className="st-row-text">
        <label htmlFor={id}>
          {setting.label}
          {tagAdvanced && setting.advanced && <span className="cn-pill off">Advanced</span>}
        </label>
        <span>{setting.help}</span>
      </div>
      <div className="st-row-control">
        <SettingControl setting={setting} edit={edit} problem={problem} id={id} noteId={noteId} onEdit={onEdit} />
        <SettingNote setting={setting} edit={edit} problem={problem} id={noteId} onEdit={onEdit} />
      </div>
    </div>
  );
}

/** The person's own look, every look as a card with a small picture of it. */
function LookCards() {
  const look = useLookChoice();
  const titleId = useId();

  if (!look) {
    return null;
  }

  const fallbackName = lookOptions.find((option) => option.id === look.fallback)?.name ?? look.fallback;

  return (
    <section className="cn-card" aria-labelledby={titleId}>
      <header>
        <ConsoleIcon name="eye" />
        <h2 id={titleId}>Your look</h2>
        <HelpTip label="Your look">
          How the whole app looks for you: the menu, the home, and every screen. The data, the alerts, and their words
          are the same in every look. Your choice is kept on your account, so it follows you to every device.
        </HelpTip>
        <span className="cn-meta">Your choice · saved to your account</span>
      </header>
      <div className="st-row st-look-row">
        <div className="st-row-text">
          <b>Look</b>
          <span>
            How the whole app looks for you: the menu, the home, and every screen. Data, alerts and words are the same
            in every look.
          </span>
          {!look.mayChoose && <span>An Admin has set one look for everyone: {fallbackName}.</span>}
        </div>
        <div className="st-looks" role="radiogroup" aria-label="Look">
          {lookOptions.map((option) => (
            <LookCard
              key={option.id}
              look={option}
              name={`${titleId}-look`}
              checked={option.id === look.shown}
              fallback={option.id === look.fallback}
              disabled={!look.mayChoose || look.busy}
              onPick={() => void look.choose(option.id)}
            />
          ))}
        </div>
      </div>
      {look.error && (
        <p className="cn-error sq-pad" role="alert">
          {look.error}
        </p>
      )}
      <footer>
        <span>Everyone, not only Admins, also finds this under their account menu.</span>
        {look.mayChoose && look.own && (
          <button type="button" className="link-button" disabled={look.busy} onClick={() => void look.choose(null)}>
            Use the default, {fallbackName}
          </button>
        )}
      </footer>
    </section>
  );
}

/** Props for {@link LookCard}. */
interface LookCardProps {
  /** The look. */
  look: LookOption;
  /** The radio group's name. */
  name: string;
  /** Whether it is the look the person sees. */
  checked: boolean;
  /** Whether it is the instance's default. */
  fallback: boolean;
  /** Whether the choice is closed. */
  disabled: boolean;
  /** Picks it. */
  onPick: () => void;
}

/** One look as a card: a small picture of it, its name, whether it is the default, and what it is like. */
function LookCard({ look, name, checked, fallback, disabled, onPick }: LookCardProps) {
  const { id } = look;

  return (
    <label className={checked ? "st-look on" : "st-look"}>
      <span className={`st-shot st-shot-${id}`} aria-hidden="true">
        {id === "console" ? <ConsoleShot /> : <ForecastShot />}
      </span>
      <span className="st-look-name">
        <input type="radio" name={name} value={id} checked={checked} disabled={disabled} onChange={onPick} />
        <b>{look.name}</b>
        {fallback && <span className={checked ? "cn-pill ok" : "cn-pill off"}>Default</span>}
      </span>
      <span className="st-look-words">{look.words}</span>
    </label>
  );
}

/** A small picture of Console: the menu, a strip of figures, panels, and tables. */
function ConsoleShot() {
  return (
    <span className="st-console">
      <span className="st-console-menu">
        {Array.from({ length: 8 }, (_, index) => (
          <i key={index} />
        ))}
      </span>
      <span className="st-console-page">
        <span className="st-console-figures">
          {Array.from({ length: 6 }, (_, index) => (
            <i key={index} className={index === 4 ? "bad" : undefined} />
          ))}
        </span>
        <span className="st-console-panels">
          <i />
          <i />
          <i />
        </span>
        <span className="st-console-tables">
          <i />
          <i />
          <i />
        </span>
      </span>
    </span>
  );
}

/** A small picture of Forecast: the condition and the hours ahead. */
function ForecastShot() {
  return (
    <span className="st-forecast">
      <small>Production · now</small>
      <b>Mostly clear</b>
      <span className="st-forecast-hours">
        {Array.from({ length: 8 }, (_, index) => (
          <i key={index} className={index === 5 ? "bad" : undefined} style={{ height: `${10 + (index % 3) * 6}px` }} />
        ))}
      </span>
    </span>
  );
}

/** The changes not saved yet, with Discard and Review and save; it stays at the foot of the view. */
function SaveBar({ editor }: { editor: SettingsEditor }) {
  const count = editor.editList.length;
  const changed = editor.editList.flatMap((change) => {
    const setting = editor.byKey.get(change.key);

    return setting ? [{ key: change.key, setting, words: changeWords(setting, change.value) }] : [];
  });

  return (
    <section className="st-savebar" aria-label="Unsaved changes">
      <div className="st-savebar-text">
        <b>{count === 1 ? "1 change" : `${count} changes`}</b>
        <ul>
          {changed.map((one) => (
            <li key={one.key} title={one.words}>
              <span className="st-savebar-label">{one.setting.label}</span>
              <span className="visually-hidden">{one.words}</span>
            </li>
          ))}
        </ul>
      </div>
      <button type="button" className="cn-button" onClick={editor.discard}>
        Discard
      </button>
      <button type="button" className="cn-button primary" onClick={() => void editor.reviewEdits()}>
        Review and save
      </button>
    </section>
  );
}
