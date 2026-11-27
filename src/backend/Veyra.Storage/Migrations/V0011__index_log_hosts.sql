-- Filtering by host within a time window (ADR 0024). A host is not in the minute and hour counts, so the Log
-- Explorer's chart counts a host's records of the whole window from the records; this index keeps that, and the
-- search, to the speed budget of ADR 0015. Like the other indexes of log_record, it is made on the parent, so every
-- day has it: the existing days get it now, and days created from now on get it with their partition.
CREATE INDEX log_record_host_time ON storage.log_record (host_name, timestamp DESC);
