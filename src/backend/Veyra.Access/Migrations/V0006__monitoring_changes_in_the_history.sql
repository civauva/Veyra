-- Changes to monitored systems and agents join the settings history, without their values (ADR 0019).

ALTER TABLE access.setting_change
    -- What a row is about: 1 = SQL Server, 2 = PostgreSQL server, 3 = agent; null for a setting. Rows about one of
    -- these are not settings saves, so they do not move the settings version.
    ADD COLUMN subject smallint CHECK (subject BETWEEN 1 AND 3),
    -- The fields a change to one of them touched, by name, never their values.
    ADD COLUMN fields  text[],
    DROP CONSTRAINT setting_change_kind_check,
    -- 5 = added, 6 = removed, for monitored systems and agents.
    ADD CONSTRAINT setting_change_kind_check CHECK (kind BETWEEN 1 AND 6);
