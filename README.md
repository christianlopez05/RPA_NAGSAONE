# RPA_NAGSAONE

Robots RPA de NAGSA ligados a los agentes de B1AgentTeam. Siguen la directriz `docs/rpa-robots.md` de B1AgentTeam: **un robot no tiene trabajo propio; ejecuta tareas de una cola, y cada tarea tiene dueño** (el encargo de un agente con la propuesta que una persona confirmó, o una persona que confirmó en una pantalla).

## Estructura

Un repositorio, un ejecutable por sistema. Cada robot se compila, se instala y se despliega por separado, en la máquina donde está su aplicación.

| Proyecto | Qué es | Dónde corre |
|---|---|---|
| `Rpa.Comun` | El contrato de todos los robots: cola `RPA_TAREA`, estados, política de reintentos, latido en `RPA_ROBOT` | Biblioteca |
| `Rpa.AW.Flujos` | Flujos del robot de A+W sin pantalla: validación, procesamiento, lectura de la base de A+W | Biblioteca, probada con dobles |
| `Rpa.AW` | Robot de A+W: maneja la ventana de A+W Business con FlaUI | Máquina con el cliente de A+W |
| `Rpa.Tests` | Pruebas de `Rpa.Comun` y `Rpa.AW.Flujos` (`dotnet test`) | — |
| `SQL/` | `01` cola de tareas, `02` registro de robots, con su reversión | INTERFAZ |

Un robot nuevo (por ejemplo `Rpa.Sri`, con Playwright para el portal del SRI) es otra carpeta con su ejecutable, que referencia `Rpa.Comun` y toma **solo sus flujos** de la misma cola.

```
Agente de B1AgentTeam (tras el clic) ─┐
Pantalla de WEB_INTERFACES_CORE ──────┴─► INTERFAZ.dbo.RPA_TAREA ─┬─► Rpa.AW  (máquina de A+W)   ─► A+W
                                                                  └─► Rpa.Sri (otra máquina)       ─► portal del SRI
                              cada robot late en INTERFAZ.dbo.RPA_ROBOT: máquina, flujos y estado
```

## Estados de una tarea

| Estado | Significado | Qué hacer |
|---|---|---|
| `PEN` | Pendiente | Nada: el robot la tomará |
| `PRO` | La tomó un robot | Nada |
| `OK` | Escrita **y verificada** en el sistema | — |
| `ERR` | Dato inválido, error definitivo, o falló antes de guardar y agotó los reintentos | Corregir y reprocesar (`RPA_TAREA_REPROCESAR`) |
| `REV` | Pudo haber guardado y no lo confirmó, o fue un ensayo | **Revisar en el sistema** antes de reprocesar |

Si un robot falla **después** de guardar, nunca reintenta solo. Reprocesar es siempre decisión de una persona.

## Reglas para todos los robots

- Usuario propio del robot en Windows y en cada sistema, nunca el de una persona.
- **Sin claves en archivos ni en git.** `appsettings.json` no se versiona; las cadenas de conexión con clave y las claves de los sistemas van en variables de entorno del usuario de Windows del robot.
- Modo ensayo primero; pruebas reales solo en la base de pruebas del sistema.
- Verificar en el sistema después de escribir.
- Nunca SAP Business One: SAP solo se toca por ASISTANT B1.

---

## Rpa.AW · Robot de A+W

Maneja el cliente **A+W Business 6** (Gupta) con FlaUI. La base para abrir A+W, iniciar sesión, recorrer el menú de Gupta y leer sus mensajes (`Cliente/`, `Pantallas/PantallaBase.cs`) está **copiada de `RPA_AW`, de Wilthon Marcillo** (commit `9af446f`), sin cambios de lógica.

Usa el **mismo candado** que `RPA_AW` (`Global\RPA_AW_Robot`): si los dos están en la misma máquina, se turnan en vez de pelearse por la ventana de A+W.

### Flujo `rpa_aw_pedido_edi`: pedidos de vidrio por EDI

Importa en A+W el pedido de vidrio de un lote de PrefSuite: el `.ASC` (interfaz "A+W EDI Import" 02.2.035) que hoy se descarga de la pantalla **AwEdiVidrios** de WEB_INTERFACES_CORE y alguien carga a mano. Lo usará el agente «Pedidos de vidrio A+W» de B1AgentTeam.

