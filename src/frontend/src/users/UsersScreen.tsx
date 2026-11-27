import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useSearchParams } from "react-router";

import { api } from "../api/client";
import type { SecondFactorPolicy, UserList, UserShow } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { ScreenBar } from "../ui/ScreenBar";
import { useSession } from "../session/useSession";
import { AddUserPanel } from "./AddUserPanel";
import { PersonBadge } from "./PersonBadge";
import { PersonPanel, type PersonTab } from "./PersonPanel";
import { Posture } from "./Posture";
import { RoleBar } from "./RoleBar";
import "./users.css";

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
  const [list, setList] = useState<UserList | null>(null);
  const [policy, setPolicy] = useState<SecondFactorPolicy | null>(null);
  const [search, setSearch] = useState("");
  const [show, setShow] = useState<UserShow>("everyone");
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const now = new Date();

  const asking = useRef<AbortController | null>(null);

  // The server applies the search and the chip, and counts every figure (VEY-67). A newer question cancels an older
  // one, and the last answer stays on screen until the new one arrives.
  const load = useCallback(() => {
    asking.current?.abort();
    const controller = new AbortController();
    asking.current = controller;

    return Promise.all([api.listUsers({ search, show }, controller.signal), api.secondFactorPolicy()])
      .then(([answer, current]) => {
        if (!controller.signal.aborted) {
          setList(answer);
          setPolicy(current.requiredFor);
          setError(null);
        }
      })
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) {
          setError(messageOf(failure));
        }
      });
  }, [search, show]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => () => asking.current?.abort(), []);

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

  const shown = list?.people ?? [];
  const counts: Record<UserShow, number> = {
    everyone: list?.counts.everyone ?? 0,
    nofactor: list?.counts.noFactor ?? 0,
    locked: list?.counts.locked ?? 0,
    off: list?.counts.off ?? 0,
  };

  return (
    <main className="people">
      <ScreenBar trail={[{ label: "Users" }]} />
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

      {list && (
        <div className={selected ? "people-layout open" : "people-layout"}>
          <div className="people-main">
            <Posture posture={list.posture} policy={policy} onPolicy={(next) => void choosePolicy(next)} />
            <RoleBar roles={list.roles} />

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
              <HelpTip label="People">
                Each badge is one person: the band shows their role, 2FA whether they use a second factor, and the time
                when they were last active. An amber badge is locked after too many wrong passwords; a grey one is
                switched off and cannot sign in. Choose a badge to see more.
              </HelpTip>
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
