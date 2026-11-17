-- An agent on a Linux host can read its Docker containers (VEY-32, ADR 0018 and 0019).
ALTER TABLE agents.agent
    -- Read the containers' stats and logs. Only with watch_host, and only on Linux.
    ADD COLUMN watch_docker boolean NOT NULL DEFAULT false;
