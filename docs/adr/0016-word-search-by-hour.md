# 0016. Word search by hour

Date: 2026-11-12
Status: Accepted

## Context

ADR 0015 gives every log search 100 ms. A search for a word reads `storage.log_record` through a GIN index on
`to_tsvector('simple', body)` and returns the newest matches first, a page at a time. PostgreSQL has two ways to do it,
and each has a case it cannot bound:

- **Newest first through the time index**, parsing each record's body and keeping the matches. It costs about 1.8 µs
  for every record it steps over: half to read the record, half to parse it. When a common word stops appearing, for
  example an error fixed by a deploy, every newer record is stepped over. At 35 records a second (126,000 an hour) that
  is about 225 ms for every hour since the word last appeared, and seconds for a day. On the benchmark the request
  "7 days, a common word" grew from 58 to 97 ms in the hour the load ran, because the load never writes that word.
- **Through the word index**, reading every match of a day and sorting them. For a word in 368,000 of a day's 2.5
  million records that is 670 ms, or 345 ms with the memory and workers word counts get, however recent the matches.

The planner picks one from its estimates; neither is right for a word that was common and then stopped. Both indexes
are keyed by something else than what the search walks: the word index knows the word but not the time, and the time
index knows the time but not the word.

## Decision

Index each word together with the hour it was written, and search hour by hour, newest first.

1. **An index of words by hour.** An immutable function `storage.log_terms(body, timestamp)` returns the record's words,
   from the same parser and `simple` dictionary as today, each followed by `@` and the UTC hour as hours since 1970:
   `deadlock@498452`. A GIN index on it replaces `log_record_body_text`.
2. **Search walks the hours.** The words of the search text come from `to_tsvector('simple', text)`, the words
   `plainto_tsquery` uses today. One statement takes the window's hours newest first, looks up each hour's own keys,
   sorts only that hour's matches, and stops when the page is full. Measured on the benchmark: an hour without the word
   0.1 ms, a full hour with 18,000 matches 21 ms. A week of hours without the word costs about 20 ms in all.
3. **Counts by word read the same index.** Each word becomes one condition, `log_terms && {word@h1, word@h2, …}` over
   the window's hours, with the extra memory and workers word counts already get. A day of the common word counts in
   171 ms, against 185 ms today.
4. **Upgrading without stopping ingestion.** The migration creates the function and the index on the parent table only,
   which locks nothing, so new days get the index from their first record. A background job builds it on each existing
   day with `CREATE INDEX CONCURRENTLY`, newest day first, and attaches it. Until a day has it, that day is searched and
   counted the old way, as days not yet counted are in ADR 0015. When every day has it, the old index is dropped.
5. **Proof.** Plan tests fail when a word search stops reading the new index, and the benchmark gains a request for a
   word that stopped appearing an hour before.

Attribute filters keep today's plans: a JSON index cannot be keyed by hour the same way without changing how values of
different types match. The same worst case remains for an attribute that stopped appearing; the budgets will show if
it needs the same treatment.

## Alternatives considered

- **Bound today's plans: the newest 50,000 records first, then each day through the word index.** No schema change and
  it covers attributes too, but the worst case is about 450 ms, and a gap of more than about 25 minutes of a busy
  installation's logs is already over the budget.
- **Store the parsed words as a column.** Halves the cost of stepping over a record, but the cost still grows with the
  gap, and every record grows.
- **A BRIN time index combined with the word index.** Combining them makes the page bitmap lossy, so every record of
  each page is parsed again: 150 ms for one hour.
- **Hour slices through the time index and the word index together.** Each slice reads the whole day's matches from
  the word index (27 ms for the common word) and 0.25 µs per record of the slice from the time index, so the cost still
  grows with the gap.
- **The RUM index, which can return matches in time order.** It is an extension that the stock PostgreSQL image of
  ADR 0006 does not include.
- **A separate search engine.** Against ADR 0003 and ADR 0006: one PostgreSQL and no other services.

## Consequences

- A word search answers in tens of milliseconds however long ago the word last appeared.
- The word index is larger: 124 MB instead of 70 MB for a day of 2.5 million records, about 6% more storage for the
  day. Writing costs about 4 µs more per record (8 µs instead of 4 µs for this index).
- On upgrade, the background job builds the index at about 30 seconds per day of 3 million records on the reference
  installation. Ingestion and searches continue meanwhile; days not yet indexed are as slow as today.
- The hour is part of the index key, so the function must never change its output for a stored record. A change to it
  needs a new index and a new ADR.
- Attribute filters keep the worst case described above.
