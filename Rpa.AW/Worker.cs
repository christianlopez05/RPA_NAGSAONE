using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rpa.AW.Cliente;
using Rpa.AW.Config;
using Rpa.AW.Flujos.PedidoEdi;
using Rpa.AW.Pantallas;
using Rpa.Comun;

namespace Rpa.AW;

// Ciclo del robot de A+W: al arrancar pasa a REV las tareas que quedaron colgadas de esta máquina; luego toma
// tareas de sus flujos, late en RPA_ROBOT y espera cuando no hay nada. Hoy hace un flujo: rpa_aw_pedido_edi.
public class Worker : BackgroundService
{
    private readonly ProcesadorPedidoEdi _edi;
    private readonly IRegistroRobot _registro;
    private readonly Localizador _loc;
    private readonly RobotOptions _opt;
    private readonly ILogger<Worker> _log;
    private readonly IHostApplicationLifetime _vida;

    public Worker(ProcesadorPedidoEdi edi, IRegistroRobot registro, Localizador loc, RobotOptions opt,
                  ILogger<Worker> log, IHostApplicationLifetime vida)
    {
        _edi = edi;
        _registro = registro;
        _loc = loc;
        _opt = opt;
        _log = log;
        _vida = vida;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Rpa.AW iniciado en {Maquina} · ambiente {Amb} · pedidos EDI en {Modo}", _opt.Maquina, _opt.Ambiente,
            _edi.ModoEnsayo ? "MODO ENSAYO (no importa)" : "MODO REAL (importa)");

        var pantalla = RevisionPantalla.Medir();
        var problema = RevisionPantalla.Problema(pantalla, _opt.AnchoMinimoPantalla, _opt.AltoMinimoPantalla);
        if (problema != null)
            _log.LogWarning("PANTALLA MÁS CHICA DE LO RECOMENDADO: {Problema}. Pantalla: {Medida}.", problema, pantalla);

        var pendientes = _loc.PendientesDe(PantallaImportacionEdi.Claves).ToList();
        if (pendientes.Count > 0)
            _log.LogWarning("Selectores EDI sin configurar: {Claves}. Las tareas irán a ERR hasta completarlos (use --menu y --probar-edi).",
                string.Join(", ", pendientes));

        // SQL puede no estar disponible al arrancar (la VM reinició antes que el servidor): se reintenta.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int huerfanas = await _edi.RecuperarHuerfanasAsync();
                if (huerfanas > 0)
                    _log.LogWarning("{N} tarea(s) quedaron en proceso de una ejecución anterior → pasan a revisión (REV).", huerfanas);
                await Latir(EstadosRobot.Esperando, problema);
                _log.LogInformation("Conectado a la cola RPA_TAREA de INTERFAZ. Esperando tareas…");
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError("No se pudo conectar a la cola en INTERFAZ: {Msg}. Reintento en 30 s.", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Latir(EstadosRobot.Trabajando);
                if (!await _edi.ProcesarSiguienteAsync())
                {
                    await Latir(EstadosRobot.Esperando);
                    if (_opt.UnaVez)
                    {
                        _log.LogInformation("Cola vacía: ejecución única terminada.");
                        _vida.StopApplication();
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(_opt.IntervaloColaSegundos), stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Típicamente: SQL caído. Se registra y se espera antes de reintentar para no saturar.
                _log.LogError("Error en el ciclo del robot: {Msg}", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, _opt.IntervaloColaSegundos)), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        await Latir(EstadosRobot.Detenido);
    }

    // Un latido que falla nunca detiene al robot: se avisa y sigue.
    private async Task Latir(string estado, string? detalle = null)
    {
        try { await _registro.LatirAsync(estado, detalle); }
        catch (Exception ex) { _log.LogWarning("No se pudo registrar el latido en RPA_ROBOT: {Msg}", ex.Message); }
    }
}
