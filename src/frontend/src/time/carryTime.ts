/**
 * A link to another screen that keeps the range the address holds now, and nothing else: each screen's own filters
 * stay its own. A path may bring a filter of its own, such as a tile that opens the failed traces (VEY-82).
 *
 * @param target - The screen's path, with its own query parameters and an anchor if it has them.
 * @param current - The address's query parameters now.
 * @returns The path with the range, when the address names one.
 */
export function carryTime(target: string, current: URLSearchParams): string {
  // An anchor, such as a panel to scroll to, stays at the end, after the query (VEY-83).
  const sharp = target.indexOf("#");
  const anchor = sharp < 0 ? "" : target.slice(sharp);
  const path = sharp < 0 ? target : target.slice(0, sharp);
  const mark = path.indexOf("?");
  const kept = new URLSearchParams(mark < 0 ? "" : path.slice(mark + 1));
  const bare = mark < 0 ? path : path.slice(0, mark);

  for (const key of ["range", "from", "to"]) {
    const value = current.get(key);

    if (value !== null) {
      kept.set(key, value);
    }
  }

  const search = kept.toString();

  return `${search ? `${bare}?${search}` : bare}${anchor}`;
}
