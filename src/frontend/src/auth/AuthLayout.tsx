import type { ReactNode } from "react";

import { Logo } from "../app/Logo";
import "./auth.css";

/** Props for {@link AuthLayout}. */
interface AuthLayoutProps {
  /** The heading of the card, for example "Sign in to Veyra". */
  title: string;
  /** The form. */
  children: ReactNode;
}

/** The centred card that the sign-in and setup screens share. */
export function AuthLayout({ title, children }: AuthLayoutProps) {
  return (
    <main className="auth">
      <div className="auth-card">
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
