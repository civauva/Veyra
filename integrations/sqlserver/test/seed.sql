-- The monitoring login with exactly the permissions docs/integrations/sqlserver.md lists, a database with a full,
-- a differential, and a log backup, one without, and two SQL Agent jobs: one that fails and one that succeeds.
CREATE LOGIN veyra_monitor WITH PASSWORD = '$(MONITOR_PASSWORD)';
GRANT VIEW SERVER STATE TO veyra_monitor;
GRANT VIEW ANY DEFINITION TO veyra_monitor;
GRANT CONNECT ANY DATABASE TO veyra_monitor;
GO

USE msdb;
CREATE USER veyra_monitor FOR LOGIN veyra_monitor;
GRANT SELECT ON dbo.backupset TO veyra_monitor;
GRANT SELECT ON dbo.sysjobs TO veyra_monitor;
GRANT SELECT ON dbo.sysjobhistory TO veyra_monitor;
GRANT EXECUTE ON dbo.agent_datetime TO veyra_monitor;
GRANT SELECT ON dbo.backupmediafamily TO veyra_monitor;
GRANT SELECT ON dbo.sysjobsteps TO veyra_monitor;
GRANT SELECT ON dbo.sysjobactivity TO veyra_monitor;
GRANT SELECT ON dbo.sysjobschedules TO veyra_monitor;
GRANT SELECT ON dbo.sysschedules TO veyra_monitor;
GRANT SELECT ON dbo.suspect_pages TO veyra_monitor;
GRANT SELECT ON dbo.log_shipping_monitor_primary TO veyra_monitor;
GRANT SELECT ON dbo.log_shipping_monitor_secondary TO veyra_monitor;
GO

CREATE DATABASE shop;
CREATE DATABASE reporting;
GO

USE shop;
CREATE TABLE dbo.Orders (Id int IDENTITY PRIMARY KEY, CustomerId int NOT NULL, Status nvarchar(20) NOT NULL);
INSERT dbo.Orders (CustomerId, Status)
SELECT TOP (5000) ABS(CHECKSUM(NEWID())) % 100, N'new'
FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b;
GO

ALTER DATABASE shop SET RECOVERY FULL;
BACKUP DATABASE shop TO DISK = N'/var/opt/mssql/data/shop.bak' WITH INIT;
BACKUP DATABASE shop TO DISK = N'/var/opt/mssql/data/shop_diff.bak' WITH DIFFERENTIAL, INIT;
BACKUP LOG shop TO DISK = N'/var/opt/mssql/data/shop.trn' WITH INIT;
GO

USE msdb;
EXEC dbo.sp_add_job @job_name = N'Nightly full backup - reporting';
EXEC dbo.sp_add_jobstep
  @job_name = N'Nightly full backup - reporting',
  @step_name = N'backup',
  @subsystem = N'TSQL',
  @command = N'RAISERROR(N''The backup device is full'', 16, 1);';
EXEC dbo.sp_add_jobserver @job_name = N'Nightly full backup - reporting';

EXEC dbo.sp_add_job @job_name = N'Purge sessions';
EXEC dbo.sp_add_jobstep
  @job_name = N'Purge sessions',
  @step_name = N'purge',
  @subsystem = N'TSQL',
  @command = N'WAITFOR DELAY ''00:00:01'';';
EXEC dbo.sp_add_jobserver @job_name = N'Purge sessions';
GO

EXEC msdb.dbo.sp_start_job N'Nightly full backup - reporting';
EXEC msdb.dbo.sp_start_job N'Purge sessions';
GO
