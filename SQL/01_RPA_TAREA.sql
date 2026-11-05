/* =====================================================================================
   RPA · Cola común de tareas (SQL Server INTERFAZ)
   Directriz de B1AgentTeam (docs/rpa-robots.md): un robot no tiene trabajo propio; ejecuta tareas
   de una cola y cada tarea tiene dueño (el encargo de un agente con la propuesta que una persona
   confirmó, o una persona que confirmó en una pantalla). Sirve para todos los flujos nuevos; los
   retales siguen en RPA_AW_RETAL_COLA hasta que se pruebe su migración.

   ESTADO:  PEN = pendiente      PRO = en proceso (tomada por un robot)
            OK  = escrita y verificada en el sistema
            ERR = dato inválido, error definitivo o agotó los reintentos antes de guardar
            REV = pudo haber guardado y no lo confirmó, o fue un ensayo: revisar antes de reprocesar

   Objetos:
     RPA_TAREA, RPA_TAREA_LOG
     RPA_TAREA_ENCOLAR      adaptador o pantalla → inserta (idempotente por OPERACION_ID)
     RPA_TAREA_TOMAR        robot → toma la siguiente PEN de los flujos que sabe hacer (PEN → PRO)
     RPA_TAREA_MARCAR       robot → cierra la tarea (OK / ERR / REV / PEN para reintento)
     RPA_TAREA_HUERFANAS    robot (al arrancar) → PRO colgadas de esta máquina → REV
     RPA_TAREA_LOG_W        robot → evento a la bitácora
     RPA_TAREA_REPROCESAR   persona → ERR/REV → PEN (queda quién lo pidió)
     RPA_TAREA_ESTADO       adaptador → estado de una tarea por OPERACION_ID

   Ejecutar en: INTERFAZ. Idempotente. Reversión: 99_ROLLBACK_01_RPA_TAREA.sql
   Compatible con SQL Server < 2017 (sin CREATE OR ALTER / STRING_SPLIT / STRING_AGG / TRIM).
   ===================================================================================== */
USE [INTERFAZ];
GO

