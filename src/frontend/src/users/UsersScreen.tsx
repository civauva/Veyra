import { useCallback, useEffect, useId, useState } from "react";
import { useSearchParams } from "react-router";

import { api } from "../api/client";
import type { SecondFactorPolicy, UserSummary } from "../api/types";
import { useSession } from "../session/useSession";
import { AddUserPanel } from "./AddUserPanel";
import { PersonBadge } from "./PersonBadge";
import { PersonPanel, type PersonTab } from "./PersonPanel";
import { Posture } from "./Posture";
import { RoleBar } from "./RoleBar";
import "./users.css";

/** Which people the badge grid shows. */
type Show = "everyone" | "nofactor" | "locked" | "off";

const tabs: PersonTab[] = ["overview", "signins", "sessions", "history"];

/**
 * Who can sign in, how far in they reach, and how well their accounts are protected (ADR 0011): a security strip
 * with the second-factor rule, the roles as one bar, a badge per person, and the selected person in a panel. The
 * selected person and tab live in the URL, so a link opens the same view.
 */
export function UsersScreen() {
  const { state } = useSession();
  const myId = state.status === "signedIn" ? state.session.id : null;
  const [params, setParams] = useSearchParams();
  const selected = params.get("person");
  const tabParam = params.get("tab");
  const tab: PersonTab = tabs.includes(tabParam as PersonTab) ? (tabParam as PersonTab) : "overview";
  const filterName = useId();
  const [people, setPeople] = useState<UserSummary[] | null>(null);
  const [policy, setPolicy] = useState<SecondFactorPolicy | null>(null);
  const [search, setSearch] = useState("");
  const [show, setShow] = useState<Show>("everyone");
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const now = new Date();

  const load = useCallback(
    () =>
      Promise.all([api.listUsers(), api.secondFactorPolicy()])
        .then(([list, current]) => {
          setPeople(list);
          setPolicy(current.requiredFor);
          setError(null);
        })
        .catch((failure: unknown) => setError(messageOf(failure))),
    [],
  );

  useEffect(() => {
    void load();
  }, [load]);

  function open(id: string | null, nextTab: PersonTab = "overview") {
    const changed = new URLSearchParams(params);

    if (id) {
      changed.set("person", id);
    } else {
      changed.delete("person");
    }

    if (id && nextTab !== "overview") {
      changed.set("tab", nextTab);
    } else {
      changed.delete("tab");
    }

    setParams(changed, { replace: true });
  }

  async function choosePolicy(next: SecondFactorPolicy) {
    const before = policy;
    setPolicy(next);

    try {
      setPolicy((await api.setSecondFactorPolicy(next)).requiredFor);
    } catch (failure) {
      setPolicy(before);
      setError(messageOf(failure));
    }
  }

  const needle = search.trim().toLowerCase();
  const shown = (people ?? []).filter((person) => {
    const matches =
      !needle ||
      person.displayName.toLowerCase().includes(needle) ||
      person.email.toLowerCase().includes(needle) ||
      (person.team ?? "").toLowerCase().includes(needle);
    const locked = person.lockedUntil !== null && new Date(person.lockedUntil) > now;

    return (
      matches &&
      (show === "everyone" ||
        (show === "nofactor" && !person.secondFactorSince) ||
        (show === "locked" && locked) ||
        (show === "off" && person.disabled))
    );
  });
  const counts: Record<Show, number> = {
    everyone: people?.length ?? 0,
    nofactor: (people ?? []).filter((person) => !person.secondFactorSince).length,
    locked: (people ?? []).filter((person) => person.lockedUntil !== null && new Date(person.lockedUntil) > now).length,
    off: (people ?? []).filter((person) => person.disabled).length,
  };

  return (
    <main className="people">
      <div className="people-head">
        <div>
          <h1>Users</h1>
          <p>Who can sign in, how far in they reach, and how well their accounts are protected.</p>
        </div>
        <button type="button" className="button primary" onClick={() => setAdding(true)}>
          + Add user
        </button>
      </div>

      {error && (
        <p className="people-error" role="alert">
          {error}
        </p>
      )}

      {people && (
        <div className={selected ? "people-layout open" : "people-layout"}>
          <div className="people-main">
            <Posture people={people} policy={policy} onPolicy={(next) => void choosePolicy(next)} now={now} />
            <RoleBar people={people} />

            <div className="people-toolbar">
              <input
                type="search"
                aria-label="Find a person"
                placeholder="Find by name, email, or team"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
              <div className="segments" role="radiogroup" aria-label="Show">
                {(
                  [
                    ["everyone", "Everyone"],
                    ["nofactor", "No second factor"],
                    ["locked", "Locked"],
                    ["off", "Switched off"],
                  ] as const
                ).map(([key, label]) => (
                  <label key={key} className={show === key ? "on" : undefined}>
                    <input type="radio" name={filterName} checked={show === key} onChange={() => setShow(key)} />
                    {label} <em>{counts[key]}</em>
                  </label>
                ))}
              </div>
            </div>

            {shown.length === 0 ? (
              <p className="people-empty">Nobody matches.</p>
            ) : (
              <ul className="people-grid" aria-label="People">
                {shown.map((person) => (
                  <li key={person.id}>
                    <PersonBadge
                      person={person}
                      isYou={person.id === myId}
                      selected={person.id === selected}
                      now={now}
                      onSelect={() => open(person.id === selected ? null : person.id, tab)}
                    />
                  </li>
                ))}
              </ul>
            )}

            <p className="people-footnote">
              Veyra keeps at least one active Admin: the last one cannot lose the role or be switched off. People are
              switched off rather than deleted, so their history stays.
            </p>
          </div>

          {selected && (
            <>
              <div className="people-shade sheet-only" aria-hidden="true" onClick={() => open(null)} />
              <PersonPanel
                key={selected}
                id={selected}
                isYou={selected === myId}
                tab={tab}
                onTab={(next) => open(selected, next)}
                onClose={() => open(null)}
                onChanged={() => void load()}
                now={now}
              />
            </>
          )}
        </div>
      )}

      {adding && (
        <AddUserPanel
          onClose={() => setAdding(false)}
          onAdded={(person) => {
            setAdding(false);
            void load();
            open(person.id);
          }}
        />
      )}
    </main>
  );
}

function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}
