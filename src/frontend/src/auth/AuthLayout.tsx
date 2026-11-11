import type { ReactNode } from "react";

import { Logo } from "../app/Logo";
import "./auth.css";

/** Props for {@link AuthLayout}. */
interface AuthLayoutProps {
  /** The heading of the card, for example "Sign in to Veyra". */
  title: string;
  /** The form. */
  children: ReactNode;
  /** A wider card, for setting up a second factor next to its QR code. */
  wide?: boolean;
}

/** The centred card that the sign-in and setup screens share. */
export function AuthLayout({ title, children, wide = false }: AuthLayoutProps) {
  return (
    <main className="auth">
      <div className={wide ? "auth-card wide" : "auth-card"}>
        <div className="auth-brand">
          <Logo />
          <span>veyra</span>
        </div>
        <h1>{title}</h1>
        {children}
      </div>
    </main>
  );
}
