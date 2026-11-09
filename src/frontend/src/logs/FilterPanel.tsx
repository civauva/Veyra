import { useEffect, useRef, useState, type FormEvent } from "react";

import { selectableSeverities, type ServiceCount } from "../api/types";
import { formatClock, formatCount, formatTime } from "./format";
import { timeRanges, type LogFilters } from "./logFilters";
import { severityLabels } from "./SeverityBadge";

/** How many services show before "Show all". */
const collapsedServiceCount = 7;

/** Props for {@link FilterPanel}. */
interface FilterPanelProps {
  /** The current filters. */
  filters: LogFilters;
  /** Called with the new filters when the person changes one. */
  onChange: (filters: LogFilters) => void;
  /** The busiest services in the window, with their counts. */
  services: ServiceCount[];
  /** The start of the searched window, shown under the time choices. */
  from: Date;
  /** The end of the searched window. */
  to: Date;
  /** On phones the panel is a sheet; whether it is open. Wider screens always show it. */
  open: boolean;
  /** Closes the sheet on phones. */
  onClose: () => void;
}

/**
 * The filter column of the Log Explorer: time, minimum severity, services, attributes, and trace. Every change
 * applies at once; on phones the panel opens as a sheet from the toolbar's Filters button.
 */
export function FilterPanel({ filters, onChange, services, from, to, open, onClose }: FilterPanelProps) {
  const [showAllServices, setShowAllServices] = useState(false);
  const doneRef = useRef<HTMLButtonElement>(null);

  // The sheet covers the screen on phones, so the keyboard moves into it when it opens.
  useEffect(() => {
    if (open) {
      doneRef.current?.focus();
    }
  }, [open]);

  // A ticked service may have no records in the window; keep it listed so it can be unticked.
  const listed = [
    ...services,
    ...filters.services
      .filter((name) => !services.some((service) => service.name === name))
      .map((name) => ({ name, count: 0 })),
  ];
  const shown = showAllServices ? listed : listed.slice(0, collapsedServiceCount);

  function toggleService(name: string) {
    const selected = filters.services.includes(name)
      ? filters.services.filter((service) => service !== name)
      : [...filters.services, name];

    onChange({ ...filters, services: selected });
  }

  return (
    <aside className={open ? "filters open" : "filters"} aria-label="Filters">
      <div className="filters-sheet-bar">
        <h2>Filters</h2>
        <button ref={doneRef} type="button" className="button primary" onClick={onClose}>
          Done
        </button>
      </div>

      <section className="filter-group" aria-labelledby="filter-time">
        <h3 id="filter-time" className="section-title">
          Time
        </h3>
        <div className="segmented">
          {timeRanges.map((range) => (
            <button
              key={range}
              type="button"
              className="chip-button"
              aria-pressed={!filters.around && filters.range === range}
              onClick={() => onChange({ ...filters, range, around: null })}
            >
              {range}
            </button>
          ))}
        </div>
        {filters.around ? (
          <p className="filter-note">
            ± 1 minute around {formatTime(filters.around)} UTC.{" "}
            <button type="button" className="link-button" onClick={() => onChange({ ...filters, around: null })}>
              Back to the last {filters.range}
            </button>
          </p>
        ) : (
          <p className="filter-note">
            {formatClock(from)} – {formatClock(to)} UTC
          </p>
        )}
      </section>

      <section className="filter-group" aria-labelledby="filter-severity">
        <h3 id="filter-severity" className="section-title">
          At least
        </h3>
        <div className="chip-row">
          {selectableSeverities.map((severity) => (
            <button
              key={severity}
              type="button"
              className="chip-button"
              aria-pressed={filters.minimumSeverity === severity}
              onClick={() =>
                onChange({ ...filters, minimumSeverity: filters.minimumSeverity === severity ? null : severity })
              }
            >
              {severityLabels[severity]}
            </button>
          ))}
        </div>
      </section>

      <section className="filter-group" aria-labelledby="filter-services">
        <h3 id="filter-services" className="section-title">
          Services
        </h3>
        {listed.length === 0 && <p className="filter-note">No services in this window.</p>}
        <ul className="service-list">
          {shown.map((service) => (
            <li key={service.name}>
              <label>
                <input
                  type="checkbox"
                  checked={filters.services.includes(service.name)}
                  onChange={() => toggleService(service.name)}
                />
                <span className="service-name">{service.name}</span>
                <span className="service-count">{formatCount(service.count)}</span>
              </label>
            </li>
          ))}
        </ul>
        {listed.length > collapsedServiceCount && (
          <button type="button" className="link-button" onClick={() => setShowAllServices(!showAllServices)}>
            {showAllServices ? "Show fewer" : `Show all ${listed.length}`}
          </button>
        )}
      </section>

      <section className="filter-group" aria-labelledby="filter-attributes">
        <h3 id="filter-attributes" className="section-title">
          Attributes
        </h3>
        <AttributeFilters filters={filters} onChange={onChange} />
      </section>

      <section className="filter-group" aria-labelledby="filter-trace">
        <h3 id="filter-trace" className="section-title">
          Trace
        </h3>
        {filters.traceId ? (
          <span className="filter-chip">
            <span className="mono">{filters.traceId}</span>
            <button
              type="button"
              className="chip-remove"
              aria-label="Clear the trace filter"
              onClick={() => onChange({ ...filters, traceId: null })}
            >
              ×
            </button>
          </span>
        ) : (
          <p className="filter-note">Select a record with a trace, then choose &quot;Same trace&quot;.</p>
        )}
      </section>
    </aside>
  );
}

/** Props for {@link AttributeFilters}. */
interface AttributeFiltersProps {
  /** The current filters. */
  filters: LogFilters;
  /** Called with the new filters. */
  onChange: (filters: LogFilters) => void;
}

/** The attribute filters as removable chips, and a small form to add one. */
function AttributeFilters({ filters, onChange }: AttributeFiltersProps) {
  const [adding, setAdding] = useState(false);
  const [key, setKey] = useState("");
  const [value, setValue] = useState("");

  function add(event: FormEvent) {
    event.preventDefault();
    onChange({ ...filters, attributes: [...filters.attributes, { key: key.trim(), value }] });
    setKey("");
    setValue("");
    setAdding(false);
  }

  return (
    <>
      {filters.attributes.map((attribute, index) => (
        <span key={`${attribute.key}=${attribute.value}`} className="filter-chip">
          <span className="mono">
            {attribute.key} = {attribute.value}
          </span>
          <button
            type="button"
            className="chip-remove"
            aria-label={`Remove ${attribute.key} = ${attribute.value}`}
            onClick={() => onChange({ ...filters, attributes: filters.attributes.filter((_, at) => at !== index) })}
          >
            ×
          </button>
        </span>
      ))}
      {adding ? (
        <form className="attribute-form" onSubmit={add}>
          <label>
            Attribute
            <input
              required
              className="mono"
              placeholder="http.route"
              value={key}
              onChange={(e) => setKey(e.target.value)}
            />
          </label>
          <label>
            Value
            <input required className="mono" value={value} onChange={(e) => setValue(e.target.value)} />
          </label>
          <div className="attribute-form-actions">
            <button type="submit" className="button primary">
              Add
            </button>
            <button type="button" className="button" onClick={() => setAdding(false)}>
              Cancel
            </button>
          </div>
        </form>
      ) : (
        <button type="button" className="link-button" onClick={() => setAdding(true)}>
          <span aria-hidden="true">+ </span>
          Add attribute filter
        </button>
      )}
    </>
  );
}
