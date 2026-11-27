import { useLayoutEffect, useRef } from "react";

import { ConsoleIcon, type ConsoleIconName } from "./ConsoleIcon";
import { ConsoleMenu } from "./ConsoleMenu";

/** Where the guides live: the project's documentation. */
export const docs = "https://github.com/civauva/Veyra/blob/main/docs";

/** The guides the help menu leads to. */
const guides: { label: string; icon: ConsoleIconName; href: string }[] = [
  { label: "Installing and running Veyra", icon: "book", href: `${docs}/guides/deployment.md` },
  { label: "SQL Server guide", icon: "database", href: `${docs}/integrations/sqlserver.md` },
  { label: "Agents guide", icon: "hosts", href: `${docs}/integrations/agents.md` },
  { label: "Alert rules explained", icon: "alerts", href: `${docs}/architecture/alerting.md` },
];

/** The keys the Console look answers to, as the help menu and the shortcuts dialog list them. */
export const shortcuts: { keys: string; what: string }[] = [
  { keys: "Ctrl K", what: "Search" },
  { keys: "T", what: "Time range" },
  { keys: "L", what: "Live on or off" },
  { keys: "?", what: "Keyboard shortcuts" },
  { keys: "Esc", what: "Close a menu or panel" },
];

/** Props for {@link ConsoleHelp}. */
interface ConsoleHelpProps {
  /** Opens the keyboard shortcuts. */
  onShortcuts(): void;
}

/** Help at the top bar: the keyboard shortcuts, and the guides for installing, SQL Server, agents, and alert rules. */
export function ConsoleHelp({ onShortcuts }: ConsoleHelpProps) {
  return (
    <ConsoleMenu
      button={<ConsoleIcon name="help" size={16} />}
      label="Help"
      buttonClass="cn-icon"
      wrapClass="cn-help"
      menuLabel="Help"
      menuClass="cn-menu-help"
    >
      {(close) => (
        <>
          <p className="cn-menu-cap">Help</p>
          <button
            type="button"
            className="cn-menu-option"
            onClick={() => {
              close();
              onShortcuts();
            }}
          >
            <ConsoleIcon name="keyboard" size={14} /> Keyboard shortcuts <kbd>?</kbd>
          </button>
          {guides.map((guide) => (
            <a key={guide.href} className="cn-menu-option" href={guide.href} target="_blank" rel="noreferrer">
              <ConsoleIcon name={guide.icon} size={14} /> {guide.label}
            </a>
          ))}
        </>
      )}
    </ConsoleMenu>
  );
}

/** Props for {@link ShortcutsDialog}. */
interface ShortcutsDialogProps {
  /** Closes it. */
  onClose(): void;
}

/** The keyboard shortcuts, over the screen. Escape, its button, or a click beside it closes it. */
export function ShortcutsDialog({ onClose }: ShortcutsDialogProps) {
  const closeRef = useRef<HTMLButtonElement>(null);

  // Before it is painted, so a key pressed as soon as it shows already reaches it.
  useLayoutEffect(() => {
    closeRef.current?.focus();

    function keyed(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    document.addEventListener("keydown", keyed);

    return () => document.removeEventListener("keydown", keyed);
  }, [onClose]);

  return (
    <div className="cn-dialog-scrim" onClick={onClose}>
      <div
        className="cn-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="cn-shortcuts-title"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="cn-shortcuts-title">Keyboard shortcuts</h2>
        <dl>
          {shortcuts.map((shortcut) => (
            <div key={shortcut.keys}>
              <dt>{shortcut.what}</dt>
              <dd>
                <kbd>{shortcut.keys}</kbd>
              </dd>
            </div>
          ))}
        </dl>
        <button ref={closeRef} type="button" className="cn-button" onClick={onClose}>
          Close
        </button>
      </div>
    </div>
  );
}
