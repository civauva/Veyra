import { useEffect, useId, useState } from "react";

/** Props for {@link CodeField}. */
interface CodeFieldProps {
  /** The digits typed so far. */
  value: string;
  /** Called with the digits when they change; anything that is not a digit is dropped. */
  onChange(value: string): void;
  /** The visible label. */
  label: string;
  /** Whether the field takes focus when it appears. */
  autoFocus?: boolean;
  /** Whether the field is read-only while a request runs. */
  disabled?: boolean;
}

/** How long one code is valid, as RFC 6238 and every authenticator app have it. */
const stepSeconds = 30;

/**
 * The six digits from an authenticator app. It is one input, so typing, pasting, and the phone's one-time-code
 * suggestion all work, drawn as six boxes with the time left until the app shows a new code.
 */
export function CodeField({ value, onChange, label, autoFocus = false, disabled = false }: CodeFieldProps) {
  const id = useId();
  const left = useSecondsLeft();
  const digits = Array.from({ length: 6 }, (_, index) => value[index] ?? "");

  return (
    <div className="code-field">
      <label htmlFor={id}>{label}</label>
      <div className="code-boxes">
        <input
          id={id}
          inputMode="numeric"
          autoComplete="one-time-code"
          maxLength={7}
          value={value}
          disabled={disabled}
          // The boxes below show the digits; the input stays on top for typing, selection, and screen readers.
          autoFocus={autoFocus}
          onChange={(event) => onChange(event.target.value.replace(/\D/g, "").slice(0, 6))}
        />
        <div className="code-digits" aria-hidden="true">
          {digits.slice(0, 3).map((digit, index) => (
            <span key={index} className={index === value.length ? "next" : undefined}>
              {digit}
            </span>
          ))}
          <i />
          {digits.slice(3).map((digit, index) => (
            <span key={index + 3} className={index + 3 === value.length ? "next" : undefined}>
              {digit}
            </span>
          ))}
        </div>
      </div>
      <div className="code-tick" aria-hidden="true">
        <span>
          <b style={{ width: `${(left / stepSeconds) * 100}%` }} />
        </span>
        new code in {left} s
      </div>
    </div>
  );
}

/** The seconds until the app shows its next code, counted from the clock as the app does. */
function useSecondsLeft(): number {
  const [left, setLeft] = useState(() => secondsLeft());

  useEffect(() => {
    const timer = window.setInterval(() => setLeft(secondsLeft()), 1000);

    return () => window.clearInterval(timer);
  }, []);

  return left;
}

function secondsLeft(): number {
  return stepSeconds - (Math.floor(Date.now() / 1000) % stepSeconds);
}
