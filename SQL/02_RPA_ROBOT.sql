/* =====================================================================================
   RPA · Registro de robots (SQL Server INTERFAZ)
   Cada robot (Rpa.AW, Rpa.Sri…) puede estar en un servidor distinto, según dónde esté su aplicación.
   Aquí deja su latido: en qué máquina corre, con qué usuario de Windows, qué flujos sabe hacer y si está vivo.
   Lo leen el adaptador MCP de B1AgentTeam (rpa_equipos: a qué máquina va cada flujo) e Infraestructura
   (avisar si un robot está caído). Generaliza RPA_AW_ROBOT_EQUIPO de RPA_AW.

   Objetos:
     RPA_ROBOT
     RPA_ROBOT_LATIDO      robot → alta o actualización de su fila
     RPA_ROBOT_CONSULTAR   adaptador → robots con sus flujos y segundos sin latido

   Ejecutar en: INTERFAZ. Idempotente. Reversión: 99_ROLLBACK_02_RPA_ROBOT.sql
   Compatible con SQL Server < 2017.
   ===================================================================================== */
USE [INTERFAZ];
GO

IF OBJECT_ID('dbo.RPA_ROBOT', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RPA_ROBOT (
        MAQUINA         NVARCHAR(50)   NOT NULL,
        ROBOT           VARCHAR(40)    NOT NULL,              -- Rpa.AW, Rpa.Sri…
        USUARIO_WIN     NVARCHAR(100)  NOT NULL,              -- usuario de Windows con la sesión del robot
        FLUJOS          VARCHAR(400)   NOT NULL,              -- flujos que sabe hacer, separados por coma
        VERSION         NVARCHAR(40)   NULL,
        ESTADO          VARCHAR(12)    NOT NULL,              -- ESPERANDO / TRABAJANDO / DETENIDO
        DETALLE         NVARCHAR(400)  NULL,
        ULTIMO_LATIDO   DATETIME       NOT NULL,
        FECHA_REGISTRO  DATETIME       NOT NULL CONSTRAINT DF_RPA_ROBOT_FREG DEFAULT (GETDATE()),
        CONSTRAINT PK_RPA_ROBOT PRIMARY KEY (MAQUINA, ROBOT),
        CONSTRAINT CK_RPA_ROBOT_ESTADO CHECK (ESTADO IN ('ESPERANDO','TRABAJANDO','DETENIDO'))
    );
END
GO

IF OBJECT_ID('dbo.RPA_ROBOT_LATIDO', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_ROBOT_LATIDO;
GO
CREATE PROCEDURE dbo.RPA_ROBOT_LATIDO
    @MAQUINA     NVARCHAR(50),
    @ROBOT       VARCHAR(40),
    @USUARIO_WIN NVARCHAR(100),
    @FLUJOS      VARCHAR(400),
    @VERSION     NVARCHAR(40),
    @ESTADO      VARCHAR(12),
    @DETALLE     NVARCHAR(400) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RPA_ROBOT
       SET USUARIO_WIN = @USUARIO_WIN, FLUJOS = @FLUJOS, VERSION = @VERSION, ESTADO = @ESTADO,
           DETALLE = @DETALLE, ULTIMO_LATIDO = GETDATE()
     WHERE MAQUINA = @MAQUINA AND ROBOT = @ROBOT;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.RPA_ROBOT (MAQUINA, ROBOT, USUARIO_WIN, FLUJOS, VERSION, ESTADO, DETALLE, ULTIMO_LATIDO)
        VALUES (@MAQUINA, @ROBOT, @USUARIO_WIN, @FLUJOS, @VERSION, @ESTADO, @DETALLE, GETDATE());
END
GO

IF OBJECT_ID('dbo.RPA_ROBOT_CONSULTAR', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_ROBOT_CONSULTAR;
GO
CREATE PROCEDURE dbo.RPA_ROBOT_CONSULTAR
    @FLUJO VARCHAR(60) = NULL      -- NULL = todos los robots
AS
BEGIN
    SET NOCOUNT ON;
    SELECT MAQUINA, ROBOT, USUARIO_WIN, FLUJOS, VERSION, ESTADO, DETALLE, ULTIMO_LATIDO,
           DATEDIFF(SECOND, ULTIMO_LATIDO, GETDATE()) AS SEG_SIN_LATIDO,
           -- Pendientes de los flujos de este robot, para ver si se está quedando atrás.
           (SELECT COUNT(*) FROM dbo.RPA_TAREA AS T
             WHERE T.ESTADO = 'PEN' AND ',' + R.FLUJOS + ',' LIKE '%,' + T.FLUJO + ',%') AS PENDIENTES
    FROM dbo.RPA_ROBOT AS R
    WHERE @FLUJO IS NULL OR ',' + R.FLUJOS + ',' LIKE '%,' + @FLUJO + ',%'
    ORDER BY ROBOT, MAQUINA;
END
GO
