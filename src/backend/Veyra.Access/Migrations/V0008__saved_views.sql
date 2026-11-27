-- Saved views (ADR 0024): a named query of a screen, kept per person beside their look, and shared with everyone
-- when its owner says so. A view holds no time range; the screen keeps the range it shows. The screens are checked by
-- the API, not here, so a screen that gets views needs no migration.
CREATE TABLE access.saved_view (
    id          uuid        PRIMARY KEY,
    owner_id    uuid        NOT NULL REFERENCES access.user_account (id) ON DELETE CASCADE,
    -- The screen the view belongs to, such as 'logs'.
    screen      text        NOT NULL,
    name        text        NOT NULL,
    -- The query text as the screen's bar shows it, such as 'service:orders level>=warning'.
    query       text        NOT NULL,
    shared      boolean     NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL
);

-- A person's own views of a screen, and the count that holds them to 100.
CREATE INDEX saved_view_owner ON access.saved_view (owner_id, screen);

-- The views others shared on a screen.
CREATE INDEX saved_view_shared ON access.saved_view (screen) WHERE shared;
