-- Words by hour (ADR 0016): each word of a record's body is indexed together with the UTC hour the record was written,
-- as hours since 1970, for example 'deadlock@498452'. A search walks the hours of its window newest first and looks up
-- only each hour's keys, so a word that stopped appearing is found as fast as one that is everywhere.
--
-- The words come from the same parser and 'simple' dictionary as to_tsvector('simple', body), so a search finds what
-- it found before. The hour is floored, never rounded: a record at 02:59:59.9 belongs to hour 2.
--
-- This function is part of every index key. It must never change what it returns for a stored record; a different
-- function needs a new index and a new ADR. It is declared IMMUTABLE although extract() is only STABLE, because the
-- epoch of a timestamptz does not depend on the time zone. chr(31) never occurs inside a word, so it can separate them.
CREATE FUNCTION storage.log_terms(body text, at timestamptz) RETURNS text[]
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$
    SELECT string_to_array(
        array_to_string(
            tsvector_to_array(to_tsvector('simple', body)),
            '@' || floor(extract(epoch FROM at) / 3600)::bigint || chr(31))
        || '@' || floor(extract(epoch FROM at) / 3600)::bigint,
        chr(31))
$$;

-- On the parent only: building it locks no existing day, so ingestion goes on. Days created from now on get it with
-- their first record; the existing days are listed below and built by a background job.
CREATE INDEX log_record_terms ON ONLY storage.log_record USING gin (storage.log_terms(body, timestamp));

-- The default partition holds only records outside every day, usually none, so it is indexed at once.
CREATE INDEX log_record_default_terms ON storage.log_record_default USING gin (storage.log_terms(body, timestamp));
ALTER INDEX storage.log_record_terms ATTACH PARTITION storage.log_record_default_terms;

-- The days whose partition does not have the index yet. A background job builds it on each with CREATE INDEX
-- CONCURRENTLY, newest day first; until then the day is searched and counted the old way, through
-- log_record_body_text. When none is left, the job drops log_record_body_text.
CREATE TABLE storage.log_terms_backfill (
    day date PRIMARY KEY
);

INSERT INTO storage.log_terms_backfill (day)
SELECT to_date(substring(child.relname FROM 12), 'YYYYMMDD')
FROM pg_inherits AS link
JOIN pg_class AS child ON child.oid = link.inhrelid
WHERE link.inhparent = 'storage.log_record'::regclass
  AND child.relname ~ '^log_record_[0-9]{8}$';

-- A new installation has no days yet, so it never needs the old index.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM storage.log_terms_backfill) THEN
        DROP INDEX storage.log_record_body_text;
    END IF;
END
$$;
