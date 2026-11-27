/**
 * A link to another screen that keeps the range the address holds now, and nothing else: each screen's own filters
 * stay its own. A path may bring a filter of its own, such as a tile that opens the failed traces (VEY-82).
 *
 * @param path - The screen's path, with its own query parameters if it has any.
 * @param current - The address's query parameters now.
 * @returns The path with the range, when the address names one.
 */
export function carryTime(path: string, current: URLSearchParams): string {
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

  return search ? `${bare}?${search}` : bare;
}
