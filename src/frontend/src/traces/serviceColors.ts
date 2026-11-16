/**
 * The colours services get on the trace screens, as CSS variables of the theme, so each works in light and dark. A
 * service keeps its colour on every screen and every visit, because it is chosen from its name, not its position.
 * Red and amber are left out: on these screens they mean failed and slow.
 */
const palette = [
  "var(--accent)",
  "var(--metric-violet)",
  "var(--metric-teal)",
  "var(--metric-pink)",
  "var(--ok-fg)",
  "rgb(var(--nav-logs))",
  "var(--debug-fg)",
];

/**
 * The colour of a service.
 *
 * @param service - The service name.
 * @returns A CSS colour value.
 */
export function serviceColor(service: string): string {
  let hash = 0;

  for (const character of service) {
    hash = (hash * 31 + character.charCodeAt(0)) >>> 0;
  }

  return palette[hash % palette.length] ?? "var(--accent)";
}
