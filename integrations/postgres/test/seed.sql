-- The monitoring role exactly as docs/integrations/postgres.md creates it, and two databases with something to see.
-- run.sh passes the role's password as monitor_password.
CREATE ROLE veyra_monitor LOGIN PASSWORD :'monitor_password';
GRANT pg_monitor TO veyra_monitor;

CREATE DATABASE shop;
CREATE DATABASE reporting;

\connect shop

CREATE TABLE orders (id integer PRIMARY KEY, status text NOT NULL);
INSERT INTO orders SELECT id, 'new' FROM generate_series(1, 1000) AS id;

-- A manual vacuum, which postgresql.table.vacuum.count counts, then dead rows it has not cleared yet. 200 stay below
-- the 250 that would wake autovacuum for a table of 1000 rows.
VACUUM orders;
UPDATE orders SET status = 'seen' WHERE id <= 200;