1. Toma la siguiente tarea `rpa_aw_pedido_edi` en `PEN`.
2. Revisa que la versión del flujo sea la suya y que el `.ASC` sea el pedido que dice la tarea: registros de 512 caracteres, `FH`/`K1`/`P1`/`#END#`, mismo cliente (K1/4) y mismo lote (K1/7). Si no, `ERR` sin abrir A+W.
3. Si el lote ya tiene pedido en A+W para ese cliente (estado ≥ 401), `ERR`: **no se importa otro**.
4. Escribe el `.ASC` en Latin-1 en `Edi:CarpetaArchivos` y lo importa en la pantalla de A+W.
5. Verifica que apareció **un** pedido nuevo del lote: `OK` con `{"pedido": …}` en `RESULTADO`. Si no aparece, o aparecen dos, `REV`.

### Puesta en marcha (contra demo2026)

1. **SQL:** ejecutar `SQL/01_RPA_TAREA.sql` y `SQL/02_RPA_ROBOT.sql` en INTERFAZ. El usuario SQL del robot necesita `EXECUTE` sobre `RPA_TAREA_*` y `RPA_ROBOT_*`, y lectura de `SYSADM.BW_AUFTR_KOPF` en la base de A+W.
2. **Configuración:** copiar `Rpa.AW/appsettings.ejemplo.json` como `appsettings.json` junto al ejecutable y completar el usuario de A+W del robot. Las claves, como variables de entorno del usuario de Windows del robot (los comandos `setx` están en la plantilla).
3. **Encontrar la pantalla:** `Rpa.AW.exe --menu` guarda las opciones del menú lateral en la carpeta `Exploracion`. Poner la de importar pedidos EDI en `Selectores:Menu.ImportacionEdi`.
4. **Mapear la pantalla:** `Rpa.AW.exe --probar-edi` la abre (sin importar nada) y guarda captura y controles. Completar `Edi.Importar` y, si la pantalla pide la ruta, `Edi.Archivo`. Si A+W lee su propia carpeta de importación, dejar `Edi.Archivo` sin poner y apuntar `Edi:CarpetaArchivos` a esa carpeta. Ajustar `Pantallas/PantallaImportacionEdi.cs` si la pantalla pide otros pasos: **es una primera versión**.
5. **Encolar una prueba:** descargar el `.ASC` de un lote desde AwEdiVidrios y
   `Rpa.AW.exe --encolar-edi C:\ruta\LOTE6539.ASC --lote 6539 --cliente 398`.
   Queda con origen `PANTALLA` y su usuario de Windows como persona.
6. **Ensayo:** `Edi:Habilitado = true` y correr `Rpa.AW.exe --una-vez`. Con `ModoEnsayo = true` abre la importación, deja el archivo listo, toma la captura y deja la tarea en `REV` sin importar.
7. Con el ensayo validado, `ModoEnsayo = false` y probar contra **demo2026**: la tarea debe quedar en `OK` con el número de pedido, y el pedido verse en A+W.

### Comandos

| Comando | Qué hace |
|---|---|
| `Rpa.AW.exe` | Procesa la cola y se queda esperando (lo arranca el Programador de tareas al iniciar sesión el usuario del robot) |
| `--una-vez` | Procesa lo pendiente y termina |
| `--probar-login [--cerrar]` | Abre A+W e inicia sesión, sin SQL |
| `--menu [--cerrar]` | Guarda todas las opciones del menú lateral |
| `--probar-edi [--cerrar]` | Abre la importación EDI y guarda sus controles, sin importar |
| `--encolar-edi <archivo> --lote N [--cliente 398] [--empresa VETRIKO]` | Deja una tarea de prueba en la cola |

La máquina del robot sigue las mismas reglas que en `RPA_AW`: inicio de sesión automático, sin bloqueo de pantalla, resolución fija y no minimizar el Escritorio Remoto.

## Pendiente

- Adaptador MCP en el puente de B1AgentTeam: `rpa_aw_pedido_edi` (encola), `rpa_estado` y `rpa_equipos` (leen `RPA_TAREA` y `RPA_ROBOT`).
- Escucha de órdenes, para que el adaptador despierte al robot en vez de esperar su ciclo.
- Captura del error en una carpeta compartida que pueda leer la plataforma.
- Migrar los retales de `RPA_AW` como flujo `rpa_aw_retal` de `Rpa.AW`, con Wilthon.
