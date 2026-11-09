-- People who sign in to Veyra (ADR 0008). The schema "access" is created by the migrator before this script runs.
CREATE TABLE access.user_account (
    id                uuid        PRIMARY KEY,
    email             text        NOT NULL,
    -- Lower-case form of the email, so sign-in and uniqueness ignore case.
    email_normalized  text        NOT NULL UNIQUE,
    display_name      text        NOT NULL,
    -- PBKDF2 hash from ASP.NET Core Identity. NULL for people who only sign in through single sign-on.
    password_hash     text,
    -- 1 = Viewer, 2 = Editor, 3 = Admin (Veyra.Core.Security.VeyraRole).
    role              smallint    NOT NULL CHECK (role BETWEEN 1 AND 3),
    disabled          boolean     NOT NULL DEFAULT false,
    failed_sign_ins   integer     NOT NULL DEFAULT 0,
    locked_until      timestamptz,
    created_at        timestamptz NOT NULL,
    last_sign_in_at   timestamptz
);

-- Write-only keys that OTLP senders present. Only the SHA-256 hash of a key is stored, never the key itself.
CREATE TABLE access.ingestion_key (
    id            uuid        PRIMARY KEY,
    name          text        NOT NULL,
    -- "vyk_" plus the first eight characters of the random part, so people can tell keys apart.
    display_prefix text       NOT NULL,
    key_hash      bytea       NOT NULL UNIQUE,
    -- Environments the key may send data for. NULL means every environment.
    environments  text[],
    created_by    uuid        NOT NULL REFERENCES access.user_account (id),
    created_at    timestamptz NOT NULL,
    expires_at    timestamptz,
    revoked_at    timestamptz,
    last_used_at  timestamptz
);
