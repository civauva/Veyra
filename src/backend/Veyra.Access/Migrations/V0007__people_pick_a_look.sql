-- A look of the web app per person (VEY-69, ADR 0021), such as forecast or console. NULL until the person picks one;
-- the default look on the Settings screen is used until then. The looks are checked by the API, not here, so a new
-- look needs no migration.
ALTER TABLE access.user_account ADD COLUMN look text;
