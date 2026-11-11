import { useState } from "react";

/** Props for {@link MadePassword}. */
interface MadePasswordProps {
  /** The password, words joined by dashes. */
  value: string;
  /** Makes other words. */
  onOther(): void;
  /** What a screen reader calls it, for example "First password". */
  label: string;
  /** The line under the words. */
  note: string;
}

/** A password Veyra made up, shown once as separate words with copy and "other words". */
export function MadePassword({ value, onOther, label, note }: MadePasswordProps) {
  const [copied, setCopied] = useState(false);
  const words = value.split("-");

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <div className="made-password">
      <div className="made-words" aria-label={label} role="group">
        {words.map((word, index) => (
          <span key={`${word}-${index}`}>
            {index > 0 && <em>-</em>}
            <code>{word}</code>
          </span>
        ))}
      </div>
      <div className="made-actions">
        <button type="button" className="button small" onClick={() => void copy()}>
          {copied ? "Copied" : "Copy"}
        </button>
        <button
          type="button"
          className="button small quiet"
          onClick={() => {
            setCopied(false);
            onOther();
          }}
        >
          Other words
        </button>
      </div>
      <small>{note}</small>
    </div>
  );
}
