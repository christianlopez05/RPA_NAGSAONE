/* =====================================================================================
   Reversión de 02_RPA_ROBOT.sql: borra el registro de robots y sus procedimientos.
   Ejecutar en: INTERFAZ.
   ===================================================================================== */
USE [INTERFAZ];
GO

IF OBJECT_ID('dbo.RPA_ROBOT_CONSULTAR', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_ROBOT_CONSULTAR;
IF OBJECT_ID('dbo.RPA_ROBOT_LATIDO', 'P')    IS NOT NULL DROP PROCEDURE dbo.RPA_ROBOT_LATIDO;
GO

IF OBJECT_ID('dbo.RPA_ROBOT', 'U') IS NOT NULL DROP TABLE dbo.RPA_ROBOT;
GO
