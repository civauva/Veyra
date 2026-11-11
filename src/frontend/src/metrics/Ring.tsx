import { formatShare, shareTone } from "./metricFormat";

/** Props for {@link Ring}. */
interface RingProps {
  /** The share in use, from 0 to 1; null when unknown. */
  share: number | null;
  /** What it measures, for example `CPU`. */
  label: string;
  /** A line under the label, for example `10.9 of 16 GB`. */
  detail: string;
}

const radius = 31;
const circumference = 2 * Math.PI * radius;

/**
 * A ring that fills with a share: green, amber from 75 %, red from 90 %, like the disk bars. The number in the middle
 * and the label say the same in words.
 */
export function Ring({ share, label, detail }: RingProps) {
  const filled = share === null ? 0 : Math.min(Math.max(share, 0), 1) * circumference;

  return (
    <div className="ring-box">
      <span className={`ring ${shareTone(share)}`}>
        <svg width="74" height="74" viewBox="0 0 74 74" aria-hidden="true">
          <circle className="ring-track" cx="37" cy="37" r={radius} />
          <circle
            className="ring-fill"
            cx="37"
            cy="37"
            r={radius}
            strokeDasharray={`${filled} ${circumference}`}
            transform="rotate(-90 37 37)"
          />
        </svg>
        <b>{formatShare(share)}</b>
      </span>
      <span className="ring-label">{label}</span>
      <em>{detail}</em>
    </div>
  );
}
