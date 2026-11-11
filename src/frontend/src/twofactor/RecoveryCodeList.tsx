import { useId, useState } from "react";

/** Props for {@link RecoveryCodeList}. */
interface RecoveryCodeListProps {
  /** The codes, shown once. */
  codes: string[];
  /** The account they belong to, for the downloaded file. */
  account: string;
  /** Called when the person confirms they stored the codes. */
  onDone(): void;
  /** The text of the button that closes the list. */
  doneLabel?: string;
}

/**
 * Ten recovery codes, with copy and download, and a check that the person stored them before the list goes away for
 * good.
 */
export function RecoveryCodeList({ codes, account, onDone, doneLabel = "Done" }: RecoveryCodeListProps) {
  const checkId = useId();
  const [stored, setStored] = useState(false);
  const [copied, setCopied] = useState(false);
  const text = `Veyra recovery codes for ${account}\nEach code signs you in once instead of the app.\n\n${codes.join("\n")}\n`;

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
    } catch {
      // Some browsers refuse the clipboard; the codes stay on screen to copy by hand.
      setCopied(false);
    }
  }

  function download() {
    const url = URL.createObjectURL(new Blob([text], { type: "text/plain" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = "veyra-recovery-codes.txt";
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div className="recovery">
      <p className="recovery-lead">
        If you lose your phone, each code signs you in once instead of the app. Keep them in a password manager or on
        paper. Veyra shows them only now.
      </p>
      <ul className="recovery-codes" aria-label="Recovery codes">
        {codes.map((code) => (
          <li key={code}>
            <code>{code}</code>
          </li>
        ))}
      </ul>
      <div className="recovery-actions">
        <button type="button" className="button" onClick={() => void copy()}>
          {copied ? "Copied" : "Copy all"}
        </button>
        <button type="button" className="button" onClick={download}>
          Download .txt
        </button>
      </div>
      <label className="recovery-check" htmlFor={checkId}>
        <input id={checkId} type="checkbox" checked={stored} onChange={(event) => setStored(event.target.checked)} />I
        have stored these codes somewhere safe
      </label>
      <div className="twofactor-foot">
        <button type="button" className="button primary" disabled={!stored} onClick={onDone}>
          {doneLabel}
        </button>
      </div>
    </div>
  );
}
