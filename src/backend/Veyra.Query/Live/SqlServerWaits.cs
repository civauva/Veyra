using Veyra.Query.Live.Api;

namespace Veyra.Query.Live;

/// <summary>
/// SQL Server's wait types explained (ADR 0020, area 2): the category each belongs to, a few words for the Simple
/// view, and what it means and usually causes for the popup that explains it.
/// </summary>
/// <remarks>
/// Known types are listed by name; the rest are grouped by their prefix, which SQL Server keeps consistent: every
/// <c>LCK_M_</c> type is a lock, every <c>PAGEIOLATCH_</c> type a read from disk. A type neither matches is "Other",
/// with its name as its words.
/// </remarks>
internal static class SqlServerWaits
{
    private static readonly Dictionary<string, (string Category, string Simple, string Meaning, string Causes)> _known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["WRITELOG"] = (
                "Log write",
                "waits for its changes to be written to the log",
                "The request waits until its transaction's log records are on disk. Every commit does.",
                "A slow log disk, many small transactions committed one by one, or a log on the same disk as data."),
            ["SOS_SCHEDULER_YIELD"] = (
                "CPU",
                "waits for its turn on a processor",
                "The request used its slice of CPU time and waits to be scheduled again.",
                "Busy processors: queries that read much more than they need, or too few CPUs for the load."),
            ["CXPACKET"] = (
                "Parallelism",
                "waits for other threads of the same query",
                "The query runs in parallel and one thread waits for the others to finish their part.",
                "Large scans split unevenly, or a cost threshold for parallelism set too low."),
            ["CXCONSUMER"] = (
                "Parallelism",
                "waits for other threads of the same query",
                "A consumer thread of a parallel query waits for rows from the producers. Mostly harmless.",
                "Parallel plans on large reads; worth a look only together with high CXPACKET waits."),
            ["CXSYNC_PORT"] = (
                "Parallelism",
                "waits for other threads of the same query",
                "Threads of a parallel query wait to synchronise with each other.",
                "Parallel plans, often with skewed data between threads."),
            ["ASYNC_NETWORK_IO"] = (
                "Network",
                "waits for the application to take its rows",
                "SQL Server has rows ready and waits for the client to read them.",
                "An application that reads row by row while doing other work, or a slow network to the client."),
            ["RESOURCE_SEMAPHORE"] = (
                "Memory",
                "waits for memory to run",
                "The query needs a memory grant for sorting or hashing and waits until enough memory is free.",
                "Queries that ask for far more memory than they use, from bad row estimates, or too little memory."),
            ["RESOURCE_SEMAPHORE_QUERY_COMPILE"] = (
                "Memory",
                "waits for memory to compile",
                "The query waits for memory to compile its plan.",
                "Many large ad hoc queries compiled at once."),
            ["THREADPOOL"] = (
                "CPU",
                "waits for a worker thread",
                "SQL Server has no free worker thread to run the request. Serious: new work cannot start.",
                "A long blocking chain holding many workers, or very many parallel queries."),
            ["LOGBUFFER"] = (
                "Log write",
                "waits for room in the log buffer",
                "The request waits for space in the in-memory log buffer.",
                "A log disk that cannot keep up with the changes."),
            ["IO_COMPLETION"] = (
                "Disk I/O",
                "waits for the disk",
                "The request waits for a non-data disk operation, such as a sort or hash spilled to tempdb.",
                "Spills to tempdb or a slow disk."),
            ["ASYNC_IO_COMPLETION"] = (
                "Disk I/O",
                "waits for the disk",
                "The request waits for a large disk operation, usually a backup, restore, or file growth.",
                "Backups and file growth; a slow disk."),
            ["BACKUPIO"] = (
                "Backup",
                "waits for the backup's disk",
                "A backup or restore waits for its target to read or write.",
                "A slow backup target, often a network share."),
            ["HADR_SYNC_COMMIT"] = (
                "Availability",
                "waits for a replica to confirm its changes",
                "In a synchronous Availability Group, the commit waits until the secondary has hardened the log.",
                "A slow network or a slow log disk on the secondary replica."),
            ["PREEMPTIVE_OS_WRITEFILEGATHER"] = (
                "Disk I/O",
                "waits for a file to grow",
                "SQL Server waits for Windows to zero a new part of a file as it grows.",
                "Large file growths of the log, which is always zeroed, or data without instant file initialization."),
            ["OLEDB"] = (
                "External",
                "waits for a linked server or another provider",
                "The request waits for a call through OLE DB, often a linked server or a DMV that uses one.",
                "Slow linked servers, or some system functions."),
            ["WAITFOR"] = (
                "Idle",
                "waits on purpose",
                "The statement runs WAITFOR DELAY or WAITFOR TIME and waits by design.",
                "Code that pauses on purpose."),
        };

    private static readonly (string Prefix, string Category, string Simple, string Meaning, string Causes)[] _prefixes =
    [
        (
            "LCK_M_",
            "Lock",
            "waits for a lock",
            "The request wants rows, pages, or a table another session has locked, and waits until that session "
            + "commits or rolls back.",
            "Long transactions, transactions an application left open, or missing indexes that make updates lock "
            + "more."),
        (
            "PAGEIOLATCH_",
            "Disk I/O",
            "waits for the disk",
            "The request waits for a data page to be read from disk into memory.",
            "Queries that read more than they need, too little memory for the working set, or a slow disk."),
        (
            "PAGELATCH_",
            "Page contention",
            "waits for a page in memory",
            "Several requests want the same page in memory at once. Not a disk wait.",
            "Many inserts at the end of one index, or tempdb allocation pages under heavy use."),
        (
            "LATCH_",
            "Latch",
            "waits for an internal structure",
            "The request waits for an internal SQL Server structure other than a data page.",
            "Heavy parallelism or busy internal caches."),
        (
            "HADR_",
            "Availability",
            "waits for an availability replica",
            "The request waits for Availability Group work.",
            "Replica network or disk speed."),
        (
            "PREEMPTIVE_",
            "External",
            "waits for Windows",
            "SQL Server called the operating system and waits for it to return.",
            "File operations, authentication, or external calls."),
        (
            "BROKER_",
            "Idle",
            "waits for Service Broker",
            "Service Broker work, usually idle background waiting.",
            "Normally harmless."),
    ];

    /// <summary>Explains a wait type.</summary>
    /// <param name="type">The wait type, such as <c>LCK_M_U</c>.</param>
    /// <returns>Its category, words, meaning, and usual causes.</returns>
    public static WaitTermResponse Explain(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_known.TryGetValue(type, out var known))
        {
            return new WaitTermResponse(type, known.Category, known.Simple, known.Meaning, known.Causes);
        }

        foreach (var (prefix, category, simple, meaning, causes) in _prefixes)
        {
            if (type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new WaitTermResponse(type, category, simple, meaning, causes);
            }
        }

        return new WaitTermResponse(
            type,
            "Other",
            $"waits ({type})",
            $"The request waits on {type}.",
            "See Microsoft's documentation of sys.dm_os_wait_stats for this type.");
    }
}
