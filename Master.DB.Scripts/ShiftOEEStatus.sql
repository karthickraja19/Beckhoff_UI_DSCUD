USE [TEAL_Master]
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[HealthData](
	[Date] [date] NOT NULL,
	[Shift] [varchar](20) NOT NULL,
	[HealthCategory] [varchar](50) NOT NULL,
	[StartTime] [datetime] NOT NULL,
	[EndTime] [datetime] NOT NULL,
	[TotalTime] [float] NOT NULL
) ON [PRIMARY]
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[OEEData](
	[DateTime] [datetime] NOT NULL,
	[Availability] [float] NOT NULL,
	[Performance] [float] NOT NULL,
	[Quality] [float] NOT NULL
) ON [PRIMARY]
GO


SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[ProductionData](
	[Date] [date] NOT NULL,
	[Shift] [varchar](20) NOT NULL,
	[ProductionTime] [datetime] NOT NULL,
	[ProductCode] [varchar](max) NOT NULL,
	[ProductionStatus] [bit] NOT NULL,
	[CycleTime] [float] NULL,
	[Iteration] [int] NOT NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[ProductionData] ADD  DEFAULT ((0)) FOR [ProductionStatus]
GO

ALTER TABLE [dbo].[ProductionData] ADD  DEFAULT ((0)) FOR [Iteration]
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[ProductionDataHourly](
	[Date] [date] NOT NULL,
	[Shift] [varchar](20) NOT NULL,
	[Hour] [int] NOT NULL,
	[ProductCount] [int] NULL
) ON [PRIMARY]
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[ShiftData](
	[Date] [date] NOT NULL,
	[Shift] [varchar](1) NOT NULL,
	[RunTime] [float] NULL,
	[DownTime] [float] NULL,
	[IdleTime] [float] NULL,
	[PlannedDownTime] [float] NULL,
	[BreakTime] [float] NULL,
	[PassCount] [int] NULL,
	[FailCount] [int] NULL,
	[Yield] [float] NULL,
	[OEE] [float] NULL,
	[MTBF] [float] NULL,
	[MTBA] [float] NULL,
	[TossingRate] [float] NULL,
	[RetestRate] [float] NULL,
	[LastUpdated] [datetime] NULL
) ON [PRIMARY]
GO


