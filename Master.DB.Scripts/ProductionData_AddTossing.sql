-- Adds [Tossing] to an existing [ProductionData] table (new databases get it from
-- ShiftOEEStatus.sql). 1 = the unit was tossed by the pre-process CCD check.
-- Existing rows and writers that do not know the column get 0. Safe to run twice.
USE [TEAL_Master]
GO

IF COL_LENGTH('dbo.ProductionData', 'Tossing') IS NULL
BEGIN
	ALTER TABLE [dbo].[ProductionData] ADD [Tossing] [bit] NOT NULL DEFAULT ((0))
END
GO
