# PostgreSQL

The OpenTelemetry Collector watches a PostgreSQL server with its `postgresql` and `sqlquery` receivers and sends
connections, database and table sizes, deadlocks, locks, rows live and dead, vacuums, transaction ID age, blocked
sessions, and the longest running query to Veyra over OTLP. Veyra shows the server as a database block, named
`postgresql` and the name you choose (ADR 0018). The tested configuration is
[`integrations/postgres/collector.yaml`](../../integrations/postgres/collector.yaml).

Supported: PostgreSQL 14 and newer, the versions its developers still support, with the Collector contrib
distribution 0.140.0. CI runs the check in [`integrations/postgres/test/`](../../integrations/postgres/test/run.sh)
against PostgreSQL 14, 16, and 18 on every push, with TLS and without.

There are two ways to set it up. **From the Settings screen** is the usual one: a Veyra agent reads the server, and
nothing is edited by hand. **With your own Collector** is for people who already run one.

## From the Settings screen

Veyra's built-in agent, the Collector beside Veyra in its Docker Compose deployment, reads every PostgreSQL server
Veyra's host can reach; an agent in another network reads the servers there (ADR 0019, [`agents.md`](agents.md)).

1. Open **Settings → Database monitoring** and choose **Add PostgreSQL**.
2. **Where it is.** Give the server its name in Veyra, such as `pg-main`; its history and alerts are filed under it,
   so keep it once chosen. Enter its address, its port (5432 unless it listens elsewhere), its environment, and the
   agent that reads it.
3. **The role Veyra uses.** The page makes a strong password and writes the script that creates the role (step 1
   below). Copy it and run it once in `psql` as a superuser. Using a role of your own, type its password instead; it
   may not hold spaces, quotes, or backslashes, which the `postgresql` receiver cannot pass on.
4. **Encryption.** *Required* uses TLS and checks the server's certificate and name against what the agent trusts.
   *Trust its certificate* uses TLS and accepts any certificate, such as a self-signed one. *Off* sends everything,
   the password too, without TLS; use it only on a network you trust.
5. **Save and start watching.** The agent gets the settings within half a minute, and the panel follows what it
   finds: reached the server, encrypted the connection, logged in, read every figure.

Each server on the page says how it stands, in words, as SQL Servers do: *Receiving*, *Waiting for first data*,
*Login failed* (a wrong password, a missing role, or `pg_hba.conf` turning the agent away), *Not reachable*,
*Certificate refused* (also when the server does not take TLS at all), *A grant is missing*, or a problem with the
agent itself. The password is stored encrypted with Veyra's Data Protection keys, never shown again, and sent only to
the agent that reads the server.

## With your own Collector

### 1. Create a monitoring role

As a superuser:

```sql
CREATE ROLE veyra_monitor LOGIN PASSWORD '...';
GRANT pg_monitor TO veyra_monitor;
```

`pg_monitor` lets the role read the server's statistics and every session's state and start time, but no table's
rows. Without it, other roles' sessions show no start time and are not counted as long or blocked. The receivers read
each database's own statistics by connecting to it, which every role may do unless `CONNECT` was revoked from
`PUBLIC`; then grant it: `GRANT CONNECT ON DATABASE shop TO veyra_monitor;`.

`pg_hba.conf` must let the Collector sign in with a password, with a line such as this one and the Collector's own
address, followed by `SELECT pg_reload_conf();`:

```
host  all  veyra_monitor  10.0.0.0/8  scram-sha-256
```

Use `hostssl` instead of `host` to refuse connections without TLS.

### 2. Create a key

Create an ingestion key on Veyra's **Keys** page. The secret is shown once.

### 3. Choose the server's name

`POSTGRES_SERVER` is the name Veyra shows for the server, for example `pg-main`. Veyra files the server's history and
alerts under it, so keep it once chosen. The `postgresql` receiver names only the database, so two servers with a
database of the same name could not be told apart without it. One Collector configuration reads one server.

### 4. Encryption and the certificate

The two receivers take the same choice in two forms; set all three variables together:

| Choice | `POSTGRES_TLS_INSECURE` | `POSTGRES_TLS_SKIP_VERIFY` | `POSTGRES_SSLMODE` |
| --- | --- | --- | --- |
| TLS, checking the certificate and the name (the default) | `false` | `false` | `verify-full` |
| TLS with a self-signed certificate, not checked | `false` | `true` | `require` |
| No TLS | `true` | `false` | `disable` |

To check a self-signed certificate instead of skipping the check, let the Collector's machine trust it: add the
server's certificate, or the CA that signed it, to the machine's trusted certificates, or, in a container, mount a
folder holding it and set `SSL_CERT_DIR=/etc/ssl/certs:/etc/veyra/certs`. The certificate's name must match
`POSTGRES_HOST`.

### 5. Run it

