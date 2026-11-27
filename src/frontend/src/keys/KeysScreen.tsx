import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";

import { api } from "../api/client";
import type { CreatedIngestionKey, IngestionKey } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { ScreenBar } from "../ui/ScreenBar";
import { endOfLocalDay, localDay } from "../time/zone";
import "./keys.css";

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** A date for the key table, for example `2026-11-09`, on the person's calendar (VEY-22). */
function formatDate(instant: string | null, missing: string): string {
  return instant ? localDay(instant) : missing;
}

/** Whether a key still works, in the words the table shows. */
function stateOf(key: IngestionKey, now: number): "Active" | "Revoked" | "Expired" {
  if (key.revokedAt) {
    return "Revoked";
  }

  return key.expiresAt && Date.parse(key.expiresAt) <= now ? "Expired" : "Active";
}

/**
 * Ingestion keys (ADR 0008), for administrators: the keys senders present, a form to create one, and revoking.
 * A new key's secret is shown once, right after creating it, because Veyra stores only its hash.
 */
export function KeysScreen() {
  const [keys, setKeys] = useState<IngestionKey[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedIngestionKey | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [now] = useState(() => Date.now());

  const reload = useCallback(() => {
    api
      .listKeys()
      .then((list) => {
        setKeys(list);
        setLoadError(null);
      })
      .catch((failure: unknown) => setLoadError(messageOf(failure)));
  }, []);

  useEffect(reload, [reload]);

  async function revoke(id: string) {
    setActionError(null);

    try {
      await api.revokeKey(id);
      setConfirming(null);
      reload();
    } catch (failure) {
      setActionError(messageOf(failure));
    }
  }

  return (
    <main className="keys">
      <ScreenBar trail={[{ label: "Ingestion keys" }]} />
      <h1>Ingestion keys</h1>
      <p className="keys-intro">
        Every sender, such as an OpenTelemetry Collector or an application, presents a key in the{" "}
        <code>Authorization: Bearer</code> header. Give each sender its own key, so one can be revoked without touching
        the others.
      </p>

      {created ? (
        <NewKey created={created} onDone={() => setCreated(null)} />
      ) : (
        <CreateKeyForm
          onCreated={(key) => {
            setCreated(key);
            reload();
          }}
        />
      )}

      {loadError && (
        <p className="keys-error" role="alert">
          {loadError}
        </p>
      )}
      {actionError && (
        <p className="keys-error" role="alert">
          {actionError}
        </p>
      )}

      {keys && keys.length === 0 && <p className="keys-empty">No keys yet. Create one for your first sender.</p>}
      {keys && keys.length > 0 && (
        // Phones show each key as a card without the table's headings, so one question mark explains its fields.
        <p className="keys-phone-help">
          <ColumnName name="a key's fields">
            Key: its first characters, to tell keys apart; Veyra keeps only a hash of the whole key. Environments: the
            environments it may send, others are refused. Last used: the day a sender last presented it; an old date may
            mean a key nobody needs. State: active keys work, revoked and expired ones never work again.
          </ColumnName>
        </p>
      )}
      {keys && keys.length > 0 && (
        <table className="keys-table">
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">
                <ColumnName name="Key">
                  The first characters of the key, to tell keys apart and to find one in a sender&apos;s settings. Veyra
                  keeps only a hash of the whole key, so it cannot show it again.
                </ColumnName>
              </th>
              <th scope="col">
                <ColumnName name="Environments">
                  The environments whose telemetry the key may send, by deployment.environment.name. Records of any
                  other are refused, and the sender is told. All means any environment.
                </ColumnName>
              </th>
              <th scope="col">Created</th>
              <th scope="col">
                <ColumnName name="Last used">
                  The day a sender last presented the key. Never means no sender has used it yet; an old date may mean a
                  sender stopped, or a key nobody needs that you can revoke.
                </ColumnName>
              </th>
              <th scope="col">Expires</th>
              <th scope="col">
                <ColumnName name="State">
                  Active keys work. A revoked key stops within 30 seconds, and an expired one after its last day; both
                  stay listed and cannot work again, so give the sender a new key.
                </ColumnName>
              </th>
              <th scope="col">
                <span className="visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {keys.map((key) => {
              const state = stateOf(key, now);

              return (
                <tr key={key.id} className={state === "Active" ? undefined : "keys-inactive"}>
                  <th scope="row">{key.name}</th>
                  <td data-label="Key" className="mono">
                    {key.displayPrefix}…
                  </td>
                  <td data-label="Environments">{key.environments?.join(", ") ?? "All"}</td>
                  <td data-label="Created">{formatDate(key.createdAt, "")}</td>
                  <td data-label="Last used">{formatDate(key.lastUsedAt, "Never")}</td>
                  <td data-label="Expires">{formatDate(key.expiresAt, "Never")}</td>
                  <td data-label="State">
                    <span className={`key-state key-state-${state.toLowerCase()}`}>{state}</span>
                  </td>
                  <td className="keys-actions">
                    {state === "Active" &&
                      (confirming === key.id ? (
                        <span className="keys-confirm">
                          Senders using it stop within 30 seconds.
                          <button type="button" className="button danger" onClick={() => void revoke(key.id)}>
                            Yes, revoke
                          </button>
                          <button type="button" className="button" onClick={() => setConfirming(null)}>
                            Cancel
                          </button>
                        </span>
                      ) : (
                        <button
                          type="button"
                          className="button"
                          aria-label={`Revoke ${key.name}`}
                          onClick={() => setConfirming(key.id)}
                        >
                          Revoke
                        </button>
                      ))}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </main>
  );
}

/** Props for {@link ColumnName}. */
interface ColumnNameProps {
  /** The column's name, such as "Last used". */
  name: string;
  /** What the column means, behind the question mark. */
  children: ReactNode;
}

/** A column's name with a question mark that explains it (VEY-60). */
function ColumnName({ name, children }: ColumnNameProps) {
  return (
    <span className="keys-column">
      {name}
      <HelpTip label={name}>{children}</HelpTip>
    </span>
  );
}

/** Props for {@link CreateKeyForm}. */
interface CreateKeyFormProps {
  /** Called with the new key and its secret. */
  onCreated: (key: CreatedIngestionKey) => void;
}

/** The form that creates a key: a name, optional environments, and an optional expiry date. */
function CreateKeyForm({ onCreated }: CreateKeyFormProps) {
  const [name, setName] = useState("");
  const [environments, setEnvironments] = useState("");
  const [expires, setExpires] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    const list = environments
      .split(",")
      .map((environment) => environment.trim())
      .filter((environment) => environment.length > 0);

    try {
      const key = await api.createKey({
        name: name.trim(),
        environments: list.length > 0 ? list : null,
        // A date means "works through that day", so the key expires at its last second in the person's zone.
        expiresAt: expires ? new Date(endOfLocalDay(expires)).toISOString() : null,
      });
      onCreated(key);
    } catch (failure) {
      setError(messageOf(failure));
      setBusy(false);
    }
  }

  return (
    <form className="keys-form" onSubmit={(event) => void submit(event)} aria-label="Create a key">
      <label>
        Name
        <input
          required
          maxLength={200}
          placeholder="web-01 collector"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
      </label>
      <label>
        Environments <span className="keys-optional">(optional)</span>
        <input
          placeholder="All, or production, staging"
          value={environments}
          onChange={(e) => setEnvironments(e.target.value)}
        />
      </label>
      <label>
        Expires <span className="keys-optional">(optional)</span>
        <input type="date" value={expires} onChange={(e) => setExpires(e.target.value)} />
      </label>
      <button type="submit" className="button primary" disabled={busy}>
        Create key
      </button>
      {error && (
        <p className="keys-error" role="alert">
          {error}
        </p>
      )}
    </form>
  );
}

/** Props for {@link NewKey}. */
interface NewKeyProps {
  /** The key just created, with its secret. */
  created: CreatedIngestionKey;
  /** Hides the secret for good. */
  onDone: () => void;
}

/** The secret of a key just created, with a copy button. Veyra cannot show it again. */
function NewKey({ created, onDone }: NewKeyProps) {
  const [copied, setCopied] = useState<"idle" | "copied" | "failed">("idle");

  async function copy() {
    try {
      // The clipboard exists only in secure contexts, so plain-HTTP installations land in the catch.
      await navigator.clipboard.writeText(created.secret);
      setCopied("copied");
    } catch {
      setCopied("failed");
    }
  }

  return (
    <section className="keys-new" aria-label="New key">
      <h2>{created.name}</h2>
      <p>Copy the key now and configure it in the sender. Veyra stores only its hash and cannot show it again.</p>
      <code className="keys-secret">{created.secret}</code>
      <div className="keys-new-actions">
        <button type="button" className="button" onClick={() => void copy()}>
          Copy
        </button>
        <button type="button" className="button primary" onClick={onDone}>
          I have copied it
        </button>
        <span role="status" className="keys-copied">
          {copied === "copied" ? "Copied" : copied === "failed" ? "Copying needs HTTPS; select the key instead" : ""}
        </span>
      </div>
    </section>
  );
}
