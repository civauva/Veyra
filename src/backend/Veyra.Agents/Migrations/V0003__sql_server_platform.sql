-- Azure SQL Managed Instance and Azure SQL Database as monitored systems (VEY-57, ADR 0020).
ALTER TABLE agents.sql_server
    -- Veyra.Agents.SqlServers.SqlServerPlatform: 1 SQL Server, 2 Managed Instance, 3 Azure SQL Database. A server
    -- that existed before this column is a SQL Server.
    ADD COLUMN platform      smallint NOT NULL DEFAULT 1,
    -- The one database an Azure SQL Database is read in. Null means the server as a whole; required for platform 3.
    ADD COLUMN database_name text;
