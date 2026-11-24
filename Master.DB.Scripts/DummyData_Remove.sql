/*
    DummyData_Remove.sql - takes out what DummyData_Seed.sql put into TEAL_Master, and
    nothing else: the alarms by the ID range it recorded, the units by their "DMY" serial
    numbers, the health rows inside the seeded period (the seed refuses to run when real
    ones are there). Then drops dbo.DummyDataSeed, so the seed can be run again.

    The current shift's row in [ShiftData] may still hold figures worked out from the dummy
    data; Master.Scheduler works it out again at its next refresh (StatusRefreshSeconds).

    Run: sqlcmd -S <server> -E -b -i DummyData_Remove.sql   (or open it in SSMS and Execute)
*/
USE [TEAL_Master];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.DummyDataSeed', N'U') IS NULL
BEGIN
    PRINT 'No dummy data recorded in TEAL_Master (dbo.DummyDataSeed does not exist): nothing to remove.';
    RETURN;
END;

DECLARE @SeededAt datetime, @From datetime, @AlarmFrom int, @AlarmTo int;
SELECT TOP 1 @SeededAt = SeededAt, @From = FromTime, @AlarmFrom = AlarmIdFrom, @AlarmTo = AlarmIdTo FROM dbo.DummyDataSeed;

DECLARE @alarms int = 0, @units int = 0, @health int = 0;

BEGIN TRANSACTION;

IF @AlarmFrom IS NOT NULL
BEGIN
    DELETE dbo.Alarms WHERE ID BETWEEN @AlarmFrom AND @AlarmTo AND AlarmDescription LIKE '%[[]dummy]';
    SET @alarms = @@ROWCOUNT;
END;

DELETE dbo.ProductionData WHERE ProductCode LIKE 'DMY%';
SET @units = @@ROWCOUNT;

DELETE dbo.HealthData WHERE StartTime >= @From AND EndTime <= @SeededAt;
SET @health = @@ROWCOUNT;

DROP TABLE dbo.DummyDataSeed;

COMMIT TRANSACTION;

PRINT 'Removed ' + CAST(@health AS varchar(10)) + ' health rows, ' + CAST(@alarms AS varchar(10)) + ' alarms, '
    + CAST(@units AS varchar(10)) + ' units.';
GO
