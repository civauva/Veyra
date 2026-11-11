import { useMemo } from "react";
import { encode } from "uqr";

/** Props for {@link QrCode}. */
interface QrCodeProps {
  /** The text to encode, the `otpauth://` address. */
  value: string;
  /** What a screen reader says instead of the picture. */
  label: string;
}

/**
 * A QR code drawn as SVG, dark squares on white whatever the theme, because phone cameras read that most reliably.
 * The `uqr` library only computes the modules; drawing them here keeps the picture crisp and the page free of
 * inline HTML.
 */
export function QrCode({ value, label }: QrCodeProps) {
  const { size, path } = useMemo(() => {
    const qr = encode(value, { border: 2, ecc: "M" });
    const squares: string[] = [];

    qr.data.forEach((row, y) =>
      row.forEach((dark, x) => {
        if (dark) {
          squares.push(`M${x} ${y}h1v1h-1z`);
        }
      }),
    );

    return { size: qr.size, path: squares.join("") };
  }, [value]);

  return (
    <svg className="qr" viewBox={`0 0 ${size} ${size}`} role="img" aria-label={label} shapeRendering="crispEdges">
      <rect width={size} height={size} fill="#fff" />
      <path d={path} fill="#0e2440" />
    </svg>
  );
}
