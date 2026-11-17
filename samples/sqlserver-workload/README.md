# SQL Server workload

`workload.ps1` keeps a SQL Server busy for a few minutes, so Veyra's SQL Server screen has something to show on a
server that is otherwise idle, such as one on a developer's machine.

It creates one database, `VeyraWorkload`, with two small tables, and touches nothing else. While it runs:

- a transaction holds a lock and three sessions wait behind it: a blocking chain on the Live and Blocking tabs;
- one session keeps running a CPU-heavy query: a long request on the Live tab, a busy statement on the Queries tab;
- once a minute, two transactions take the same rows in opposite order: a deadlock on the Blocking tab.

Every session names itself `veyra-workload`.

## Run it

Windows PowerShell 5.1, on any machine that reaches the server. It signs in with Windows authentication unless
`-Credential` names a SQL login, and needs a login that may create a database.

```powershell
powershell -ExecutionPolicy Bypass -File samples\sqlserver-workload\workload.ps1 -Server localhost -Minutes 3
```

Open the server's SQL Server screen in Veyra while it runs. Veyra reads the server every 10 seconds, so the Live tab
shows the blocking chain within a few seconds. Deadlocks reach the Blocking tab within a minute.

## Remove it

```powershell
powershell -ExecutionPolicy Bypass -File samples\sqlserver-workload\workload.ps1 -Server localhost -Cleanup
```

Do not run it against a production server: it holds a lock and uses CPU on purpose.
