-- Two-factor sign-in, sessions, and sign-in and account history (ADR 0011).

-- A free-text team, set by an Admin, so people can be told apart on the Users screen.
ALTER TABLE access.user_account ADD COLUMN team text;

-- One row per sign-in. The session cookie carries the ID; every request checks the row, so a session can be ended
-- on its own and the list shows where an account is signed in.
CREATE TABLE access.session (
    -- 32 random bytes, Base64url. Only ever sent inside the encrypted session cookie.
    id              text        PRIMARY KEY,
    user_id         uuid        NOT NULL REFERENCES access.user_account (id) ON DELETE CASCADE,
    started_at      timestamptz NOT NULL,
    -- Written at most every five minutes, so busy screens do not write on every request.
    last_active_at  timestamptz NOT NULL,
    ended_at        timestamptz,
    address         text,
    -- The User-Agent header, cut to 512 characters.
    user_agent      text,
    -- 1 = password, 2 = password and app code, 3 = password and recovery code, 4 = identity provider.
    method          smallint    NOT NULL CHECK (method BETWEEN 1 AND 4)
);

CREATE INDEX session_user_started ON access.session (user_id, started_at DESC);

-- Every sign-in attempt for an account that exists, kept for Veyra:Access:SignInHistoryDays (90 by default).
CREATE TABLE access.sign_in_event (
    id           bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id      uuid        NOT NULL REFERENCES access.user_account (id) ON DELETE CASCADE,
    at           timestamptz NOT NULL,
    -- 1 = signed in, 2 = signed in with an app code, 3 = signed in with a recovery code, 4 = wrong password,
    -- 5 = wrong code, 6 = locked, 7 = switched off.
    outcome      smallint    NOT NULL CHECK (outcome BETWEEN 1 AND 7),
    address      text,
    user_agent   text,
    -- The account had not signed in from this address within the kept history.
    new_address  boolean     NOT NULL DEFAULT false
);

CREATE INDEX sign_in_event_user_at ON access.sign_in_event (user_id, at DESC);
CREATE INDEX sign_in_event_at ON access.sign_in_event (at);

-- Changes to an account and who made them, kept while the account exists. No addresses.
CREATE TABLE access.user_event (
    id        bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id   uuid        NOT NULL REFERENCES access.user_account (id) ON DELETE CASCADE,
    at        timestamptz NOT NULL,
    -- Veyra.Access.Users.UserEventKind.
    kind      smallint    NOT NULL,
    -- NULL for changes Veyra made itself, such as first-run setup or the server's way back in.
    actor_id  uuid        REFERENCES access.user_account (id) ON DELETE SET NULL,
    -- What changed, for example "Viewer Editor" for a role change or the new team.
    detail    text
);

CREATE INDEX user_event_user_at ON access.user_event (user_id, at DESC);

-- The authenticator-app secret, encrypted with ASP.NET Data Protection. While enabled_at is NULL the person is still
-- setting it up and it is not asked for.
CREATE TABLE access.user_second_factor (
    user_id         uuid        PRIMARY KEY REFERENCES access.user_account (id) ON DELETE CASCADE,
    secret          text        NOT NULL,
    created_at      timestamptz NOT NULL,
    enabled_at      timestamptz,
    -- The 30-second step of the last accepted code, so the same code cannot be used twice.
    last_used_step  bigint
);

-- Single-use codes for a lost phone, as SHA-256 hashes.
CREATE TABLE access.recovery_code (
    user_id    uuid        NOT NULL REFERENCES access.user_account (id) ON DELETE CASCADE,
    code_hash  bytea       NOT NULL,
    used_at    timestamptz,
    PRIMARY KEY (user_id, code_hash)
);

-- Settings Admins change in the UI, such as who must use a second factor.
CREATE TABLE access.setting (
    name   text PRIMARY KEY,
    value  text NOT NULL
);
