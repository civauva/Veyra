import type { ReactNode } from "react";

import { HelpTip } from "../ui/HelpTip";

/** Props for {@link FigureName}. */
export interface FigureNameProps {
  /** The figure's name, such as "Series"; its question mark is read as "What Series means". */
  name: string;
  /** What the figure is, where it comes from, and what to look for. */
  children: ReactNode;
}

/**
 * The name of a figure in a strip of figures, with the question mark that explains it (VEY-60).
 *
 * @param props - The name and its explanation.
 * @returns The figure's term.
 */
export function FigureName({ name, children }: FigureNameProps) {
  return (
    <dt className="metric-name">
      <span>{name}</span>
      <HelpTip label={name}>{children}</HelpTip>
    </dt>
  );
}

/** Props for {@link PanelName}. */
export interface PanelNameProps {
  /** The heading's id, which names the panel it heads. */
  id?: string;
  /** The heading as shown. */
  name: string;
  /** What the question mark explains, when it differs from the heading, such as "a host card"; the heading by default. */
  label?: string;
  /** What the panel shows, where it comes from, and what to look for. */
  children: ReactNode;
}

/**
 * The heading of a panel or a section of cards, with the question mark that explains it (VEY-60). The question mark
 * sits beside the heading rather than in it, so the panel keeps the heading alone as its name.
 *
 * @param props - The heading, what it explains, and the explanation.
 * @returns The heading and its question mark.
 */
export function PanelName({ id, name, label, children }: PanelNameProps) {
  return (
    <div className="metric-name">
      <h2 id={id}>{name}</h2>
      <HelpTip label={label ?? name}>{children}</HelpTip>
    </div>
  );
}