IF OBJECT_ID('dbo.RPA_TAREA', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RPA_TAREA (
        ID               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RPA_TAREA PRIMARY KEY,
        FLUJO            VARCHAR(60)      NOT NULL,             -- herramienta del catálogo RpaFlows, p. ej. rpa_aw_pedido_edi
        VERSION_FLUJO    INT              NOT NULL,
        OPERACION_ID     UNIQUEIDENTIFIER NOT NULL,             -- id de la propuesta (agente) o de la pantalla: no se duplica
        ORIGEN           VARCHAR(10)      NOT NULL,             -- AGENTE / PANTALLA
        ENCARGO_ID       UNIQUEIDENTIFIER NULL,                 -- encargo de B1AgentTeam, si viene de un agente
        AGENTE_ID        VARCHAR(60)      NULL,
        PERSONA          NVARCHAR(100)    NOT NULL,             -- quién confirmó
        EMPRESA          NVARCHAR(40)     NOT NULL,             -- empresa del grupo sobre la que actúa (VETRIKO…)
        DATOS            NVARCHAR(MAX)    NOT NULL,             -- JSON de entrada, ya validado; el robot no recalcula
        ESTADO           VARCHAR(3)       NOT NULL CONSTRAINT DF_RPA_TAREA_EST DEFAULT ('PEN'),
        INTENTOS         INT              NOT NULL CONSTRAINT DF_RPA_TAREA_INT DEFAULT (0),
        RESULTADO        NVARCHAR(MAX)    NULL,                 -- JSON de salida (p. ej. el número de pedido)
        MENSAJE          NVARCHAR(1000)   NULL,                 -- el error tal cual
        PASO_FALLIDO     NVARCHAR(60)     NULL,
        CAPTURA          NVARCHAR(400)    NULL,                 -- ruta en una carpeta compartida
        MAQUINA          NVARCHAR(50)     NULL,
        FECHA_REGISTRO   DATETIME         NOT NULL CONSTRAINT DF_RPA_TAREA_FREG DEFAULT (GETDATE()),
        FECHA_INICIO     DATETIME         NULL,
        FECHA_FIN        DATETIME         NULL,
        CONSTRAINT CK_RPA_TAREA_ESTADO CHECK (ESTADO IN ('PEN','PRO','OK','ERR','REV')),
        CONSTRAINT CK_RPA_TAREA_ORIGEN CHECK (ORIGEN IN ('AGENTE','PANTALLA')),
        CONSTRAINT CK_RPA_TAREA_AGENTE CHECK (ORIGEN <> 'AGENTE' OR (ENCARGO_ID IS NOT NULL AND AGENTE_ID IS NOT NULL)),
        CONSTRAINT CK_RPA_TAREA_PERSONA CHECK (LEN(PERSONA) > 0)
    );

    CREATE UNIQUE INDEX UX_RPA_TAREA_OPERACION ON dbo.RPA_TAREA (OPERACION_ID);
    CREATE INDEX IX_RPA_TAREA_ESTADO ON dbo.RPA_TAREA (ESTADO, FLUJO, ID);
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_LOG', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RPA_TAREA_LOG (
        ID        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RPA_TAREA_LOG PRIMARY KEY,
        ID_TAREA  INT            NULL,                 -- NULL = evento general del robot
        FECHA     DATETIME       NOT NULL CONSTRAINT DF_RPA_TAREA_LOG_F DEFAULT (GETDATE()),
        NIVEL     VARCHAR(5)     NOT NULL,             -- INFO / WARN / ERROR
        MENSAJE   NVARCHAR(2000) NOT NULL,
        MAQUINA   NVARCHAR(50)   NULL
    );
    CREATE INDEX IX_RPA_TAREA_LOG_TAREA ON dbo.RPA_TAREA_LOG (ID_TAREA, ID);
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_ENCOLAR', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_ENCOLAR;
GO
CREATE PROCEDURE dbo.RPA_TAREA_ENCOLAR
    @FLUJO         VARCHAR(60),
    @VERSION_FLUJO INT,
    @OPERACION_ID  UNIQUEIDENTIFIER,
    @ORIGEN        VARCHAR(10),
    @ENCARGO_ID    UNIQUEIDENTIFIER = NULL,
    @AGENTE_ID     VARCHAR(60) = NULL,
    @PERSONA       NVARCHAR(100),
    @EMPRESA       NVARCHAR(40),
    @DATOS         NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    -- La misma operación encolada dos veces (doble clic, reintento del adaptador) devuelve la tarea que ya existe.
    IF NOT EXISTS (SELECT 1 FROM dbo.RPA_TAREA WHERE OPERACION_ID = @OPERACION_ID)
    BEGIN
        BEGIN TRY
            INSERT INTO dbo.RPA_TAREA (FLUJO, VERSION_FLUJO, OPERACION_ID, ORIGEN, ENCARGO_ID, AGENTE_ID, PERSONA, EMPRESA, DATOS)
            VALUES (@FLUJO, @VERSION_FLUJO, @OPERACION_ID, @ORIGEN, @ENCARGO_ID, @AGENTE_ID, @PERSONA, @EMPRESA, @DATOS);

            INSERT INTO dbo.RPA_TAREA_LOG (ID_TAREA, NIVEL, MENSAJE, MAQUINA)
            VALUES (SCOPE_IDENTITY(), 'INFO', N'Encolada por ' + @PERSONA + N' (' + @ORIGEN + N')', NULL);

            SELECT ID, ESTADO, CAST(1 AS BIT) AS NUEVA FROM dbo.RPA_TAREA WHERE OPERACION_ID = @OPERACION_ID;
            RETURN;
        END TRY
        BEGIN CATCH
            -- Carrera con otra llamada de la misma operación: el índice único la frenó; se devuelve la existente.
            IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
        END CATCH
    END
    SELECT ID, ESTADO, CAST(0 AS BIT) AS NUEVA FROM dbo.RPA_TAREA WHERE OPERACION_ID = @OPERACION_ID;
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_TOMAR', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_TOMAR;
GO
CREATE PROCEDURE dbo.RPA_TAREA_TOMAR
    @MAQUINA NVARCHAR(50),
    @FLUJOS  VARCHAR(400)          -- flujos que este robot sabe hacer, separados por coma
