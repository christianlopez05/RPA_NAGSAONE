/* =====================================================================================
   Reversión de 01_RPA_TAREA.sql: borra la cola común de tareas y sus procedimientos.
   CUIDADO: borra también las tareas y su bitácora. Revisar antes que no haya PEN ni PRO.
   Ejecutar en: INTERFAZ.
   ===================================================================================== */
USE [INTERFAZ];
GO

IF OBJECT_ID('dbo.RPA_TAREA_ESTADO', 'P')     IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_ESTADO;
IF OBJECT_ID('dbo.RPA_TAREA_REPROCESAR', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_REPROCESAR;
IF OBJECT_ID('dbo.RPA_TAREA_LOG_W', 'P')      IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_LOG_W;
IF OBJECT_ID('dbo.RPA_TAREA_HUERFANAS', 'P')  IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_HUERFANAS;
IF OBJECT_ID('dbo.RPA_TAREA_MARCAR', 'P')     IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_MARCAR;
IF OBJECT_ID('dbo.RPA_TAREA_TOMAR', 'P')      IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_TOMAR;
IF OBJECT_ID('dbo.RPA_TAREA_ENCOLAR', 'P')    IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_ENCOLAR;
GO

IF OBJECT_ID('dbo.RPA_TAREA_LOG', 'U') IS NOT NULL DROP TABLE dbo.RPA_TAREA_LOG;
IF OBJECT_ID('dbo.RPA_TAREA', 'U')     IS NOT NULL DROP TABLE dbo.RPA_TAREA;
GO
