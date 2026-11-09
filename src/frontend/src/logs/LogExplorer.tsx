import { useEffect, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";

import type { LogEntry } from "../api/types";
import { FilterPanel } from "./FilterPanel";
import { formatCount } from "./format";
import { Histogram } from "./Histogram";
import { LogDetail } from "./LogDetail";
import { defaultFilters, filtersFromUrl, filtersToUrl, type LogFilters } from "./logFilters";
import { LogList } from "./LogList";
import { severityLabels } from "./SeverityBadge";
import { useLogSearch } from "./useLogSearch";
import "./logs.css";

/**
 * The Log Explorer (VEY-14 mockups): filters on the left, search, histogram, and results in the middle, the selected
 * record on the right. The filters live in the URL, so a link or a reload shows the same view.
 */
export function LogExplorer() {
  const [searchParams, setSearchParams] = useSearchParams();
  const filters = filtersFromUrl(searchParams);
  const [following, setFollowing] = useState(false);
  const [selected, setSelected] = useState<LogEntry | null>(null);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const search = useLogSearch(filters, following && !filters.around);
  const total = search.summary?.buckets.reduce((sum, bucket) => sum + bucket.total, 0) ?? null;

  function applyFilters(next: LogFilters) {
    setSearchParams(filtersToUrl(next));
  }

  function closeDetail() {
    const id = selected?.id;
    setSelected(null);

    // Return the keyboard to where the person was in the list.
    requestAnimationFrame(() => document.querySelector<HTMLButtonElement>(`.log-row[data-id="${id}"]`)?.focus());
  }

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== "Escape") {
        return;
      }

      if (filtersOpen) {
        setFiltersOpen(false);
      } else {
        setSelected(null);
      }
    }

    document.addEventListener("keydown", onKeyDown);

    return () => document.removeEventListener("keydown", onKeyDown);
  }, [filtersOpen]);

  return (
    <main className={selected ? "explorer has-detail" : "explorer"}>
      <h1 className="visually-hidden">Logs</h1>

      <FilterPanel
        filters={filters}
        onChange={applyFilters}
        services={search.summary?.services ?? []}
        from={search.from}
        to={search.to}
        open={filtersOpen}
        onClose={() => setFiltersOpen(false)}
      />

      <div className="explorer-main" aria-busy={search.loading}>
        <div className="toolbar">
          <SearchBox key={filters.text} text={filters.text} onSearch={(text) => applyFilters({ ...filters, text })} />
          <button
            type="button"
            className="button toolbar-filters"
            aria-expanded={filtersOpen}
            onClick={() => setFiltersOpen(true)}
          >
            Filters{activeFilterCount(filters) > 0 && ` · ${activeFilterCount(filters)}`}
          </button>
          <button
            type="button"
            className="button follow"
            aria-pressed={following}
            disabled={filters.around !== null}
            title={filters.around ? "Following needs a window that ends now" : "Look for new records every 5 seconds"}
            onClick={() => setFollowing(!following)}
          >
            <span className="follow-dot" aria-hidden="true" />
            Follow new
          </button>
          <button
            type="button"
            className="button toolbar-export"
            title="Download the loaded records as JSON"
            disabled={search.items.length === 0}
            onClick={() => exportRecords(search.items)}
          >
            Export
          </button>
        </div>

        <FilterSummary filters={filters} />

        <Histogram summary={search.summary} from={search.from} to={search.to} />

        {search.error && (
          <p className="explorer-error" role="alert">
            {search.error}
          </p>
        )}

        {!search.loading && !search.error && search.items.length === 0 ? (
          <p className="explorer-empty">No records match. Widen the time range or remove a filter.</p>
        ) : (
          <LogList
            items={search.items}
            selectedId={selected?.id ?? null}
            onSelect={setSelected}
            hasMore={search.hasMore}
            onLoadMore={search.loadMore}
          />
        )}

        <p className="explorer-footer">
          {search.loading && search.items.length === 0 ? (
            <span role="status">Loading…</span>
          ) : (
            <span>
              Showing {formatCount(search.items.length)}
              {total !== null && ` of ${formatCount(total)}`} · newest first
            </span>
          )}
          {search.hasMore && (
            <button type="button" className="link-button" disabled={search.loadingMore} onClick={search.loadMore}>
              Load more
            </button>
          )}
        </p>
      </div>

      {selected ? (
        <LogDetail
          entry={selected}
          onClose={closeDetail}
          onSameTrace={(traceId) => applyFilters({ ...filters, traceId, around: null })}
          onAround={(entry) =>
            applyFilters({
              ...defaultFilters,
              range: filters.range,
              services: [entry.serviceName],
              around: entry.timestamp,
            })
          }
        />
      ) : (
        <p className="detail-placeholder">Select a record to see its message, attributes, and trace.</p>
      )}
    </main>
  );
}

/** Props for {@link SearchBox}. */
interface SearchBoxProps {
  /** The text currently searched for. */
  text: string;
  /** Called with the new text when the person presses Enter or clears the box. */
  onSearch: (text: string) => void;
}

/**
 * The message search. It searches on Enter rather than on every key, so typing does not send a query per letter;
 * clearing the box searches at once. The parent remounts it with a new key when the text changes elsewhere.
 */
function SearchBox({ text, onSearch }: SearchBoxProps) {
  const [draft, setDraft] = useState(text);

  function submit(event: FormEvent) {
    event.preventDefault();
    onSearch(draft);
  }

  return (
    <form role="search" className="search" onSubmit={submit}>
      <span className="search-icon" aria-hidden="true">
        ⌕
      </span>
      <input
        type="search"
        aria-label="Search messages"
        placeholder="Words in the message"
        value={draft}
        onChange={(event) => {
          setDraft(event.target.value);

          if (event.target.value === "" && text !== "") {
            onSearch("");
          }
        }}
      />
      <span className="search-hint" aria-hidden="true">
        Enter to search
      </span>
    </form>
  );
}

/** Props for {@link FilterSummary}. */
interface FilterSummaryProps {
  /** The current filters. */
  filters: LogFilters;
}

/** The active filters as small labels above the list. Shown on phones, where the filter panel is hidden. */
function FilterSummary({ filters }: FilterSummaryProps) {
  const labels = [
    filters.minimumSeverity && `≥ ${severityLabels[filters.minimumSeverity]}`,
    filters.around ? "± 1 min" : filters.range,
    filters.services.length === 1
      ? filters.services[0]
      : filters.services.length > 1 && `${filters.services.length} services`,
    filters.attributes.length > 0 && `${filters.attributes.length} attributes`,
    filters.traceId && "one trace",
  ].filter((label): label is string => typeof label === "string" && label.length > 0);

  return (
    <p className="filter-summary" aria-hidden="true">
      {labels.map((label) => (
        <span key={label} className="filter-summary-chip">
          {label}
        </span>
      ))}
    </p>
  );
}

/** How many filters differ from the defaults, for the phone's Filters button. */
function activeFilterCount(filters: LogFilters): number {
  return [
    filters.minimumSeverity !== null,
    filters.services.length > 0,
    filters.text.trim() !== "",
    filters.attributes.length > 0,
    filters.traceId !== null,
    filters.around !== null,
  ].filter(Boolean).length;
}

/** Downloads the loaded records as a JSON file. Only what is loaded: the API has no export endpoint yet. */
function exportRecords(items: LogEntry[]): void {
  const blob = new Blob([JSON.stringify(items, null, 2)], { type: "application/json" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `veyra-logs-${new Date().toISOString().replace(/[:.]/g, "-")}.json`;
  link.click();
  URL.revokeObjectURL(link.href);
}
