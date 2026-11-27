import type { ReactNode } from "react";

import { HelpTip } from "../../ui/HelpTip";

/** Props for {@link PanelHead}. */
export interface PanelHeadProps {
  /** The heading's ID, which names the panel for assistive technology. */
  id: string;
  /** The panel's name, its heading. */
  name: string;
  /** What the panel shows, where it comes from, and what to make of it. */
  help: ReactNode;
  /** What stands beside the name, such as a note on the range or a download. */
  children?: ReactNode;
}

/**
 * The head of a panel on a database server's screens: its name with a question mark that explains the panel (VEY-60),
 * then whatever stands beside it. The question mark sits outside the heading, so the panel keeps its name.
 *
 * @param props - The heading, its explanation, and what stands beside it.
 * @returns The panel's head.
 */
export function PanelHead({ id, name, help, children }: PanelHeadProps) {
  return (
    <div className="metric-panel-head">
      <div className="db-help-name">
        <h2 id={id}>{name}</h2>
        <HelpTip label={name}>{help}</HelpTip>
      </div>
      {children}
    </div>
  );
}

/** Props for {@link TileName}. */
export interface TileNameProps {
  /** The tile's name. */
  name: string;
  /** What the figure is, where it comes from, and what is good or bad. */
  help: ReactNode;
  /** The class of the name itself, which the tile's own stylesheet sets. */
  className?: string;
}

/**
 * A figure tile's name with a question mark that explains the figure (VEY-60). The row stays above a tile that opens
 * what it counts, so the question mark answers on its own and the rest of the tile still opens.
 *
 * @param props - The name, its explanation, and the class of the name.
 * @returns The name's row.
 */
export function TileName({ name, help, className }: TileNameProps) {
  return (
    <div className="db-tile-name">
      <span className={className}>{name}</span>
      <HelpTip label={name}>{help}</HelpTip>
    </div>
  );
}
