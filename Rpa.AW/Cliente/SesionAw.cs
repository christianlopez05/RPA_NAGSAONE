// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using Rpa.AW.Config;

namespace Rpa.AW.Cliente;

// Mantiene viva la conexión con el cliente A+W: se engancha si ya está abierto, lo abre si no,
// inicia sesión y se reconecta si A+W se cerró entre un movimiento y otro.
//
// Login de A+W ("Login de database", Gupta): expone sus campos a UI Automation con AutomationId estables
// (Base de datos 4098, Usuario 4100, Contraseña 4102, Servidor 4110, Ok 24578; ver Selectores Login.*).
// Al arrancar, A+W intenta conectarse solo con un usuario por defecto y puede mostrar "Error [4669]":
// ese aviso se acepta antes de llenar el login.
public sealed class SesionAw : IDisposable
{
    private const string TituloLogin = "Login de database";
    // Avisos de inicialización que NO son un rechazo del login (carpetas UNC de A+W que no se alcanzan):
    // pueden salir mientras A+W valida el login; se aceptan y se sigue.
    private static readonly string[] AvisosInofensivos = { "[5514]", "[4458]" };
    private readonly AwOptions _opt;
    private readonly ILogger<SesionAw> _log;
    private Application? _app;
    // Solo se cierra el A+W que abrió el robot; si se enganchó a uno ya abierto (de una persona), no se toca.
    private bool _abiertoPorRobot;

    public UIA3Automation Automation { get; } = new()
    {
        ConnectionTimeout = TimeSpan.FromSeconds(10),
        TransactionTimeout = TimeSpan.FromSeconds(10)
    };

    public SesionAw(AwOptions opt, ILogger<SesionAw> log)
    {
        _opt = opt;
        _log = log;
    }

    public int ProcessId => _app?.ProcessId ?? 0;

