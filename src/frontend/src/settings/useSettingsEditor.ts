import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router";

import { api, ApiError } from "../api/client";
import type {
  Setting,
  SettingChangeInput,
  SettingsFile,
  SettingsListing,
  SettingsPage,
  SettingsReview,
} from "../api/types";
import { useViewMode, type ViewMode } from "../ui/ViewToggle";
import type { SettingEdit } from "./SettingField";
import { sectionFrom, sections, type SectionId, type SectionInfo, type Watched } from "./settingSections";

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** A problem's message without the key it starts with, which the field already shows: "Must be at least 1." */
function plainProblem(message: string): string {
  const rest = message.replace(/^Veyra(:[A-Za-z0-9]+)+\s+/, "");

  return rest === message ? message : rest.charAt(0).toUpperCase() + rest.slice(1);
}

/** What the review dialog reviews: edits made on the screen, or the changes of an imported file. */
export interface PendingReview {
  /** The review from the API. */
  review: SettingsReview;
  /** The changes to save. */
  changes: SettingChangeInput[];
  /** The imported file's name; null for edits. */
  origin: string | null;
}

/** What {@link useSettingsEditor} returns: the state of the Settings screen and what can be done on it. */
export interface SettingsEditor {
  /** The section the address names, the first when it names none. */
  section: SectionId;
  /** Whether the address names a section, as when one was picked on a phone. */
  chosen: boolean;
  /** Every setting with the value in effect, the model the screen edits; null before it arrives. */
  page: SettingsPage | null;
  /** Why the settings could not be read, or null. */
  loadError: string | null;
  /** Unsaved edits by key. */
  edits: Map<string, SettingEdit>;
  /** Problems from the last review, by key. */
  problems: Map<string, string>;
  /** Problems no field shows, and failures of export or import. */
  loose: string[];
  /** The changes under review in the dialog, or null when it is closed. */
  pending: PendingReview | null;
  /** Why the last save failed, shown in the dialog, or null. */
  saveError: string | null;
  /** Why the last save was refused because someone saved first, or null. */
  conflict: string | null;
  /** Whether a save is running. */
  busy: boolean;
  /** What the last save, revert, or import did, or null. */
  status: string | null;
  /** The words the settings are searched for. */
  query: string;
  /** Whether the history is open. */
  history: boolean;
  /** The server's counts of monitored servers and agents, for the section list; null before they arrive. */
  watched: Watched | null;
  /** The Simple or Advanced view, which the server lists the settings for. */
  view: ViewMode;
  /** Every setting by key. */
  byKey: Map<string, Setting>;
  /** The settings the server listed for the search, the section, and the view, in its order. */
  shown: Setting[];
  /** The sections the server listed for the view, and the one open now. */
  listed: SectionInfo[];
  /** The edits to send, as changes. */
  editList: SettingChangeInput[];
  /** The cards of the settings shown, in order. */
  groups: string[];
  /**
   * How many unsaved edits a section holds.
   *
   * @param id - The section.
   * @returns The count.
   */
  counts(id: SectionId): number;
  /**
   * Edits a setting, or takes its edit back with undefined.
   *
   * @param key - The setting's key.
   * @param value - The edit.
   */
  edit(key: string, value: SettingEdit): void;
  /** Throws every unsaved edit away. */
  discard(): void;
  /**
   * Searches the settings.
   *
   * @param query - The words.
   */
  setQuery(query: string): void;
  /**
   * Opens or closes the history.
   *
   * @param open - Whether it is open.
   */
  setHistory(open: boolean): void;
  /** Asks the server to review the edits and opens the dialog, or shows the problems it found. */
  reviewEdits(): Promise<void>;
  /**
   * Saves the changes under review.
   *
   * @param confirmed - Whether the person confirmed a change that needs it.
   */
  save(confirmed: boolean): Promise<void>;
  /** Closes the review without saving. */
  closeReview(): void;
  /** Downloads every saved setting as a file. */
  exportFile(): Promise<void>;
  /**
   * Reviews the changes an exported file would make.
   *
   * @param file - The file the person picked.
   */
  importFile(file: File): Promise<void>;
  /** Reads the latest values again after a conflict. */
  reload(): void;
  /**
   * Takes the page the server answered a revert with.
   *
   * @param next - The page with the earlier value.
   */
  reverted(next: SettingsPage): void;
}

/**
 * The Settings screen's state and actions for every look (ADR 0017, ADR 0021): the page of every setting as the model
 * that is edited, the server's listing for the search, the section, and the view (VEY-67), the edits, the review
 * before saving, saving with its conflicts and problems, and export and import. A look draws it in its own frame.
 *
 * @returns The state and the actions.
 */
export function useSettingsEditor(): SettingsEditor {
  const [params] = useSearchParams();
  const section = sectionFrom(params.get("section"));
  const chosen = params.has("section");
  const [page, setPage] = useState<SettingsPage | null>(null);
  const [listing, setListing] = useState<SettingsListing | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [edits, setEdits] = useState<Map<string, SettingEdit>>(new Map());
  const [problems, setProblems] = useState<Map<string, string>>(new Map());
  const [loose, setLoose] = useState<string[]>([]);
  const [pending, setPending] = useState<PendingReview | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [history, setHistory] = useState(false);
  const [watched, setWatched] = useState<Watched | null>(null);
  const [view] = useViewMode();

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

  // The server decides which settings and sections are listed for the search, the section, and the view (VEY-67).
  // The page above stays the model that is edited; the last listing stays on screen until the next one arrives.
  useEffect(() => {
    const controller = new AbortController();

    api
      .settingsListing({ search: query, section, view }, controller.signal)
      .then(setListing)
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(messageOf(failure));
        }
      });

    return () => controller.abort();
  }, [query, section, view]);

  // The servers and agents give the Database monitoring and Agents sections their lines in the list. Refreshed when
  // the section changes, so a server just added is counted on the way back.
  useEffect(() => {
    // The server counts them (VEY-68), so the screen reads no lists only to count them.
    api
      .agentsSummary()
      .then(setWatched)
      .catch(() => setWatched(null));
  }, [section]);

  const byKey = useMemo(() => new Map(page?.settings.map((setting) => [setting.key, setting]) ?? []), [page]);

  // The settings the server listed, in its order, as the page holds them now.
  const shown = useMemo(() => (listing?.keys ?? []).flatMap((key) => byKey.get(key) ?? []), [listing, byKey]);

  // The sections the server listed for the view, and the one open now, as when a link opens it.
  const listed = sections.filter((entry) => entry.id === section || listing?.sections.includes(entry.id));

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

  return {
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
    groups: [...new Set(shown.map((setting) => setting.group))],
    counts: (id) => editList.filter((change) => byKey.get(change.key)?.section === id).length,
    edit,
    discard: () => setEdits(new Map()),
    setQuery,
    setHistory,
    reviewEdits: () => review(editList, null),
    save,
    closeReview: () => setPending(null),
    exportFile,
    importFile,
    reload: () => {
      setConflict(null);
      load();
    },
    reverted: (next) => {
      setPage(next);
      setStatus("Reverted. Veyra uses the earlier value now.");
    },
  };
}
