-- A time zone per person (VEY-22): the IANA name, such as Europe/Berlin, that every screen shows times in. NULL
-- until the person picks one; the browser's zone is used until then.
ALTER TABLE access.user_account ADD COLUMN time_zone text;
