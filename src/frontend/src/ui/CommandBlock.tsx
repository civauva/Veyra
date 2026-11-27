import { useState } from "react";

import "./commandBlock.css";

/** Props for {@link CommandBlock}. */
export interface CommandBlockProps {
  /** What the command does, such as "Start it". It names the block and its Copy button for screen readers. */
  label: string;
  /** The command itself. */
  text: string;
}

/**
 * A command or script to run, in monospace with a Copy button, as the Agents page and an alert's fix steps show them.
 *
 * @param props - The command and what it does.
 * @returns The block.
 */
export function CommandBlock({ label, text }: CommandBlockProps) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2_000);
    } catch {
      setCopied(false);
    }
  }

  return (
    <div className="command-block">
      <button type="button" className="link-button" aria-label={`Copy: ${label}`} onClick={() => void copy()}>
        {copied ? "Copied" : "Copy"}
      </button>
      <pre aria-label={label}>{text}</pre>
    </div>
  );
}
