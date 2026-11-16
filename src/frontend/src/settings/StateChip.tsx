import type { Tone } from "./monitoringFormat";

/** Props for {@link StateChip}. */
export interface StateChipProps {
  /** The state's label, such as "Receiving". */
  label: string;
  /** Its colour. */
  tone: Tone;
}

/**
 * A state as a coloured chip with a dot, as the Database monitoring and Agents pages show it.
 *
 * @param props - The label and colour.
 * @returns The chip.
 */
export function StateChip({ label, tone }: StateChipProps) {
  return (
    <span className={`monitor-state ${tone}`}>
      <i aria-hidden="true" />
      {label}
    </span>
  );
}
