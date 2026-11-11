import type { ChannelKind } from "../api/types";

/** Props for {@link ChannelIcon}. */
interface ChannelIconProps {
  /** Webhook or email. */
  kind: ChannelKind;
  /** The width and height in pixels. Defaults to 22. */
  size?: number;
}

/**
 * The picture of a channel kind: a hook for webhooks, an envelope for email. Decorative: the kind is always written
 * next to it.
 */
export function ChannelIcon({ kind, size = 22 }: ChannelIconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {kind === "Webhook" ? (
        <path d="M9.5 8.5a3.5 3.5 0 1 1 5.2 3.1L17 16M8.4 12.2 6 16.4a3.5 3.5 0 1 0 5 3.1h6.5M17.5 19.5a3.5 3.5 0 1 0-1.2-6.8" />
      ) : (
        <>
          <rect x="3.5" y="5.5" width="17" height="13" rx="2.5" />
          <path d="m4.5 7.5 7.5 6 7.5-6" />
        </>
      )}
    </svg>
  );
}