    // Devuelve la ventana principal de A+W lista para trabajar.
    // FlaUI lanza excepción al consultar un proceso que ya no existe: se trata como terminado.
    private bool ProcesoTerminado()
    {
        try { return _app == null || _app.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (ArgumentException) { return true; }
    }

    public Window VentanaPrincipal(Localizador loc)
    {
        if (_app == null || ProcesoTerminado())
        {
            _app = Conectar();
            if (_opt.RequiereLogin) IniciarSesion(loc);
        }
        else if (_opt.RequiereLogin && Win32Ventanas.BuscarVentana(_app.ProcessId, TituloLogin) != IntPtr.Zero)
        {
            // A+W sigue abierto pero en la pantalla de login (p. ej. un intento anterior falló a mitad del login).
            _log.LogWarning("A+W está en la pantalla de login: se inicia sesión de nuevo.");
            IniciarSesion(loc);
        }

        // Tras el login A+W muestra "Mensaje: Creando conexiones con base de datos…" mientras inicializa;
        // se espera la ventana principal real (título con TituloVentanaContiene, p. ej. "A+W Business").
        var ventana = EsperarVentanaPrincipal(TimeSpan.FromSeconds(_opt.SegundosEsperaControl * 2));
        ventana.SetForeground();
        return ventana;
    }

    // Espera la ventana principal aceptando los avisos que A+W muestra al inicializar (p. ej. "Error [4458]
    // No se ha podido encontrar el directorio \\SER-AWDATA\Trans\SN\…"): se registran como advertencia y se sigue.
    private Window EsperarVentanaPrincipal(TimeSpan max)
    {
        // Todo por Win32 hasta tener la ventana principal: con un mensaje abierto A+W no responde a UIA.
        var limite = DateTime.Now + max;
        var registrados = new HashSet<string>();   // cada aviso se registra una vez aunque A+W lo repita
        while (DateTime.Now < limite)
        {
            foreach (var aviso in Win32Ventanas.AceptarMensajes(_app!.ProcessId))
            {
                if (!aviso.Cerrado)
                    _log.LogWarning("No se pudo cerrar un aviso de A+W (se reintenta): {Aviso}", aviso.Texto);
                else if (registrados.Add(aviso.Texto))
                    _log.LogWarning("A+W mostró un aviso al inicializar (Ok y se continúa): {Aviso}", aviso.Texto);
            }

            var hwnd = Win32Ventanas.BuscarVentana(_app.ProcessId, _opt.TituloVentanaContiene);
            if (hwnd != IntPtr.Zero)
            {
                try { return Automation.FromHandle(hwnd).AsWindow(); }
                catch (TimeoutException) { /* aún ocupado: se reintenta */ }
            }
            Thread.Sleep(1000);
        }
        throw new TimeoutException($"A+W no mostró su ventana principal ('{_opt.TituloVentanaContiene}') a tiempo.");
    }

    private Application Conectar()
    {
        var procesos = Process.GetProcessesByName(_opt.NombreProceso);
        Process proceso;
        if (procesos.Length > 0)
        {
            proceso = procesos[0];
            _abiertoPorRobot = false;
            _log.LogInformation("A+W ya estaba abierto (PID {Pid}), enganchando (no se cerrará al terminar).", proceso.Id);
        }
        else
        {
            if (!File.Exists(_opt.RutaEjecutable))
                throw new FileNotFoundException($"No existe el ejecutable de A+W configurado: {_opt.RutaEjecutable}");
            _log.LogInformation("Abriendo A+W: {Ruta}", _opt.RutaEjecutable);
            proceso = Process.Start(new ProcessStartInfo(_opt.RutaEjecutable)
            {
                WorkingDirectory = Path.GetDirectoryName(_opt.RutaEjecutable)!,
                UseShellExecute = true
            })!;
            _abiertoPorRobot = true;
        }

        // No se consulta UIA hasta que A+W responda (mientras carga, UIA da timeout).
        if (!Win32Ventanas.EsperarQueResponda(proceso, TimeSpan.FromSeconds(_opt.SegundosEsperaControl),
                m => _log.LogInformation("  {Msg}", m)))
            throw new TimeoutException($"A+W no quedó respondiendo en {_opt.SegundosEsperaControl} s.");

        return Application.Attach(proceso);
    }

    private void IniciarSesion(Localizador loc)
    {
        // A+W ya abierto (p. ej. de una ejecución --una-vez anterior) con su ventana principal y sin login a la
        // vista: la sesión ya está iniciada. Se mira 3 s en vez de esperar el login SegundosEsperaControl (60 s).
        if (!_abiertoPorRobot && SesionYaIniciada(TimeSpan.FromSeconds(3)))
        {
            _log.LogInformation("A+W ya tenía la sesión iniciada: se omite el login.");
            return;
        }

        // Recién mostrado el login, A+W queda ocupado unos segundos (intenta entrar con el usuario por defecto y
        // luego muestra el Error [4669]): UI Automation responde "Operation timed out". Se reintenta el llenado
        // cerrando antes por Win32 los avisos pendientes; es seguro repetirlo porque cada campo se vacía al escribir.
        // Solo cubre hasta ANTES de presionar Ok.
        const int intentosLogin = 4;
        for (int intento = 1; ; intento++)
        {
            try
            {
                if (!LlenarLogin(loc))
                {
                    _log.LogInformation("No apareció la pantalla de login (sesión ya iniciada).");
                    return;
                }
                break;
            }
            catch (TimeoutException) when (intento < intentosLogin)
            {
                foreach (var m in Win32Ventanas.AceptarMensajes(_app!.ProcessId))
                    _log.LogInformation("Aviso de A+W durante el login (Ok y se continúa): {Aviso}", m.Texto);
                _log.LogWarning("A+W no respondió al llenar el login (intento {N} de {Max}); se reintenta.", intento, intentosLogin);
                Thread.Sleep(3000);
            }
        }

        // Resultado. ÉXITO solo si el login desaparece y sigue sin aparecer 3 revisiones seguidas, con otra
        // ventana de A+W abierta (mientras valida, A+W oculta el login un instante y luego lo vuelve a mostrar
        // si falló). FALLO si sale un aviso, o si el login vuelve con la contraseña vacía.
        // Todo por Win32: mientras A+W valida el login y carga sus módulos, UI Automation da timeout.
        var pid = _app!.ProcessId;
        var limite = DateTime.Now.AddSeconds(_opt.SegundosEsperaControl * 2);
        int ausente = 0;
        bool estuvoOculto = false;
        while (DateTime.Now < limite)
        {
            Thread.Sleep(1000);
            if (ProcesoTerminado()) throw new InvalidOperationException("A+W se cerró durante el inicio de sesión.");

            foreach (var m in Win32Ventanas.AceptarMensajes(pid))
            {
                if (AvisosInofensivos.Any(c => m.Texto.Contains(c)))
                {
                    _log.LogWarning("A+W mostró un aviso al inicializar (Ok y se continúa): {Aviso}", m.Texto);
                    ausente = 0;
                    continue;
                }
                throw new InvalidOperationException($"A+W rechazó el inicio de sesión: {m.Texto}");
            }

            var sigue = Win32Ventanas.BuscarVentana(pid, TituloLogin) != IntPtr.Zero;
            if (!sigue)
            {
                estuvoOculto = true;
                if (++ausente >= 3 && Win32Ventanas.VentanasVisibles(pid).Count > 0)
                {
                    _log.LogInformation("Sesión iniciada en A+W.");
                    return;
                }
                continue;
            }
            ausente = 0;
            if (estuvoOculto)
                throw new InvalidOperationException(
                    "A+W volvió a mostrar el login tras presionar Ok: rechazó los datos (revise base de datos, usuario y contraseña).");
        }
        throw new TimeoutException("A+W no terminó el inicio de sesión a tiempo (la pantalla de login sigue abierta).");
    }

    // Llena el login (servidor, base, usuario, clave) y presiona Ok. false si no hay pantalla de login.
    // Lanza TimeoutException si A+W no responde a UIA.
    private bool LlenarLogin(Localizador loc)
    {
        // Avisos ya abiertos (p. ej. Error [4669]) se cierran por Win32 antes de tocar UIA: con un mensaje abierto
        // A+W no responde a UI Automation.
        foreach (var m in Win32Ventanas.AceptarMensajes(_app!.ProcessId))
            _log.LogInformation("Aviso inicial de A+W aceptado: {Aviso}", m.Texto);

        var login = EsperarVentana(TituloLogin, TimeSpan.FromSeconds(_opt.SegundosEsperaControl));
        if (login == null) return false;

        // Aviso del intento automático de A+W con el usuario por defecto (Error [4669]).
        var aviso = AceptarAviso(login);
        if (aviso != null) _log.LogInformation("Aviso inicial de A+W aceptado: {Aviso}", aviso);

        if (!string.IsNullOrWhiteSpace(_opt.Servidor))
            EscribirTexto(loc.Buscar(login, "Login.Servidor"), _opt.Servidor);
        if (!string.IsNullOrWhiteSpace(_opt.BaseDatos))
            EscribirTexto(loc.Buscar(login, "Login.BaseDatos"), _opt.BaseDatos);
        EscribirTexto(loc.Buscar(login, "Login.Usuario"), _opt.Usuario);
        EscribirTexto(loc.Buscar(login, "Login.Clave"), _opt.LeerClave(), ocultar: true);

        _log.LogInformation("Login: servidor {Srv} · base {Bd} · usuario {Usuario}",
            string.IsNullOrWhiteSpace(_opt.Servidor) ? "(el configurado en A+W)" : _opt.Servidor, _opt.BaseDatos, _opt.Usuario);
        PresionarCuandoHabilitado(loc.Buscar(login, "Login.Aceptar"), TimeSpan.FromSeconds(10));
        return true;
    }

    // true si durante todo el lapso la ventana principal está visible y el login no (solo Win32: sin UIA).
    private bool SesionYaIniciada(TimeSpan lapso)
    {
        var pid = _app!.ProcessId;
        var limite = DateTime.Now + lapso;
        while (true)
        {
            if (Win32Ventanas.BuscarVentana(pid, TituloLogin) != IntPtr.Zero) return false;
            if (Win32Ventanas.BuscarVentana(pid, _opt.TituloVentanaContiene) == IntPtr.Zero) return false;
            if (DateTime.Now >= limite) return true;
            Thread.Sleep(500);
        }
    }

    private Window? EsperarVentana(string titulo, TimeSpan max)
    {
        var limite = DateTime.Now + max;
        while (DateTime.Now < limite)
        {
            try
            {
                var w = BuscarVentana(titulo);
                if (w != null) return w;
            }
            catch (TimeoutException) { /* A+W ocupado: se reintenta */ }
            Thread.Sleep(1000);
        }
        return null;
    }

    private Window? BuscarVentana(string titulo) =>
        _app!.GetAllTopLevelWindows(Automation).FirstOrDefault(w => (w.Title ?? "").Contains(titulo, StringComparison.OrdinalIgnoreCase));

    // Si hay un cuadro de mensaje de Windows (#32770) dentro de la ventana, devuelve su texto y presiona su botón.
    private string? AceptarAviso(Window ventana)
    {
        var cf = Automation.ConditionFactory;
        var dlg = ventana.FindFirstChild(cf.ByClassName("#32770"))
                  ?? _app!.GetAllTopLevelWindows(Automation).FirstOrDefault(w => w.ClassName == "#32770");
        if (dlg == null) return null;
        var texto = string.Join(" ", dlg.FindAllDescendants(cf.ByControlType(ControlType.Text))
            .Select(t => t.Name).Where(t => !string.IsNullOrWhiteSpace(t)));
        var titulo = dlg.Name;
        (dlg.FindFirstDescendant(cf.ByAutomationId("1")) ?? dlg.FindFirstDescendant(cf.ByControlType(ControlType.Button)))
            ?.AsButton().Invoke();
        Thread.Sleep(500);
        return $"{titulo}: {texto}".Trim();
    }

    // Se escribe con TECLADO real (no asignando el valor): Gupta solo registra el cambio —y habilita
    // botones como Ok— cuando recibe las teclas. Tab al final dispara la validación del campo.
    // Antes de teclear: la ventana del campo al frente (las teclas van a la ventana activa) y el campo
    // vaciado por UIA (Ctrl+A no selecciona en los combos de Gupta y el texto se acumulaba).
    private void EscribirTexto(AutomationElement campo, string valor, bool ocultar = false)
    {
        try { (campo.Parent?.Parent?.AsWindow() ?? campo.Parent?.AsWindow())?.SetForeground(); } catch { }
        Win32Ventanas.TraerAlFrente(_app!.MainWindowHandle);
        try { campo.AsTextBox().Text = string.Empty; } catch { /* algunos campos no admiten asignación */ }
        campo.Focus();
        Thread.Sleep(200);
        Win32Ventanas.DesactivarBloqMayus();
        Keyboard.Type(valor);
        Keyboard.Type(VirtualKeyShort.TAB);
        Thread.Sleep(300);
        if (ocultar) return;
        var actual = campo.AsTextBox().Text?.Trim() ?? string.Empty;
        // Comparación exacta (con mayúsculas): así se detecta si algo invirtió el texto al teclear.
        if (!string.Equals(actual, valor.Trim(), StringComparison.Ordinal))
            _log.LogWarning("El campo quedó con '{Actual}' en vez de '{Valor}'.", actual, valor);
    }

    // Gupta habilita Ok cuando los datos del login están completos: se espera antes de presionarlo.
    private static void PresionarCuandoHabilitado(AutomationElement boton, TimeSpan max)
    {
        var limite = DateTime.Now + max;
        while (!boton.IsEnabled && DateTime.Now < limite) Thread.Sleep(300);
        if (!boton.IsEnabled)
            throw new InvalidOperationException(
                "El botón Ok del login de A+W sigue deshabilitado: A+W no aceptó los datos escritos (revise servidor, base de datos y usuario).");
        boton.AsButton().Invoke();
    }

    // Cierra el A+W abierto por el robot. Si pide confirmación se acepta; si no cierra, se fuerza.
    public void Cerrar()
    {
        if (_app == null || ProcesoTerminado()) return;
        if (!_abiertoPorRobot)
        {
            _log.LogInformation("A+W no lo abrió el robot: se deja abierto.");
            return;
        }
        _log.LogInformation("Cerrando A+W…");
        try
        {
            var p = Process.GetProcessById(_app.ProcessId);
            p.CloseMainWindow();
            var limite = DateTime.Now.AddSeconds(20);
            while (DateTime.Now < limite && !p.HasExited)
            {
                try
                {
                    var conf = _app.GetAllTopLevelWindows(Automation).FirstOrDefault(w => w.ClassName == "#32770");
                    var si = conf?.FindFirstDescendant(Automation.ConditionFactory.ByAutomationId("6"))   // IDYES
                             ?? conf?.FindFirstDescendant(Automation.ConditionFactory.ByAutomationId("1")); // IDOK
                    si?.AsButton().Invoke();
                }
                catch { }
                Thread.Sleep(1000);
            }
            if (!p.HasExited)
            {
                _log.LogWarning("A+W no cerró en 20 s; se fuerza el cierre (PID {Pid}).", p.Id);
                p.Kill();
            }
        }
        catch { /* ya cerrado */ }
    }

    public void Dispose()
    {
        Automation.Dispose();
        _app?.Dispose();
    }
}
