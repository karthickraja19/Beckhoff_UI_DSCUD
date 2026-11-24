/*
    DummyData_Seed.sql - fills TEAL_Master with seven days of made-up data, to check the
    screens that read the database without the machine running:

      [HealthData]      Home > State Changes (running / idle / downtime per day)
      [Alarms]          Home > Error Statistics, Alarm page (history, Downtime Top 10, pie)
      [ProductionData]  Home > I/O summary and last unit, Data page (Yield, Tossing, hourly)

    From 00:00 six days ago up to now: run, idle and down stretches split at the shift
    boundaries of [dbo].[Shift] (as MachineHealthRecorder splits them), now and then a gap
    (HMI closed); one alarm for each downtime, with the PLC's own codes and the machine's
    device names, its description ending in "[dummy]"; units only while running, every
    9-12 s, serial numbers "DMY" + yyyyMMddHHmmss, about 3.5 % NG, some retests and tossed.
    The random numbers have a fixed seed, so a run gives the same pattern each time.

    Remove it again with DummyData_Remove.sql: dbo.DummyDataSeed records what was added.
    Refuses to run twice, and refuses when [HealthData] already has rows in the seven days,
    so the remove script never deletes a real row.

    Run: sqlcmd -S <server> -E -b -i DummyData_Seed.sql   (or open it in SSMS and Execute)
*/
USE [TEAL_Master];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.DummyDataSeed', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'Dummy data is already in TEAL_Master (dbo.DummyDataSeed exists). Run DummyData_Remove.sql first.', 16, 1);
    RETURN;
END;

DECLARE @Now  datetime = DATEADD(MILLISECOND, -DATEPART(MILLISECOND, GETDATE()), GETDATE());
DECLARE @From datetime = DATEADD(DAY, -6, CAST(CAST(@Now AS date) AS datetime));
DECLARE @SeedRandom float = RAND(20261118);

IF EXISTS (SELECT 1 FROM dbo.HealthData WHERE StartTime < @Now AND EndTime > @From)
BEGIN
    RAISERROR(N'[HealthData] already has rows in the last seven days: not seeding, so the remove script cannot delete real rows.', 16, 1);
    RETURN;
END;

DECLARE @Shifts TABLE (Name varchar(15) NOT NULL, StartT time(0) NOT NULL, EndT time(0) NOT NULL);
INSERT @Shifts (Name, StartT, EndT)
SELECT Shift, StartTime, EndTime FROM dbo.Shift
WHERE Shift IS NOT NULL AND StartTime IS NOT NULL AND EndTime IS NOT NULL AND StartTime <> EndTime;

IF NOT EXISTS (SELECT 1 FROM @Shifts)
BEGIN
    RAISERROR(N'[dbo].[Shift] has no shifts: fill it first (Shift_default_data.sql).', 16, 1);
    RETURN;
END;

-- What a downtime alarm can be: the PLC's codes, the machine's devices (Master.Configuration\Enum).
-- Rows repeat so some causes come up more often (Downtime Top 10).
DECLARE @Causes TABLE (i int IDENTITY(0, 1), Code varchar(10), AlarmType varchar(50), Device varchar(60), Event varchar(60));
INSERT @Causes (Code, AlarmType, Device, Event) VALUES
    ('E101', 'servo error',    'UC_Loading_X_Axis_Gantry',           'ServoEnabledFailed : 19209'),
    ('E101', 'servo error',    'Station_1_Dispensing_Gantry_X_Axis', 'ServoEnabledFailed : 19209'),
    ('E102', 'servo error',    'Station_1_Vision_Gantry_Z_Axis',     'HomingTimeout'),
    ('E102', 'servo error',    'UC_Loading_Z_Axis_Gantry_Servo',     'HomingTimeout'),
    ('E107', 'cylinder error', 'TC_UC_Ent_Stopper_Cyl',              'WorkSensorTimeout'),
    ('E107', 'cylinder error', 'TC_UC_Ent_Stopper_Cyl',              'WorkSensorTimeout'),
    ('E107', 'cylinder error', 'TC_UC_Ld_Lifter_Cyl',                'WorkSensorTimeout'),
    ('E108', 'cylinder error', 'TC_Int_Stopper1_Cyl',                'HomeSensorTimeout'),
    ('E116', 'vacuum error',   'BTH_PnP_Vacuum',                     'VacuumNotReached'),
    ('E116', 'vacuum error',   'BTH_PnP_Vacuum',                     'VacuumNotReached'),
    ('E116', 'vacuum error',   'BTH_Ins_Fix_Vacuum',                 'VacuumNotReached'),
    ('E118', 'stepper error',  'LoadingConveyor',                    'ConveyorJam'),
    ('E118', 'stepper error',  'WorkingConveyor',                    'ConveyorJam'),
    ('E119', 'vision error',   'CCD1',                               'ResultTimeout'),
    ('E119', 'vision error',   'CCD2',                               'ResultTimeout'),
    ('E120', 'vision error',   'CCD3',                               'Disconnected');
DECLARE @CauseCount int = (SELECT COUNT(*) FROM @Causes);

DECLARE @t datetime = @From, @segStart datetime, @segEnd datetime, @end datetime, @bound datetime,
        @cat varchar(10), @r float, @dur int, @shift varchar(15), @sdate date, @tod time(0), @d date,
        @u datetime, @ct float, @pass bit, @iter int, @toss bit, @cause int,
        @firstAlarm int = NULL, @lastAlarm int = NULL, @units int = 0, @segments int = 0;

BEGIN TRANSACTION;

