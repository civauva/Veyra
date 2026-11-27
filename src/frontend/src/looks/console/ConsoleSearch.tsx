import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { useNavigate, useSearchParams } from "react-router";

import type { SearchItem } from "../../api/types";
import type { TimeChoice } from "../../app/time";
import { carryTime } from "../../time/carryTime";
import { useSearch } from "../../app/useSearch";
import { termText } from "../../logs/queryText";
import { ConsoleIcon, type ConsoleIconName } from "./ConsoleIcon";

/** Props for {@link ConsoleSearch}. */
interface ConsoleSearchProps {
  /** The chosen environment, or null for all. */
  environment: string | null;
  /** The shared range the search looks in. */
  time: TimeChoice;
}

/** A line of the results: where it leads, and what it shows. */
interface Line {
  /** Its key among the lines. */
  key: string;
  /** Its picture. */
  icon: ConsoleIconName;
  /** Its name. */
  name: string;
  /** Its short figure. */
  detail: string;
  /** Where it leads, without the range. */
  path: string;
}

const icons: Record<SearchItem["kind"], ConsoleIconName> = {
  service: "services",
  host: "hosts",
  container: "container",
  server: "database",
  database: "database",
  operation: "traces",
};

/** The screen a found thing opens. */
function pathOf(item: SearchItem): string {
  const part = encodeURIComponent;

  switch (item.kind) {
    case "service":
      return `/metrics/services/${part(item.name)}`;
    case "host":
      return `/metrics/hosts/${part(item.name)}`;
    case "container":
      return `/metrics?search=${part(item.name)}`;
    case "server":
      return `/metrics/databases/${part(item.engine ?? "")}/${part(item.server ?? item.name)}`;
    case "database":
      return `/metrics/databases/${part(item.engine ?? "")}/${part(item.server ?? "")}?tab=databases`;
    case "operation":
      return `/traces?name=${part(item.name)}`;
  }
}

/**
 * The search of the Console top bar (ADR 0022): as the person types, the server searches services, hosts and
 * containers, databases, trace operations, and logs at once, and ranks what it finds. The arrow keys move through the
 * results and Enter opens one; Enter on the words alone opens the Log Explorer with them. Ctrl K or ⌘ K focuses it.
 */
export function ConsoleSearch({ environment, time }: ConsoleSearchProps) {
  const [words, setWords] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const inputRef = useRef<HTMLInputElement>(null);
  const rootRef = useRef<HTMLFormElement>(null);
  const listId = useId();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { results, searching, error } = useSearch(words, environment, time);
  const query = words.trim();

  useEffect(() => {
    function keyed(event: globalThis.KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        inputRef.current?.focus();
        setOpen(true);
      }
    }

    function pressed(event: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    document.addEventListener("keydown", keyed);
    document.addEventListener("mousedown", pressed);

    return () => {
      document.removeEventListener("keydown", keyed);
      document.removeEventListener("mousedown", pressed);
    };
  }, []);

  const groups: { title: string; lines: Line[] }[] = results
    ? [
        { title: "Services", items: results.services.items },
        { title: "Hosts & containers", items: results.hosts.items },
        { title: "Databases", items: results.databases.items },
        { title: "Traces", items: results.traces.items },
      ]
        .map((group) => ({
          title: group.title,
          lines: group.items.map((item, index) => ({
            key: `${group.title}-${index}`,
            icon: icons[item.kind],
            name: item.kind === "database" && item.server ? `${item.name} on ${item.server}` : item.name,
            detail: item.detail,
            path: pathOf(item),
          })),
        }))
        .filter((group) => group.lines.length > 0)
    : [];

  const logLine: Line | null = query
    ? {
        key: "logs",
        icon: "logs",
        name: `Messages with “${query}”`,
        detail: results ? `${results.logs.count.toLocaleString("en")} in the range` : "Counting…",
        // Console's Log explorer takes the words as a query (ADR 0024), the words of the message.
        path: `/logs?q=${encodeURIComponent(termText("text", query))}`,
      }
    : null;

  const lines = [...groups.flatMap((group) => group.lines), ...(logLine ? [logLine] : [])];
  const shown = open && query !== "";

  function go(line: Line) {
    const target = carryTime(line.path.split("?")[0] ?? line.path, params);
    const own = line.path.includes("?") ? line.path.slice(line.path.indexOf("?") + 1) : "";
    void navigate(own ? `${target}${target.includes("?") ? "&" : "?"}${own}` : target);
    setWords("");
    setOpen(false);
    setActive(-1);
    inputRef.current?.blur();
  }

  function submitted(event: FormEvent) {
    event.preventDefault();

    const line = active >= 0 ? lines[active] : logLine;

    if (line) {
      go(line);
    }
  }

  function moved(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      setOpen(false);

      return;
    }

    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
      return;
    }

    event.preventDefault();
    setOpen(true);
    setActive((at) => {
      const count = lines.length;

      if (count === 0) {
        return -1;
      }

      return event.key === "ArrowDown" ? (at + 1) % count : (at - 1 + count) % count;
    });
  }

  let index = -1;

  return (
    <form ref={rootRef} className="cn-search" role="search" onSubmit={submitted}>
      <ConsoleIcon name="search" size={15} />
      <input
        ref={inputRef}
        type="search"
        aria-label="Search logs, traces, services, hosts, and databases"
        placeholder="Search logs, traces, services, hosts, databases…"
        role="combobox"
        aria-expanded={shown}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={shown && active >= 0 ? `${listId}-${active}` : undefined}
        value={words}
        onFocus={() => setOpen(true)}
        onChange={(event) => {
          setWords(event.target.value);
          setOpen(true);
          setActive(-1);
        }}
        onKeyDown={moved}
      />
      <kbd aria-hidden="true">Ctrl K</kbd>
      {shown && (
        <div className="cn-search-results">
          <ul id={listId} role="listbox" aria-label="Search results">
            {groups.map((group) => (
              <li key={group.title} role="presentation">
                <p className="cn-menu-cap" aria-hidden="true">
                  {group.title}
                </p>
                <ul role="group" aria-label={group.title}>
                  {group.lines.map((line) => {
                    index += 1;
                    const at = index;

                    return (
                      <li
                        key={line.key}
                        id={`${listId}-${at}`}
                        role="option"
                        aria-selected={active === at}
                        className="cn-search-line"
                        onMouseDown={(event) => event.preventDefault()}
                        onClick={() => go(line)}
                      >
                        <ConsoleIcon name={line.icon} size={14} />
                        <span>{line.name}</span>
                        <small>{line.detail}</small>
                      </li>
                    );
                  })}
                </ul>
              </li>
            ))}
            {logLine && (
              <li role="presentation">
                <p className="cn-menu-cap" aria-hidden="true">
                  Logs
                </p>
                <ul role="group" aria-label="Logs">
                  <li
                    id={`${listId}-${lines.length - 1}`}
                    role="option"
                    aria-selected={active === lines.length - 1}
                    className="cn-search-line"
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => go(logLine)}
                  >
                    <ConsoleIcon name="logs" size={14} />
                    <span>{logLine.name}</span>
                    <small>{logLine.detail}</small>
                  </li>
                </ul>
              </li>
            )}
          </ul>
          <p className="cn-search-foot">
            {error ?? (searching ? "Searching…" : "↑ ↓ to move · Enter to open · matched and ranked by the server")}
          </p>
        </div>
      )}
    </form>
  );
}
