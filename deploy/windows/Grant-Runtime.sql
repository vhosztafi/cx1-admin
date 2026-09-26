-- Run as SQL administrator AFTER Initialize-Database.ps1. Review before executing.
-- Local IIS virtual account, dedicated to this app pool; no server role is granted.
USE [master];
IF SUSER_ID(N'IIS APPPOOL\Cx1AdminDev') IS NULL
    CREATE LOGIN [IIS APPPOOL\Cx1AdminDev] FROM WINDOWS;
GO
USE [Cx1_Dev];
IF USER_ID(N'IIS APPPOOL\Cx1AdminDev') IS NULL
    CREATE USER [IIS APPPOOL\Cx1AdminDev] FOR LOGIN [IIS APPPOOL\Cx1AdminDev];
ALTER ROLE [db_datareader] ADD MEMBER [IIS APPPOOL\Cx1AdminDev];
ALTER ROLE [db_datawriter] ADD MEMBER [IIS APPPOOL\Cx1AdminDev];
GRANT EXECUTE TO [IIS APPPOOL\Cx1AdminDev];
GO
