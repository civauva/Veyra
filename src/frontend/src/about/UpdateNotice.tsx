import type { AboutState } from "./useAbout";
import "./about.css";

/** Props for {@link UpdateNotice}. */
interface UpdateNoticeProps {
  /** The running Veyra, with the newer build to offer. */
  about: AboutState;
  /** Opens the release notes. */
  onNotes(): void;
}

/**
 * The notice that a newer Veyra runs than the one this page loaded (VEY-82): its version, a link to what is new, and
 * Reload. It waits in the corner without taking the focus, so nobody loses what they are typing; Later hides it until
 * the next release. Every look shows it.
 */
export function UpdateNotice({ about, onNotes }: UpdateNoticeProps) {
  if (!about.newer) {
    return null;
  }

  return (
    <section className="ab-update" role="status" aria-label="New version">
      <span className="ab-update-mark" aria-hidden="true">
        <svg viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="1.6">
          <path d="M13.5 8a5.5 5.5 0 1 1-1.6-3.9" strokeLinecap="round" />
          <path d="M12.5 1.8v2.8H9.7" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </span>
      <p>
        {about.newer.version === about.version ? (
          // A build of the same version, such as a fix that did not change the number.
          <b>A new build of Veyra {about.newer.version} is ready.</b>
        ) : (
          <b>Veyra {about.newer.version} is ready.</b>
        )}
        <span>Reload to use it{about.version ? `; this page runs ${about.version}` : ""}.</span>
      </p>
      <div className="ab-update-actions">
        <button type="button" className="ab-update-link" onClick={onNotes}>
          What's new
        </button>
        <button type="button" className="ab-update-later" onClick={about.dismiss}>
          Later
        </button>
        <button type="button" className="ab-update-reload" onClick={() => window.location.reload()}>
          Reload
        </button>
      </div>
    </section>
  );
}