```bash
docker run -d --name veyra-postgres --restart unless-stopped \
  -v "$PWD/integrations/postgres/collector.yaml:/etc/otelcol-contrib/config.yaml:ro" \
  -e VEYRA_OTLP_ENDPOINT=https://veyra.example.com \
  -e VEYRA_ENVIRONMENT=production \
  -e POSTGRES_SERVER=pg-main \
  -e POSTGRES_HOST=pg-main.corp.local \
  -e POSTGRES_PORT=5432 \
  -e POSTGRES_USER=veyra_monitor \
  --env-file /etc/veyra-postgres.env \
  otel/opentelemetry-collector-contrib:0.140.0
```

`/etc/veyra-postgres.env` holds `VEYRA_INGESTION_KEY=vyk_...` and `POSTGRES_PASSWORD=...`, so the secrets stay out of
the shell history. Keep it readable by root only (`chmod 600`). The password is given as it is, not URL-encoded.

## What it sends

Every series carries the resource attributes `db.system.name=postgresql`, `server.address` (the server's name in
Veyra), and `deployment.environment.name`; never `service.name`.

| Veyra shows | Metric | Kind and unit | Every |
| --- | --- | --- | --- |
| Sessions per database | `postgresql.backends` | sum, not monotonic, `1`; idle sessions too | 30 s |
| The connection limit | `postgresql.connection.max` | gauge, `{connections}`; the server's `max_connections` | 30 s |
| Database size and growth | `postgresql.db_size` | sum, not monotonic, `By` | 30 s |
| Deadlocks | `postgresql.deadlocks` | cumulative count, `{deadlock}` | 30 s |
| Commits and rollbacks | `postgresql.commits`, `.rollbacks` | cumulative counts, `1` | 30 s |
| Rows live and dead per table | `postgresql.rows` | sum, not monotonic, `1`; attribute `state` `live` or `dead` | 30 s |
| Manual vacuums per table | `postgresql.table.vacuum.count` | cumulative count, `{vacuum}`; autovacuum not counted | 30 s |
| Table and index sizes | `postgresql.table.size`, `postgresql.index.size` | `By` | 30 s |
| Locks held or awaited | `postgresql.database.locks` | gauge, `{lock}`, by `relation`, `mode`, `lock_type` | 30 s |
| Cache hits and reads, temporary files, sequential scans | `postgresql.blks_hit`, `.blks_read`, `.temp_files`, `.temp.io`, `.sequential_scans` | cumulative counts | 30 s |
| Transaction ID age per database | `postgresql.database.xid.age` (`sqlquery`) | gauge, `{transactions}` | 30 s |
| Sessions waiting for a lock over 30 s | `postgresql.sessions.blocked` (`sqlquery`) | gauge, `{sessions}`, server-wide | 30 s |
| The longest running query per database | `postgresql.query.duration.max` (`sqlquery`) | gauge, `s`; 0 when none runs | 30 s |

- **Per database.** The receiver's metrics name the database in the resource attribute `postgresql.database.name`,
  and a table's also `postgresql.table.name`, as `schema.table` such as `public.orders`. The three `sqlquery`
  metrics name it in the data point attribute `postgresql.database.name` instead. Template databases are left out.
- **Transaction ID wraparound.** `postgresql.database.xid.age` is `age(datfrozenxid)`: how many transactions ago the
  database's oldest unfrozen row was written. PostgreSQL stops taking writes as it nears 2^31, about 2.1 billion;
  vacuum freezes rows and lowers it.
- **Blocked sessions** come from `pg_locks.waitstart`, which PostgreSQL has had since version 14.
- **The longest query** counts the applications' active queries; the Collector's own session and background workers
  such as autovacuum are left out. No query text is read.
- **Not sent.** The receiver's query samples and top queries (`db.server.query_sample`, `db.server.top_query`) are
  off: Veyra has no screen for them yet, and top queries need the `pg_stat_statements` extension in every database.
  The receiver warns once at start that its feature gate `receiver.postgresql.separateSchemaAttr` is off; that is
  expected.
- **Volume.** About 15 series per database, 10 per table, and a few for the server, each every 30 seconds.

## When Veyra is unreachable

The `otlphttp` exporter holds up to 1,000 batches in memory and retries each one for up to 10 minutes; a batch that
still fails after that is dropped, so an outage longer than 10 minutes leaves a gap. The memory limiter keeps the
Collector under 128 MiB.

## Check it

Within a minute the server appears under Databases on the **Metrics** page, and on **Settings → Database
monitoring** as *Receiving*. If it does not, the Collector's log (`docker logs veyra-postgres`) says why:

- `password authentication failed` for a wrong role or password, `no pg_hba.conf entry` when `pg_hba.conf` does not
  let the Collector in (step 1).
- `SSL is not enabled on the server` for a server without TLS, and `x509:` for a certificate the Collector does not
  trust (step 4).
- `401` from Veyra for a wrong key, and connection errors for a wrong endpoint.

To check a change to the configuration, run the same check as CI, which needs Docker, OpenSSL, and Node.js. It starts
a PostgreSQL, causes a deadlock, a session blocked for over 30 seconds, and a long query, and checks every metric
above:

```bash
bash integrations/postgres/test/run.sh
POSTGRES_IMAGE=postgres:18-alpine POSTGRES_TLS=off bash integrations/postgres/test/run.sh
```