WHILE @t < @Now
BEGIN
    -- The next stretch: 67 % running 20-90 min, 18 % idle 5-30 min, 12 % down 3-25 min, 3 % no record 30-120 min.
    SET @r = RAND();
    IF @r < 0.03       SELECT @cat = 'GAP',      @dur = 1800 + CAST(RAND() * 5400 AS int);
    ELSE IF @r < 0.70  SELECT @cat = 'RunTime',  @dur = 1200 + CAST(RAND() * 4200 AS int);
    ELSE IF @r < 0.88  SELECT @cat = 'IdleTime', @dur =  300 + CAST(RAND() * 1500 AS int);
    ELSE               SELECT @cat = 'DownTime', @dur =  180 + CAST(RAND() * 1320 AS int);

    SET @segStart = @t;
    SET @segEnd = DATEADD(SECOND, @dur, @t);
    IF @segEnd > @Now SET @segEnd = @Now;

    -- One alarm for the whole downtime, in the shift it started in.
    IF @cat = 'DownTime'
    BEGIN
        SET @tod = CAST(@t AS time(0)); SET @d = CAST(@t AS date); SET @shift = NULL;
        SELECT TOP 1 @shift = Name FROM @Shifts
        WHERE (StartT < EndT AND @tod >= StartT AND @tod < EndT) OR (StartT > EndT AND (@tod >= StartT OR @tod < EndT));

        SET @cause = CAST(RAND() * @CauseCount AS int);
        INSERT dbo.Alarms (AlarmCode, AlarmDescription, AlarmType, Shift, StartTime, EndTime)
        SELECT Code, 'MACHINE / ' + Device + ' : ' + Event + ' [dummy]', AlarmType, @shift, @segStart, @segEnd
        FROM @Causes WHERE i = @cause;

        SET @lastAlarm = SCOPE_IDENTITY();
        IF @firstAlarm IS NULL SET @firstAlarm = @lastAlarm;
    END;

    -- Split at every shift boundary: each piece is counted to its own shift and shift date.
    WHILE @t < @segEnd
    BEGIN
        SET @tod = CAST(@t AS time(0)); SET @d = CAST(@t AS date); SET @shift = NULL;
        SELECT TOP 1 @shift = Name,
               @sdate = CASE WHEN StartT > EndT AND @tod < EndT THEN DATEADD(DAY, -1, @d) ELSE @d END
        FROM @Shifts
        WHERE (StartT < EndT AND @tod >= StartT AND @tod < EndT) OR (StartT > EndT AND (@tod >= StartT OR @tod < EndT));

        SELECT @bound = MIN(b) FROM (
            SELECT DATEADD(SECOND, DATEDIFF(SECOND, CAST('00:00' AS time(0)), StartT), DATEADD(DAY, k, CAST(@d AS datetime))) AS b
            FROM @Shifts CROSS JOIN (VALUES (0), (1)) AS v(k)) AS x
        WHERE b > @t;

        SET @end = CASE WHEN @bound IS NOT NULL AND @bound < @segEnd THEN @bound ELSE @segEnd END;

        -- A time no shift covers is not recorded, as in the HMI.
        IF @cat <> 'GAP' AND @shift IS NOT NULL
        BEGIN
            INSERT dbo.HealthData ([Date], Shift, HealthCategory, StartTime, EndTime, TotalTime)
            VALUES (@sdate, @shift, @cat, @t, @end, DATEDIFF(SECOND, @t, @end));
            SET @segments = @segments + 1;

            IF @cat = 'RunTime'
            BEGIN
                SET @u = DATEADD(SECOND, 3 + CAST(RAND() * 10 AS int), @t);
                WHILE @u < @end
                BEGIN
                    SET @ct = ROUND(9 + RAND() * 3, 2);
                    SET @pass = CASE WHEN RAND() < 0.965 THEN 1 ELSE 0 END;
                    SET @iter = CASE WHEN RAND() < 0.02 THEN 2 ELSE 1 END;
                    SET @toss = CASE WHEN @pass = 0 AND RAND() < 0.4 THEN 1 ELSE 0 END;

                    INSERT dbo.ProductionData ([Date], Shift, ProductionTime, ProductCode, ProductionStatus, CycleTime, Iteration, Tossing)
                    VALUES (@sdate, @shift, @u,
                            'DMY' + CONVERT(varchar(8), @u, 112) + REPLACE(CONVERT(varchar(8), @u, 108), ':', ''),
                            @pass, @ct, @iter, @toss);

                    SET @units = @units + 1;
                    SET @u = DATEADD(MILLISECOND, CAST(@ct * 1000 AS int) + CAST(RAND() * 2000 AS int), @u);
                END;
            END;
        END;

        SET @t = @end;
    END;
END;

CREATE TABLE dbo.DummyDataSeed (
    SeededAt    datetime NOT NULL,
    FromTime    datetime NOT NULL,
    AlarmIdFrom int NULL,
    AlarmIdTo   int NULL);

INSERT dbo.DummyDataSeed (SeededAt, FromTime, AlarmIdFrom, AlarmIdTo) VALUES (@Now, @From, @firstAlarm, @lastAlarm);

COMMIT TRANSACTION;

PRINT 'Dummy data from ' + CONVERT(varchar(19), @From, 120) + ' to ' + CONVERT(varchar(19), @Now, 120) + ': '
    + CAST(@segments AS varchar(10)) + ' health rows, '
    + CAST(ISNULL(@lastAlarm - @firstAlarm + 1, 0) AS varchar(10)) + ' alarms, '
    + CAST(@units AS varchar(10)) + ' units.';
GO
