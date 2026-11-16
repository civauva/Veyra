-- Every setting Admins change on the Settings screen, and the history of each change (ADR 0017).

-- Who saved a value and when, and whether it is a secret. A secret's value is encrypted with ASP.NET Data Protection,
-- whose keys live apart from the database, so a backup of the database alone does not reveal it.
ALTER TABLE access.setting
    ADD COLUMN secret     boolean     NOT NULL DEFAULT false,
    ADD COLUMN changed_by uuid        REFERENCES access.user_account (id) ON DELETE SET NULL,
    ADD COLUMN changed_at timestamptz;

-- One row per setting a save changed. Kept 365 days. A secret's values are never written here, only that it changed.
CREATE TABLE access.setting_change (
    -- Also the version of the settings: a save names the version it started from and is refused if another came first.
    id              bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name            text        NOT NULL,
    -- Null when nothing was saved before, or when the change resets the setting to its default.
    old_value       text,
    new_value       text,
    secret          boolean     NOT NULL,
    -- 1 = changed, 2 = reset to its default, 3 = reverted from the history, 4 = imported.
    kind            smallint    NOT NULL CHECK (kind BETWEEN 1 AND 4),
    changed_by      uuid        REFERENCES access.user_account (id) ON DELETE SET NULL,
    -- Kept as it was, so the history still says who it was after the account is removed.
    changed_by_name text        NOT NULL,
    changed_at      timestamptz NOT NULL,
    -- The file name of an import.
    origin          text
);

-- The history panel: newest first, by time.
CREATE INDEX setting_change_at ON access.setting_change (changed_at DESC, id DESC);