AS
BEGIN
    SET NOCOUNT ON;
    -- UPDLOCK + READPAST: si hubiera más de un robot, cada uno toma una tarea distinta sin bloquearse.
    ;WITH SIGUIENTE AS (
        SELECT TOP (1) *
        FROM dbo.RPA_TAREA WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE ESTADO = 'PEN'
          AND ',' + REPLACE(@FLUJOS, ' ', '') + ',' LIKE '%,' + FLUJO + ',%'
        ORDER BY ID
    )
    UPDATE SIGUIENTE
       SET ESTADO = 'PRO', MAQUINA = @MAQUINA, FECHA_INICIO = GETDATE(), FECHA_FIN = NULL
    OUTPUT inserted.ID, inserted.FLUJO, inserted.VERSION_FLUJO, inserted.OPERACION_ID, inserted.ORIGEN,
           inserted.PERSONA, inserted.EMPRESA, inserted.DATOS, inserted.INTENTOS;
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_MARCAR', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_MARCAR;
GO
CREATE PROCEDURE dbo.RPA_TAREA_MARCAR
    @ID           INT,
    @ESTADO       VARCHAR(3),          -- OK / ERR / REV / PEN (PEN = vuelve a la cola para reintento)
    @MENSAJE      NVARCHAR(1000),
    @PASO_FALLIDO NVARCHAR(60) = NULL,
    @RESULTADO    NVARCHAR(MAX) = NULL,
    @CAPTURA      NVARCHAR(400) = NULL,
    @MAQUINA      NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RPA_TAREA
       SET ESTADO       = @ESTADO,
           INTENTOS     = INTENTOS + 1,
           MENSAJE      = @MENSAJE,
           PASO_FALLIDO = CASE WHEN @ESTADO = 'OK' THEN NULL ELSE @PASO_FALLIDO END,
           RESULTADO    = ISNULL(@RESULTADO, RESULTADO),
           CAPTURA      = ISNULL(@CAPTURA, CAPTURA),
           FECHA_FIN    = GETDATE()
     WHERE ID = @ID AND ESTADO = 'PRO' AND MAQUINA = @MAQUINA;

    INSERT INTO dbo.RPA_TAREA_LOG (ID_TAREA, NIVEL, MENSAJE, MAQUINA)
    VALUES (@ID, CASE @ESTADO WHEN 'OK' THEN 'INFO' WHEN 'PEN' THEN 'WARN' ELSE 'ERROR' END,
            @ESTADO + ISNULL(N' [' + @PASO_FALLIDO + N']', N'') + N': ' + ISNULL(@MENSAJE, N''), @MAQUINA);
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_HUERFANAS', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_HUERFANAS;
GO
CREATE PROCEDURE dbo.RPA_TAREA_HUERFANAS
    @MAQUINA NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    -- Si el robot se cayó a mitad de una tarea, no se sabe si alcanzó a escribir: no se reintenta a ciegas.
    UPDATE dbo.RPA_TAREA
       SET ESTADO = 'REV', FECHA_FIN = GETDATE(), PASO_FALLIDO = N'robot detenido',
           MENSAJE = N'El robot se detuvo mientras procesaba esta tarea. Verifique en el sistema si quedó registrada.'
     WHERE ESTADO = 'PRO' AND MAQUINA = @MAQUINA;

    SELECT @@ROWCOUNT AS [Value];
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_LOG_W', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_LOG_W;
GO
CREATE PROCEDURE dbo.RPA_TAREA_LOG_W
    @ID_TAREA INT = NULL,
    @NIVEL    VARCHAR(5),
    @MENSAJE  NVARCHAR(2000),
    @MAQUINA  NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.RPA_TAREA_LOG (ID_TAREA, NIVEL, MENSAJE, MAQUINA) VALUES (@ID_TAREA, @NIVEL, @MENSAJE, @MAQUINA);
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_REPROCESAR', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_REPROCESAR;
GO
CREATE PROCEDURE dbo.RPA_TAREA_REPROCESAR
    @ID      INT,
    @USUARIO NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    -- Reprocesar es decisión de una persona: con REV, después de revisar en el sistema que no quedó escrita.
    UPDATE dbo.RPA_TAREA
       SET ESTADO = 'PEN', INTENTOS = 0, MAQUINA = NULL, FECHA_INICIO = NULL, FECHA_FIN = NULL
     WHERE ID = @ID AND ESTADO IN ('ERR','REV');

    IF @@ROWCOUNT = 0
    BEGIN
        SELECT 0 AS [Value];   -- no existe o no está en ERR/REV
        RETURN;
    END

    INSERT INTO dbo.RPA_TAREA_LOG (ID_TAREA, NIVEL, MENSAJE, MAQUINA)
    VALUES (@ID, 'INFO', N'Reprocesada por ' + @USUARIO, NULL);
    SELECT 1 AS [Value];
END
GO

IF OBJECT_ID('dbo.RPA_TAREA_ESTADO', 'P') IS NOT NULL DROP PROCEDURE dbo.RPA_TAREA_ESTADO;
GO
CREATE PROCEDURE dbo.RPA_TAREA_ESTADO
    @OPERACION_ID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ID, FLUJO, VERSION_FLUJO, ESTADO, INTENTOS, RESULTADO, MENSAJE, PASO_FALLIDO, CAPTURA, MAQUINA,
           FECHA_REGISTRO, FECHA_INICIO, FECHA_FIN
    FROM dbo.RPA_TAREA WHERE OPERACION_ID = @OPERACION_ID;

    SELECT L.FECHA, L.NIVEL, L.MENSAJE, L.MAQUINA
    FROM dbo.RPA_TAREA_LOG AS L
    JOIN dbo.RPA_TAREA AS T ON T.ID = L.ID_TAREA
    WHERE T.OPERACION_ID = @OPERACION_ID
    ORDER BY L.ID;
END
GO
