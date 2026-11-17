-- An agent can watch the machine it runs on (VEY-35, ADR 0018 and 0019).
ALTER TABLE agents.agent
    -- Read the host's CPU, memory, disks, and network.
    ADD COLUMN watch_host        boolean NOT NULL DEFAULT false,
    -- Read the Windows Event Log's errors. Only with watch_host, and only on Windows.
    ADD COLUMN watch_event_log   boolean NOT NULL DEFAULT false,
    -- Read IIS. Only with watch_host, and only on Windows.
    ADD COLUMN watch_iis         boolean NOT NULL DEFAULT false,
    -- The environment the host belongs to; it becomes the host's deployment environment in its telemetry.
    ADD COLUMN watch_environment text    NOT NULL DEFAULT 'production';
