import { useEffect, useRef, useState } from "react";

import type { TraceService } from "../api/types";
import { formatClock, formatCount } from "../logs/format";
import { AttributeFilters } from "../logs/FilterPanel";
import { zoneLabel } from "../time/zone";
import { serviceColor } from "./serviceColors";
import { traceRanges, type TraceFilters, type TraceShow } from "./traceFilters";
import { useViewMode } from "../app/ViewToggle";

/** How many services show before "Show all". */
const collapsedServiceCount = 8;

/** Props for {@link TraceFilterPanel}. */
interface TraceFilterPanelProps {
  /** The current filters. */
  filters: TraceFilters;
  /** Called with the new filters when the person changes one. */
  onChange: (filters: TraceFilters) => void;
  /** The services of the window, most traces first. */
  services: TraceService[];
  /** How many failed traces the chart read, for the Failed choice; null before it arrives. */
  failedCount: number | null;
  /** How many slow traces the chart read, for the Slow choice. */
  slowCount: number | null;
  /** The start of the window, shown under the time choices. */
  from: Date;
  /** The end of the window. */
  to: Date;
  /** On phones the panel is a sheet; whether it is open. Wider screens always show it. */
  open: boolean;
  /** Closes the sheet on phones. */
  onClose: () => void;
}

/**
 * The filter column of the trace list: time, failed or slow, duration, services, and attributes. Every change applies
 * at once. It offers by keyboard what the chart offers by dragging.
 */
export function TraceFilterPanel({
  filters,
  onChange,
  services,
  failedCount,
  slowCount,
  from,
  to,
  open,
  onClose,
}: TraceFilterPanelProps) {
  const [showAll, setShowAll] = useState(false);
  // Simple keeps to time, what to show, and services; a filter in use always shows, so a link never hides it.
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const doneRef = useRef<HTMLButtonElement>(null);
  const picked = filters.from !== null;

  useEffect(() => {
    if (open) {
      doneRef.current?.focus();
    }
  }, [open]);

  // A ticked service may have no traces in the window; keep it listed so it can be unticked.
  const listed = [
    ...services,
    ...filters.services
      .filter((name) => !services.some((service) => service.service === name))
      .map((service) => ({ service, traces: 0 })),
  ];
  const shown = showAll ? listed : listed.slice(0, collapsedServiceCount);
  const shows: { value: TraceShow; label: string; count: number | null }[] = [
    { value: "all", label: "All", count: null },
    { value: "failed", label: "Failed", count: failedCount },
    { value: "slow", label: "Slow", count: slowCount },
  ];

  function toggleService(name: string) {
    onChange({
      ...filters,
      services: filters.services.includes(name)
        ? filters.services.filter((service) => service !== name)
        : [...filters.services, name],
    });
  }

  function duration(text: string): number | null {
    const value = Number(text);

    return text.trim() === "" || !Number.isFinite(value) || value < 0 ? null : value;
  }

  return (
    <aside className={open ? "filters open" : "filters"} aria-label="Filters">
      <div className="filters-sheet-bar">
        <h2>Filters</h2>
        <button ref={doneRef} type="button" className="button primary" onClick={onClose}>
          Done
        </button>
      </div>

      <section className="filter-group" aria-labelledby="trace-filter-time">
        <h3 id="trace-filter-time" className="section-title">
          Time
        </h3>
        <div className="segmented">
          {traceRanges.map((range) => (
            <button
              key={range}
              type="button"
              className="chip-button"
              aria-pressed={!picked && filters.range === range}
              onClick={() => onChange({ ...filters, range, from: null, to: null })}
            >
              {range}
            </button>
          ))}
        </div>
        <p className="filter-note">
          {formatClock(from)} – {formatClock(to)} {zoneLabel(to)}
          {picked && (
            <>
              {" · "}
              <button
                type="button"
                className="link-button"
                onClick={() => onChange({ ...filters, from: null, to: null, minMs: null, maxMs: null })}
              >
                Back to the last {filters.range}
              </button>
            </>
          )}
        </p>
      </section>

      <section className="filter-group" aria-labelledby="trace-filter-show">
        <h3 id="trace-filter-show" className="section-title">
          Show
        </h3>
        <div className="chip-row">
          {shows.map((show) => (
            <button
              key={show.value}
              type="button"
              className={`chip-button trace-show-${show.value}`}
              aria-pressed={filters.show === show.value}
              onClick={() => onChange({ ...filters, show: show.value })}
            >
              {show.label}
              {show.count !== null && ` · ${formatCount(show.count)}`}
            </button>
          ))}
        </div>
        {filters.show === "slow" && <p className="filter-note">As slow as the slowest 5% of the window.</p>}
      </section>

      {(advanced || filters.minMs !== null || filters.maxMs !== null) && (
        <section className="filter-group" aria-labelledby="trace-filter-duration">
          <h3 id="trace-filter-duration" className="section-title">
            Duration in ms
          </h3>
          <div className="trace-duration-fields">
            <label>
              <span className="visually-hidden">At least, in milliseconds</span>
              <input
                key={`min-${filters.minMs}`}
                type="number"
                min={0}
                inputMode="decimal"
                placeholder="at least"
                defaultValue={filters.minMs ?? ""}
                onBlur={(event) => onChange({ ...filters, minMs: duration(event.target.value) })}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    onChange({ ...filters, minMs: duration(event.currentTarget.value) });
                  }
                }}
              />
            </label>
            <span aria-hidden="true">–</span>
            <label>
              <span className="visually-hidden">At most, in milliseconds</span>
              <input
                key={`max-${filters.maxMs}`}
                type="number"
                min={0}
                inputMode="decimal"
                placeholder="any"
                defaultValue={filters.maxMs ?? ""}
                onBlur={(event) => onChange({ ...filters, maxMs: duration(event.target.value) })}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    onChange({ ...filters, maxMs: duration(event.currentTarget.value) });
                  }
                }}
              />
            </label>
          </div>
        </section>
      )}

      <section className="filter-group" aria-labelledby="trace-filter-services">
        <h3 id="trace-filter-services" className="section-title">
          Services
        </h3>
        {listed.length === 0 && <p className="filter-note">No traces in this window.</p>}
        <ul className="service-list">
          {shown.map((service) => (
            <li key={service.service}>
              <label>
                <input
                  type="checkbox"
                  checked={filters.services.includes(service.service)}
                  onChange={() => toggleService(service.service)}
                />
                <i className="trace-swatch" style={{ background: serviceColor(service.service) }} aria-hidden="true" />
                <span className="service-name">{service.service}</span>
                <span className="service-count">{formatCount(service.traces)}</span>
              </label>
            </li>
          ))}
        </ul>
        {listed.length > collapsedServiceCount && (
          <button type="button" className="link-button" onClick={() => setShowAll(!showAll)}>
            {showAll ? "Show fewer" : `Show all ${listed.length}`}
          </button>
        )}
      </section>

      {(advanced || filters.attributes.length > 0) && (
        <section className="filter-group" aria-labelledby="trace-filter-attributes">
          <h3 id="trace-filter-attributes" className="section-title">
            Attributes
          </h3>
          <AttributeFilters
            attributes={filters.attributes}
            onChange={(attributes) => onChange({ ...filters, attributes })}
          />
        </section>
      )}
    </aside>
  );
}
