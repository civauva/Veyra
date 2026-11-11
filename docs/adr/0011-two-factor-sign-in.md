# 0011. Two-factor sign-in, sessions, and sign-in history

Date: 2026-11-10
Status: Accepted

## Context

ADR 0008 lets people sign in with a local account (email and password) or through OpenID Connect, and keeps the
session in an encrypted cookie that Veyra re-checks against the user on every request. Three things are missing:

- **A second factor.** A password alone can be guessed, reused from another site, or phished, and an Admin account
  opens ingestion keys, users, and notification channels.
- **Knowing where an account is signed in.** A cookie cannot be revoked on its own: changing a password does not
  sign out other browsers, and nobody can see which browsers hold a session.
- **A record of what happened to an account.** Veyra keeps only the time of the last sign-in. After a lock, an odd
  sign-in, or a role change, an Admin has nothing to look at.

Facts that shape the choices:

- **Small installations.** Many Veyra installations have a handful of people and no identity provider. A second
  factor must work without any other service.
- **No outgoing messages required.** Email is optional (ADR 0009) and SMS would need a paid provider.
- **Lost devices happen.** People replace phones, so there must be a way back in that does not weaken the factor.
- **OpenID Connect users** already sign in through a provider that enforces its own second factor.
- **Addresses are personal data.** IP addresses and browser descriptions identify people and must not be kept
  longer than needed.

## Decision

Local accounts can add a **second factor with an authenticator app** (TOTP, RFC 6238) with **recovery codes**, and
Admins can require it. Every sign-in becomes a **session row** that can be listed and ended, every sign-in attempt
and every change to an account is **recorded**, and both are kept for a limited time.

### Second factor

- **Codes.** Six digits, a new one every 30 seconds, HMAC-SHA1 as every authenticator app supports. Veyra accepts the
  code of the current step and one step on either side for clock drift, and never the same step twice, so a code
  that was seen cannot be used again. The algorithm is a few lines over `System.Security.Cryptography`; no library.
- **Setting it up.** On the Account page Veyra makes a 160-bit secret and shows it as a QR code and as text. The
  factor is switched on only after the person enters a working code. The QR code is drawn in the browser by a small
  library, because .NET and the browser have no QR encoder.
- **Storage.** The secret is encrypted with ASP.NET Data Protection, the key ring that already protects session
  cookies and webhook URLs (ADR 0009), in a new table `access.user_second_factor`. It is never shown again.
- **Recovery codes.** Ten single-use codes, shown once at setup and replaceable at any time. They are stored as
  SHA-256 hashes; they are random, so a slow hash adds nothing.
- **Signing in.** After a correct password, an account with a second factor gets a short-lived, encrypted,
  HTTP-only cookie that only allows the code step (`POST /api/v1/session/second-factor`). The session starts after a
  correct code or recovery code. The pending cookie lasts five minutes. Wrong codes count toward the account lockout
  like wrong passwords, and the code step shares the sign-in rate limit.
- **Requiring it.** An Admin chooses on the Users screen who must use a second factor: nobody (the default), Admins,
  or everyone. Someone who must and has none sets it up right after the password step, and gets a session only once
  it works.
- **Resetting.** An Admin can remove another person's second factor, for a lost phone. That person's sessions end
  and, if it is required, they set up a new one at the next sign-in.
- **Last way back in.** If the only Admin loses both phone and recovery codes, someone with access to the server
  sets `Veyra__Access__RemoveSecondFactor` to that email and restarts Veyra. At startup Veyra removes that account's
  factor and logs a security warning; the setting is then taken out again. Server access already means full trust.
- **OpenID Connect.** Accounts that sign in through a provider are not asked for a Veyra code.

### Sessions

- **One row per sign-in** in `access.session`: a random ID, the user, when it started, when it was last active, the
  address and browser it started from, and how it was signed in (password, code, recovery code, provider). The
  cookie carries the session ID.
- **Checked on every request**, where Veyra already re-checks the user: a session that was ended, or whose user was
  switched off, is refused at once. Last activity is written at most every five minutes.
- **Ending sessions.** People end their own other sessions on the Account page; Admins end anyone's on the Users
  screen. Changing a password, switching the second factor on or off, replacing recovery codes, and any reset by an
  Admin end all of that person's other sessions. This replaces a separate session stamp.
- **Expiry.** A session ends after the cookie's idle time, as today.

### Sign-in history and account history

- **Sign-in attempts** in `access.sign_in_event`, for accounts that exist: time, outcome (signed in, wrong password,
  wrong code, recovery code used, locked, switched off), address, and browser. Attempts for unknown emails are only
  counted in Veyra's own log, so typed-in strangers' addresses are not stored.
- **Account changes** in `access.user_event`: added, role changed, switched off or on, password changed or reset,
  second factor on, off, or removed, recovery codes replaced, sessions ended, team changed; with who did it and when.
  These carry no addresses and are kept while the account exists.
- **Addresses** come from the connection after the forwarded headers of the reverse proxy (ADR 0006). The browser and
  system are read from the `User-Agent` header with a few patterns, no library; the header is stored cut to 512
  characters.
- **New addresses.** A sign-in attempt from an address the account has not used within the kept history is marked
  as new, so an Admin notices guessing from elsewhere. No address list beyond the history is kept for this.
- **Who sees it.** Admins see everyone's; everyone sees their own on the Account page.
- **How long.** Sign-in attempts and ended sessions are deleted after `Veyra:Access:SignInHistoryDays`, 90 by
  default, by an hourly clean-up in the Access module.

### Profile

- **Team.** An optional free-text field of up to 100 characters, set by an Admin, shown on the cards and the
  Account page.
- **Last active** is the newest activity of any of the person's sessions.

## Alternatives considered

- **Passkeys (WebAuthn).** The strongest option and resistant to phishing, but it needs a fixed public origin, a
  library for attestation and assertion, and a screen to manage several devices per person. It is a good later
  addition, possibly as a replacement for passwords, and would need its own ADR.
- **Codes by email.** Needs SMTP, which is optional, and an email account is often protected by the same password.
- **Codes by SMS.** Needs a paid provider and is open to SIM swapping.
- **Only through an identity provider.** Forces small installations to run or rent one, against ADR 0006.
- **Recovery by an Admin only, without codes.** Leaves the only Admin locked out with no way back but the server.
- **A session stamp instead of session rows.** Signs out all other browsers at once and nothing more: no list, no
  ending one browser, no last activity. The rows cost one table and one indexed lookup the request already makes.
- **Keeping sign-in history forever.** Useful for investigations, but keeps personal data without need. Ninety days
  covers the questions an Admin asks after an incident.

## Consequences

- New Access tables: the second factor, recovery codes, sessions, sign-in events, user events, and one setting for
  who must use a second factor; a team column on the user. Migrations as in ADR 0008.
- Sign-in becomes two steps for those who use a second factor; the API documents the pending step and its answers.
- Every authenticated request reads the session row together with the user, which it already reads.
- Losing the Data Protection key ring also loses every second factor, as it already loses webhook URLs. The
  deployment guide's backup section names the key volume.
- People who lose phone and codes depend on an Admin, and the last Admin on the server.
- Setting up a required second factor at sign-in trusts the password: someone who knows an account's password before
  its owner signs in could set up their own factor. When requiring it for existing accounts, ask people to sign in
  soon; the account history shows who switched a factor on and when.
- The deployment guide states what personal data the sign-in history keeps and for how long.
