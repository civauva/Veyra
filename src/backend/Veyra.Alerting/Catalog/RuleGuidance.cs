namespace Veyra.Alerting.Catalog;

/// <summary>
/// What each catalog rule's alert means and how to resolve it (VEY-60), for the alert's details. Each rule has an
/// entry; a test keeps it that way. Scripts are for SQL Server Management Studio or sqlcmd, run by a person who
/// understands them, never by Veyra.
/// </summary>
internal static class RuleGuidance
{
    private static readonly Dictionary<string, Guidance> _byRule = new(StringComparer.Ordinal)
    {
        ["db-head-blocker"] = new(
            "One session holds locks that other requests need, and they wait for it. Users see the application hang "
            + "or time out for as long as it lasts.",
            [
                new(
                    "Find the blocked requests and the sessions they wait for, on the server's Blocking tab or with "
                    + "this query. The head blocker is the session the others wait for that waits for nothing itself.",
                    """
                    SELECT r.session_id, r.blocking_session_id, r.wait_type, r.wait_time / 1000 AS wait_seconds,
                           s.login_name, s.host_name, s.program_name, t.text
                    FROM sys.dm_exec_requests AS r
                    JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id
                    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
                    WHERE r.blocking_session_id <> 0;
                    """),
                new(
                    "Ask the head blocker's owner to commit or roll back. As a last resort end it with KILL and its "
                    + "session ID; its work rolls back, which can take as long as it ran."),
                new(
                    "To keep it from coming back, keep transactions short, add the index the blocked statements "
                    + "need, or let readers use read committed snapshot isolation."),
            ]),
        ["db-open-transaction"] = new(
            "A session began a transaction and stopped sending work without committing or rolling back. It keeps its "
            + "locks, so others can block behind it, and the log cannot be reused past it, so the log grows.",
            [
                new(
                    "Find the session, its program, and how long its transaction has been open.",
                    """
                    SELECT s.session_id, s.login_name, s.host_name, s.program_name, s.last_request_end_time,
                           DATEDIFF(MINUTE, t.transaction_begin_time, SYSDATETIME()) AS open_minutes
                    FROM sys.dm_tran_session_transactions AS st
                    JOIN sys.dm_tran_active_transactions AS t ON t.transaction_id = st.transaction_id
                    JOIN sys.dm_exec_sessions AS s ON s.session_id = st.session_id
                    ORDER BY t.transaction_begin_time;
                    """),
                new(
                    "Tell the application's owner. It is usually code that waits for a person inside a transaction, "
                    + "or a connection returned to the pool without a commit. Ending the session with KILL rolls the "
                    + "transaction back."),
                new("To keep it from coming back, commit or roll back in a finally block, and never wait for input "
                    + "inside a transaction."),
            ]),
        ["db-tempdb-session"] = new(
            "One session holds a large share of tempdb for sorts, hashes, temporary tables, or row versions. If "
            + "tempdb fills up, queries in every database on the server fail.",
            [
                new(
                    "Find the sessions that hold the most of tempdb.",
                    """
                    SELECT su.session_id, s.login_name, s.program_name,
                           (su.user_objects_alloc_page_count - su.user_objects_dealloc_page_count
                            + su.internal_objects_alloc_page_count - su.internal_objects_dealloc_page_count)
                           * 8 / 1024 AS used_mb
                    FROM sys.dm_db_session_space_usage AS su
                    JOIN sys.dm_exec_sessions AS s ON s.session_id = su.session_id
                    ORDER BY used_mb DESC;
                    """),
                new(
                    "See what it runs on the Live tab. A big sort or hash usually means a missing index, or a query "
                    + "that reads far more rows than it needs."),
                new(
                    "If tempdb is close to full, end the session with KILL; its space is freed once it rolls back. "
                    + "Give tempdb a volume of its own, so one query cannot fill the databases' disk."),
            ]),
        ["db-plan-regression"] = new(
            "A statement got a new execution plan that is much slower than the one it had before, often after "
            + "statistics changed, an index was dropped, or it ran with unusual parameters. The application slows "
            + "down although its code did not change.",
            [
                new("Open the statement from the server's Queries tab and compare its plans."),
                new(
                    "With Query Store on, force the earlier, faster plan, in the statement's database. The query and "
                    + "plan IDs are on the statement's page.",
                    "EXEC sys.sp_query_store_force_plan @query_id = <query_id>, @plan_id = <plan_id>;"),
                new(
                    "Then find out why the plan changed: update the statistics of the tables it reads, or restore the "
                    + "index. Unforce the plan once the statement finds a good plan by itself."),
            ]),
        ["db-offline"] = new(
            "{part} is not online: it is restoring, recovering, suspect, offline, or in emergency mode. Applications "
            + "cannot use it until it is online again.",
            [
                new(
                    "See its state.",
                    "SELECT name, state_desc, user_access_desc FROM sys.databases WHERE name = N'{part}';"),
                new(
                    "Recovering after a restart: wait, and follow its progress in SQL Server's error log. Restoring: "
                    + "if no more backups are to be applied, finish the restore.",
                    "RESTORE DATABASE [{part}] WITH RECOVERY;"),
                new(
                    "Suspect or emergency: its files are damaged or missing. Read the error log for the cause first, "
                    + "do not detach it, and restore it from the last good backup."),
                new("Taken offline on purpose: bring it back online.", "ALTER DATABASE [{part}] SET ONLINE;"),
            ]),
        ["db-log-full"] = new(
            "The transaction log of {part} is nearly full. When it is full, every insert, update, and delete in the "
            + "database fails until there is room again.",
            [
                new(
                    "See why its log cannot be reused.",
                    "SELECT name, recovery_model_desc, log_reuse_wait_desc FROM sys.databases WHERE name = N'{part}';"),
                new(
                    "LOG_BACKUP: take a log backup now, and schedule one every 15 to 60 minutes. ACTIVE_TRANSACTION: "
                    + "find and end the long transaction. AVAILABILITY_REPLICA or REPLICATION: a reader of the log is "
                    + "behind.",
                    "BACKUP LOG [{part}] TO DISK = N'<backup folder>\\{part}_log.trn' WITH CHECKSUM;"),
                new(
                    "If its volume has room, let the log grow. Do not shrink it right after; it only grows again, in "
                    + "small steps that slow it down."),
            ]),
        ["db-vlf-count"] = new(
            "The log of {part} is made of very many small virtual log files, usually from growing in many small "
            + "steps. Start-up, restores, and catching up after a failover get slow.",
            [
                new(
                    "After its next log backup, shrink the log to a small size. Find the log file first.",
                    """
                    USE [{part}];
                    SELECT file_id, name, size / 128 AS size_mb FROM sys.database_files WHERE type_desc = N'LOG';
                    -- then, with the log's file_id:
                    DBCC SHRINKFILE (<file_id>, 1024);
                    """),
                new(
                    "Grow it back to the size it needs in one step, with a fixed growth rather than a percentage.",
                    """
                    ALTER DATABASE [{part}]
                    MODIFY FILE (NAME = N'<log file name>', SIZE = 8192MB, FILEGROWTH = 1024MB);
                    """),
            ]),
        ["db-volume-space"] = new(
            "Volume {part}, which holds database files, is nearly full. When it is full, files cannot grow, and the "
            + "inserts or log writes that need room fail.",
            [
                new(
                    "See which database files are on it, their size, and how they grow.",
                    """
                    SELECT DB_NAME(database_id) AS database_name, name, physical_name, size / 128 AS size_mb,
                           growth, is_percent_growth
                    FROM sys.master_files
                    WHERE physical_name LIKE N'{part}%'
                    ORDER BY size DESC;
                    """),
                new("Free room: remove old backups and other files from the volume, or make the disk larger."),
                new(
                    "Move a large or fast-growing database to another volume, or cap a file with MAXSIZE so that one "
                    + "database cannot fill the volume for all."),
            ]),
        ["db-log-backup-age"] = new(
            "{part} is in full or bulk-logged recovery but has had no log backup for a while. A restore would lose "
            + "everything since its last one, and its log keeps growing until the next.",
            [
                new(
                    "Check the SQL Server Agent job that backs up its log: is it enabled, did it fail, does Agent "
                    + "run? The server's Backups and Agent tabs show it."),
                new(
                    "Take a log backup now.",
                    "BACKUP LOG [{part}] TO DISK = N'<backup folder>\\{part}_log.trn' WITH CHECKSUM;"),
                new(
                    "If the database does not need restores to a point in time, switch it to simple recovery instead.",
                    "ALTER DATABASE [{part}] SET RECOVERY SIMPLE;"),
            ]),
        ["db-log-chain"] = new(
            "The log backups of {part} have a gap since its last full backup, from a log backup that was deleted or "
            + "taken elsewhere, or a switch to simple recovery. A restore stops at the gap; later log backups cannot "
            + "be applied.",
            [
                new(
                    "Take a full backup now. It starts a new chain that the log backups follow.",
                    "BACKUP DATABASE [{part}] TO DISK = N'<backup folder>\\{part}.bak' WITH CHECKSUM;"),
                new(
                    "Find what broke it: a backup by another tool, to an unknown place, or a change of recovery model "
                    + "in SQL Server's error log.",
                    """
                    SELECT b.backup_start_date, b.type, b.first_lsn, b.last_lsn, m.physical_device_name
                    FROM msdb.dbo.backupset AS b
                    JOIN msdb.dbo.backupmediafamily AS m ON m.media_set_id = b.media_set_id
                    WHERE b.database_name = N'{part}'
                    ORDER BY b.backup_start_date DESC;
                    """),
                new("Take one-off backups WITH COPY_ONLY, so that they do not take part in the chain."),
            ]),
        ["db-checkdb-age"] = new(
            "DBCC CHECKDB has not passed on {part} for a while. It is the only check that reads every page for "
            + "corruption: without it, damage from a disk or a driver goes unnoticed until a query fails, and every "
            + "backup taken since may hold the same damage.",
            [
                new(
                    "Run the check now, outside busy hours; it reads the whole database.",
                    "DBCC CHECKDB (N'{part}') WITH NO_INFOMSGS, ALL_ERRORMSGS;"),
                new(
                    "No output means it passed, and the alert clears at Veyra's next read. If it reports errors, do "
                    + "not repair first: restore the damaged pages or the database from the last good backup, and "
                    + "look for the cause in the storage and the Windows event log."),
                new(
                    "Schedule it weekly in a SQL Server Agent job, such as a maintenance plan with Check Database "
                    + "Integrity or Ola Hallengren's DatabaseIntegrityCheck. For a very large database, run it WITH "
                    + "PHYSICAL_ONLY on weekdays and in full at the weekend."),
            ]),
        ["db-damaged-pages"] = new(
            "SQL Server read pages of {part} that were damaged (errors 823, 824, or 825) and recorded them in "
            + "msdb.dbo.suspect_pages. The data on them may be lost, and the storage under them may be failing.",
            [
                new(
                    "See which pages.",
                    """
                    SELECT file_id, page_id, event_type, error_count, last_update_date
                    FROM msdb.dbo.suspect_pages
                    WHERE database_id = DB_ID(N'{part}');
                    """),
                new(
                    "Learn how far the damage goes.",
                    "DBCC CHECKDB (N'{part}') WITH NO_INFOMSGS, ALL_ERRORMSGS;"),
                new(
                    "Restore the damaged pages, or the database, from the last good backup; pages restored are no "
                    + "longer counted. Check the disks, the controller, and their drivers: damaged pages usually mean "
                    + "failing storage."),
            ]),
        ["db-cpu"] = new(
            "SQL Server uses most of the machine's CPU. Queries queue for the processor, and everything on the "
            + "server slows down.",
            [
                new(
                    "Find the queries using the most CPU: now on the Live tab, over the last hour on the Queries tab, "
                    + "or with this query.",
                    """
                    SELECT TOP (10) r.session_id, r.cpu_time, r.total_elapsed_time,
                           DB_NAME(r.database_id) AS database_name, t.text
                    FROM sys.dm_exec_requests AS r
                    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
                    ORDER BY r.cpu_time DESC;
                    """),
                new(
                    "Look at their plans for scans of large tables, missing indexes, or implicit conversions. A plan "
                    + "regression is a common cause."),
                new(
                    "If the load is real and lasting, the server needs more cores. Check max degree of parallelism "
                    + "and cost threshold for parallelism first."),
            ]),
        ["db-cpu-other"] = new(
            "Processes other than SQL Server take a large share of the machine's CPU, so SQL Server's queries wait "
            + "for the processor.",
            [
                new(
                    "On the machine, find them in Task Manager (Details, by CPU), or with top on Linux. Antivirus "
                    + "scans, backups by other tools, and other services are the usual ones."),
                new(
                    "Exclude the database, log, and backup files from antivirus scans, as Microsoft recommends, and "
                    + "move other heavy work off the database server or out of busy hours."),
            ]),
        ["db-workers"] = new(
            "Nearly all of SQL Server's worker threads are in use. When none is free, new connections and queries "
            + "wait (THREADPOOL), and the server seems to hang.",
            [
                new(
                    "Look for blocking first: every blocked request holds a worker. The Blocking tab shows it."),
                new(
                    "Find the sessions holding the most workers.",
                    """
                    SELECT s.session_id, s.login_name, s.program_name, COUNT(*) AS workers
                    FROM sys.dm_os_tasks AS t
                    JOIN sys.dm_exec_sessions AS s ON s.session_id = t.session_id
                    GROUP BY s.session_id, s.login_name, s.program_name
                    ORDER BY workers DESC;
                    """),
                new(
                    "Parallel queries take many workers each: raise cost threshold for parallelism, or lower max "
                    + "degree of parallelism. Do not raise max worker threads before the cause is found."),
            ]),
        ["db-grants-pending"] = new(
            "Queries wait for memory to sort or hash before they can start (RESOURCE_SEMAPHORE). They look slow or "
            + "hung although the CPU may be idle.",
            [
                new(
                    "See which queries asked for how much.",
                    """
                    SELECT session_id, requested_memory_kb / 1024 AS requested_mb,
                           granted_memory_kb / 1024 AS granted_mb, wait_time_ms, query_cost
                    FROM sys.dm_exec_query_memory_grants
                    ORDER BY requested_memory_kb DESC;
                    """),
                new(
                    "The queries asking for the most usually estimate their rows badly: update their statistics, or "
                    + "add the index that saves the sort."),
                new(
                    "Check max server memory: it should leave the operating system room and give SQL Server the "
                    + "rest."),
            ]),
        ["db-os-memory-low"] = new(
            "The operating system reports that the machine is low on memory. SQL Server shrinks its caches in "
            + "response, so queries read from disk more and slow down.",
            [
                new(
                    "Check max server memory. It should leave the operating system and other programs 10 to 20% of "
                    + "the machine's memory.",
                    "EXEC sys.sp_configure N'max server memory (MB)';"),
                new("On the machine, find other programs using memory: Task Manager, or top on Linux."),
                new("If the load needs it, give the machine more memory."),
            ]),
        ["db-memory-dump"] = new(
            "SQL Server hit a serious internal error and wrote a memory dump. It may have carried on, but the same "
            + "error can come back and can stop the service.",
            [
                new(
                    "Find the dump, then read SQL Server's error log around its time.",
                    """
                    SELECT filename, creation_time, size_in_bytes
                    FROM sys.dm_server_memory_dumps
                    ORDER BY creation_time DESC;
                    """),
                new("Check the cumulative update level: many dumps come from known bugs fixed in a later update."),
                new("If it happens again, open a support case with Microsoft and send the dump."),
            ]),
        ["db-agent-stopped"] = new(
            "The SQL Server Agent service does not run, so no job runs: no backups, no integrity checks, and no "
            + "maintenance.",
            [
                new(
                    "Start it, in SQL Server Configuration Manager or in PowerShell on the server. A named instance's "
                    + "service is SQLAgent$ and its name.",
                    "Start-Service -Name SQLSERVERAGENT"),
                new("Set its start mode to Automatic, so that it starts with the machine."),
                new(
                    "If it stops again, read its log, SQLAGENT.OUT in the instance's Log folder. SQL Server Express "
                    + "has no Agent: schedule its backups with the Windows Task Scheduler."),
            ]),
        ["db-restart"] = new(
            "SQL Server started in the last 15 minutes. Every connection was dropped, anything running was rolled "
            + "back, and the caches start empty, so the first queries are slower.",
            [
                new(
                    "Find out whether it was planned: patching, a failover, or a restart of the machine. If not, read "
                    + "SQL Server's error log and the Windows system log from just before."),
                new("Check that every database came back online and that SQL Server Agent runs."),
            ]),
        ["db-unsupported"] = new(
            "This SQL Server version is past Microsoft's end of extended support. It gets no security fixes, and "
            + "Microsoft support no longer helps with it.",
            [
                new("Plan an upgrade to a supported version: SQL Server 2019, 2022, or 2025, or Azure SQL."),
                new(
                    "Before it, check the databases with Microsoft's Data Migration Assistant, and test the "
                    + "applications on the new version."),
            ]),
        ["db-job-overrun"] = new(
            "SQL Agent job {part} has run much longer than usual. It may be blocked, working on far more data, or "
            + "stuck, and its next runs can pile up behind it.",
            [
                new("See which step it is on and what it waits for, on the server's Agent and Live tabs."),
                new(
                    "Blocked: resolve the head blocker. Stuck: stop it in the Job Activity Monitor or with this.",
                    "EXEC msdb.dbo.sp_stop_job @job_name = N'{part}';"),
                new(
                    "If it only has more to do now, its usual duration follows. Move its schedule so that it does not "
                    + "run into busy hours."),
            ]),
        ["db-job-late"] = new(
            "SQL Agent job {part} did not start within 15 minutes of its scheduled time, so whatever it does, such as "
            + "a backup, did not happen.",
            [
                new(
                    "Check that SQL Server Agent runs, and that the job and its schedule are enabled.",
                    """
                    SELECT j.name, j.enabled, s.name AS schedule_name, s.enabled AS schedule_enabled,
                           js.next_run_date, js.next_run_time
                    FROM msdb.dbo.sysjobs AS j
                    JOIN msdb.dbo.sysjobschedules AS js ON js.job_id = j.job_id
                    JOIN msdb.dbo.sysschedules AS s ON s.schedule_id = js.schedule_id
                    WHERE j.name = N'{part}';
                    """),
                new("A run still going from before skips the next start: look for a Job running long alert."),
                new("Start it now if it must run.", "EXEC msdb.dbo.sp_start_job @job_name = N'{part}';"),
            ]),
        ["db-severe-error"] = new(
            "SQL Server recorded errors of severity 20 or above, or errors 823, 824, or 825 about pages it could not "
            + "read. They come from damaged data, failing storage, or serious internal faults, and the session that "
            + "hit them was ended. A client's connection that broke while it logged in, such as error 17830, is not "
            + "counted.",
            [
                new(
                    "Read each error the alert counted, listed below with its number and message, or every recent "
                    + "event on the server's Events tab. They come from SQL Server's system_health session, and many "
                    + "are not in its error log; this query reads them there.",
                    """
                    SELECT x.value('(@timestamp)[1]', 'datetime2') AS at_utc,
                           x.value('(data[@name="error_number"]/value)[1]', 'int') AS error_number,
                           x.value('(data[@name="severity"]/value)[1]', 'int') AS severity,
                           x.value('(data[@name="message"]/value)[1]', 'nvarchar(max)') AS message
                    FROM (
                        SELECT CAST(t.target_data AS xml) AS data
                        FROM sys.dm_xe_session_targets AS t
                        JOIN sys.dm_xe_sessions AS s ON s.address = t.event_session_address
                        WHERE s.name = N'system_health' AND t.target_name = N'ring_buffer'
                    ) AS ring
                    CROSS APPLY ring.data.nodes('RingBufferTarget/event[@name="error_reported"]') AS e(x)
                    ORDER BY at_utc DESC;
                    """),
                new(
                    "Errors 823, 824, or 825: run DBCC CHECKDB on the database they name, and check the disks and "
                    + "their drivers."),
                new("Other errors: look the number up in Microsoft's documentation; many are fixed by an update."),
            ]),
        ["db-memory-error"] = new(
            "SQL Server could not get memory for some work, such as error 701: there is insufficient system memory "
            + "to run the query. Those queries failed.",
            [
                new(
                    "Read each error the alert counted, listed below, then the memory status SQL Server wrote around "
                    + "them in its error log.",
                    "EXEC sys.xp_readerrorlog 0, 1, N'memory', NULL, NULL, NULL, N'DESC';"),
                new(
                    "See what holds SQL Server's memory.",
                    """
                    SELECT TOP (10) type, SUM(pages_kb) / 1024 AS mb
                    FROM sys.dm_os_memory_clerks
                    GROUP BY type
                    ORDER BY mb DESC;
                    """),
                new(
                    "Check max server memory and what else uses the machine's memory. A single query with a huge "
                    + "memory grant can cause it too."),
            ]),
        ["db-login-failures"] = new(
            "Logins to the server failed many times in the last 15 minutes. Usually an application has a wrong or "
            + "expired password; sometimes someone is guessing passwords.",
            [
                new(
                    "See the failures the alert counted, listed below. The error log says which login failed, from "
                    + "which address, and why: the state of error 18456 says why.",
                    "EXEC sys.xp_readerrorlog 0, 1, N'Login failed', NULL, NULL, NULL, N'DESC';"),
                new(
                    "Wrong or expired password: fix the application's connection string or the login. A missing "
                    + "database: the login's default database was removed or renamed."),
                new(
                    "Many failures from an unknown address: block it at the firewall, and do not expose SQL Server "
                    + "to the internet."),
            ]),
        ["db-ag-unhealthy"] = new(
            "Replica {part} of an Availability Group is not healthy: it is disconnected, or its databases do not "
            + "synchronize. A failover to it could lose data or not be possible.",
            [
                new(
                    "See its state, in the Availability Group dashboard or with this query.",
                    """
                    SELECT ar.replica_server_name, rs.role_desc, rs.connected_state_desc,
                           rs.synchronization_health_desc, rs.last_connect_error_description
                    FROM sys.dm_hadr_availability_replica_states AS rs
                    JOIN sys.availability_replicas AS ar ON ar.replica_id = rs.replica_id;
                    """),
                new(
                    "Disconnected: check that the replica's machine and SQL Server run, and that the endpoint's port, "
                    + "5022 by default, is open between the replicas."),
                new("Suspended databases: resume their data movement on the replica."),
            ]),
        ["db-ag-queue"] = new(
            "Database {part} has log its secondary replica has still to receive or to redo. A failover now would lose "
            + "the log not sent, or wait for the redo, and a readable secondary shows old data.",
            [
                new(
                    "See which queue grows.",
                    """
                    SELECT DB_NAME(database_id) AS database_name, log_send_queue_size / 1024 AS send_mb,
                           redo_queue_size / 1024 AS redo_mb, log_send_rate, redo_rate
                    FROM sys.dm_hadr_database_replica_states
                    WHERE database_id = DB_ID(N'{part}');
                    """),
                new(
                    "The send queue grows when the network between the replicas is too slow for what the primary "
                    + "writes. The redo queue grows when the secondary's disks or CPU are too slow, or a long query on "
                    + "it blocks the redo."),
                new("Index rebuilds write a lot of log at once: run them outside busy hours, or in smaller parts."),
            ]),
        ["db-ag-sync"] = new(
            "Database {part} of an Availability Group is not synchronizing: its data movement is suspended or "
            + "failed. Its secondary falls further behind and cannot take over without losing data.",
            [
                new(
                    "See its state and why it was suspended.",
                    """
                    SELECT DB_NAME(database_id) AS database_name, synchronization_state_desc, is_suspended,
                           suspend_reason_desc
                    FROM sys.dm_hadr_database_replica_states
                    WHERE database_id = DB_ID(N'{part}');
                    """),
                new(
                    "Resume its data movement, on the replica where it is suspended.",
                    "ALTER DATABASE [{part}] SET HADR RESUME;"),
                new("If it suspends again, the error log says why: often a full log or disk on the secondary."),
            ]),
        ["db-ag-failover"] = new(
            "The primary of availability group {part} moved to or from this server in the last 15 minutes. "
            + "Applications reconnected, and what was running was rolled back.",
            [
                new(
                    "Find out whether it was planned. If not, the cluster log and SQL Server's error log on the old "
                    + "primary say why: a failed health check, a lost machine, or the network."),
                new(
                    "Check that the databases synchronize again on the new secondary, and that applications connect "
                    + "through the listener."),
            ]),
        ["db-logshipping"] = new(
            "Log shipping of {part} is behind: its backup, copy, or restore has not run within its threshold. The "
            + "secondary has older data, and a failover to it would lose more.",
            [
                new(
                    "Check the log shipping jobs in SQL Server Agent, the backup job on the primary and the copy and "
                    + "restore jobs on the secondary: did they fail, are they enabled?",
                    "EXEC master.dbo.sp_help_log_shipping_monitor;"),
                new(
                    "A missing or damaged backup file stops the restores: restore a new full backup on the secondary "
                    + "WITH NORECOVERY to start again."),
            ]),
    };

    /// <summary>The guidance of a catalog rule.</summary>
    /// <param name="rule">The rule's name, such as <c>db-checkdb-age</c>.</param>
    /// <returns>Its guidance.</returns>
    /// <exception cref="KeyNotFoundException">The rule has no guidance, which a test prevents.</exception>
    public static Guidance For(string rule) => _byRule[rule];
}
