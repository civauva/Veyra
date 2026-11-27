import { useEffect, useState } from "react";

import { api } from "../api/client";
import type { Release } from "../api/types";
import "./about.css";

/**
 * A release's day in words, read as a calendar day wherever the reader is.
 *
 * @param date - The day, as `YYYY-MM-DD`.
 * @returns For example `Nov 27, 2026`.
 */
export function releaseDay(date: string): string {
  return new Date(`${date}T00:00:00Z`).toLocaleDateString("en", {
    day: "numeric",
    month: "short",
    year: "numeric",
    timeZone: "UTC",
  });
}

/** Props for {@link ReleaseNotes}. */
interface ReleaseNotesProps {
  /** The version this page runs, marked in the list; null before it is known. */
  current: string | null;
}

/**
 * The release notes, newest first, as the server reads them from CHANGELOG.md (VEY-82): each version with its day,
 * its sentence, and its changes by kind. The version this page runs is marked, so a newer one above it stands out.
 * Each release is a level-3 heading, under the level-2 title of the dialog or page that holds the notes.
 */
export function ReleaseNotes({ current }: ReleaseNotesProps) {
  const [releases, setReleases] = useState<Release[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    api
      .releases(controller.signal)
      .then(setReleases)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(reason instanceof Error ? reason.message : "The release notes could not be read.");
        }
      });

    return () => controller.abort();
  }, []);

  if (error) {
    return (
      <p className="ab-error" role="alert">
        {error}
      </p>
    );
  }

  if (!releases) {
    return <p className="ab-quiet">Reading the release notes…</p>;
  }

  return (
    <ol className="ab-releases">
      {releases.map((release) => (
        <li key={release.version} className={release.version === current ? "ab-release current" : "ab-release"}>
          <h3 className="ab-release-head">
            <span className="ab-version">{release.version}</span>
            {release.date && <time dateTime={release.date}>{releaseDay(release.date)}</time>}
            {release.version === current && <span className="ab-here">in use here</span>}
          </h3>
          {release.summary && <p className="ab-summary">{release.summary}</p>}
          {release.sections.map((section) => (
            <section key={section.title} className="ab-section" aria-label={`${release.version}: ${section.title}`}>
              <h4 className={`ab-kind ab-kind-${section.title.toLowerCase()}`}>{section.title}</h4>
              <ul>
                {section.items.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            </section>
          ))}
        </li>
      ))}
    </ol>
  );
}
