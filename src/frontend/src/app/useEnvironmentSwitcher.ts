import { type KeyboardEvent, type RefObject, useEffect, useId, useRef, useState } from "react";

import type { ConditionKind } from "../api/types";
import { usePopover } from "../ui/usePopover";
import { useEnvironmentChoices } from "./useEnvironmentChoices";

/** One line of the environment list. */
export interface EnvironmentOption {
  /** The environment, or null for all of them. */
  name: string | null;
  /** What the line says. */
  label: string;
  /** Its weather, which each look shows its own way. */
  condition: ConditionKind;
  /** Its error ratio, when known. */
  errorRatio: number | null;
}

/** What every look's environment switcher works from (ADR 0021): the behaviour, without any markup. */
export interface EnvironmentSwitcher {
  /** Whether the list is open. */
  open: boolean;
  /** Opens or closes the list, from its button. */
  toggle(): void;
  /** The element around the button and the list, so a click outside closes it. */
  rootRef: RefObject<HTMLDivElement | null>;
  /** The button, which gets the focus back when the list closes. */
  buttonRef: RefObject<HTMLButtonElement | null>;
  /** The find box, which gets the focus when the list opens. */
  findRef: RefObject<HTMLInputElement | null>;
  /** The list's id, for the button's aria-controls. */
  listId: string;
  /** What the find box holds. */
  query: string;
  /**
   * Changes what the find box holds; the server answers what it keeps.
   *
   * @param query - The words.
   */
  setQuery(query: string): void;
  /** The lines to show: "All environments" first when the server lists it, then each environment it keeps. */
  options: EnvironmentOption[];
  /** Whether the server has answered, so an empty list means nothing matched. */
  answered: boolean;
  /**
   * Picks an environment for every screen and closes the list.
   *
   * @param option - The line picked.
   */
  choose(option: EnvironmentOption): void;
  /**
   * Moves between the find box and the lines with the arrow keys, as in any list.
   *
   * @param event - The key pressed inside the list.
   */
  moved(event: KeyboardEvent<HTMLElement>): void;
}

/**
 * The environment switcher's behaviour, shared by every look: the list opens from its button, the server lists what
 * the find box keeps (VEY-67), the arrow keys move through it, and a pick applies to every screen. Each look draws the
 * button and the list in its own way.
 *
 * @param onChoose - Called with the environment picked, or null for all of them.
 * @returns The state and the handlers to draw with.
 */
export function useEnvironmentSwitcher(onChoose: (environment: string | null) => void): EnvironmentSwitcher {
  const { open, toggle, close, rootRef, buttonRef } = usePopover();
  const [query, setQuery] = useState("");
  const choices = useEnvironmentChoices(open, query);
  const findRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  useEffect(() => {
    if (open) {
      findRef.current?.focus();
    }
  }, [open]);

  const options: EnvironmentOption[] = [
    ...(choices?.all
      ? [{ name: null, label: "All environments", condition: choices.all.condition, errorRatio: null }]
      : []),
    ...(choices?.environments ?? []).map((item) => ({
      name: item.name,
      label: item.name,
      condition: item.condition,
      errorRatio: item.errorRatio,
    })),
  ];

  function choose(option: EnvironmentOption) {
    onChoose(option.name);
    setQuery("");
    close(true);
  }

  function moved(event: KeyboardEvent<HTMLElement>) {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
      return;
    }

    const stops = Array.from(event.currentTarget.querySelectorAll<HTMLElement>("input, button"));
    const at = stops.indexOf(document.activeElement as HTMLElement);
    const next = stops[(at + (event.key === "ArrowDown" ? 1 : stops.length - 1)) % stops.length];
    event.preventDefault();
    next?.focus();
  }

  return {
    open,
    toggle,
    rootRef,
    buttonRef,
    findRef,
    listId,
    query,
    setQuery,
    options,
    answered: choices !== null,
    choose,
    moved,
  };
}
