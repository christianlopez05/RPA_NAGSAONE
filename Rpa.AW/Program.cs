using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rpa.AW;
using Rpa.AW.Cliente;
using Rpa.AW.Config;
using Rpa.AW.Flujos;
using Rpa.AW.Flujos.Datos;
using Rpa.AW.Flujos.PedidoEdi;
using Rpa.AW.Herramientas;
using Rpa.Comun;
using Rpa.Comun.Datos;

// Robot de A+W de RPA_NAGSAONE. Ejecuta las tareas de sus flujos de la cola común RPA_TAREA (docs/rpa-robots.md de B1AgentTeam).
// Es una app de CONSOLA (no Servicio de Windows): los servicios corren sin escritorio y no pueden manejar ventanas.
// En la máquina de A+W se arranca con el Programador de tareas "Al iniciar sesión" del usuario del robot.
Console.OutputEncoding = System.Text.Encoding.UTF8;
var builder = Host.CreateApplicationBuilder(args);
// appsettings.json no lleva secretos: las cadenas de conexión con clave van en variables de entorno del usuario del robot
// (ConnectionStrings__Interfaz, ConnectionStrings__AwSql), que se cargan después y mandan sobre el archivo.
builder.Configuration.SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables();

var cfg = builder.Configuration;
var optRobot = cfg.GetSection("Robot").Get<RobotOptions>() ?? new RobotOptions();
var optAw = cfg.GetSection("Aw").Get<AwOptions>() ?? new AwOptions();
var optEdi = cfg.GetSection("Edi").Get<EdiOptions>() ?? new EdiOptions();
var selectores = cfg.GetSection("Selectores").GetChildren().ToDictionary(s => s.Key, s => s.Value ?? string.Empty);

ILoggerFactory Consola() => LoggerFactory.Create(b => b.AddSimpleConsole(o => { o.TimestampFormat = "HH:mm:ss "; o.SingleLine = true; }));
string Cs(string nombre) => cfg.GetConnectionString(nombre)
    ?? throw new InvalidOperationException($"Falta ConnectionStrings:{nombre} (appsettings.json o variable de entorno ConnectionStrings__{nombre}).");
bool Arg(string nombre) => args.Contains(nombre, StringComparer.OrdinalIgnoreCase);

// --probar-login [--cerrar]: abre A+W e inicia sesión (sin SQL ni cola); guarda el árbol de la ventana principal.
if (Arg("--probar-login"))
{
    using var logs = Consola();
    return PruebaLogin.Ejecutar(optAw, optRobot, selectores, logs, Arg("--cerrar"));
}

// --menu [--cerrar]: inicia sesión y guarda todas las opciones del menú lateral (para hallar la importación EDI).
if (Arg("--menu"))
{
    using var logs = Consola();
    return PruebaEdi.Menu(optAw, optRobot, selectores, logs, Arg("--cerrar"));
}

// --probar-edi [--cerrar]: inicia sesión y abre Menu.ImportacionEdi; guarda sus controles (no importa nada).
if (Arg("--probar-edi"))
{
    using var logs = Consola();
    return PruebaEdi.ProbarImportacion(optAw, optRobot, selectores, logs, Arg("--cerrar"));
}

// --encolar-edi <archivo.ASC> --lote N [--cliente 398] [--empresa VETRIKO]: deja una tarea de prueba en la cola común.
if (Arg("--encolar-edi"))
{
    using var logs = Consola();
    return await EncolarEdi.Ejecutar(new ColaTareasSql(Cs("Interfaz")), optEdi, args, logs);
}

if (!optEdi.Habilitado)
{
    Console.WriteLine("Rpa.AW no tiene flujos habilitados (Edi:Habilitado = false en appsettings.json). No hay nada que hacer.");
    return 3;
}
if (Arg("--una-vez")) optRobot.UnaVez = true;

// Una sola instancia por equipo, con el MISMO candado que RPA_AW (robot de retales de Wilthon): dos robots manejando
// la ventana de A+W a la vez se pelearían. Si el otro está corriendo, este se retira y vuelve a intentar en su próximo arranque.
using var candado = new Mutex(true, @"Global\RPA_AW_Robot", out bool esUnico);
if (!esUnico)
{
    Console.WriteLine("Otro robot de A+W ya está en ejecución en este equipo (Rpa.AW o RPA_AW); esta instancia termina.");
    return 2;
}

var version = typeof(Worker).Assembly.GetName().Version?.ToString() ?? "0";
builder.Services.AddSingleton(optRobot);
builder.Services.AddSingleton(optAw);
builder.Services.AddSingleton(optEdi);
builder.Services.AddSingleton<SesionAw>();
builder.Services.AddSingleton(sp => new Localizador(sp.GetRequiredService<SesionAw>().Automation, selectores,
    TimeSpan.FromSeconds(optAw.SegundosEsperaControl)));
builder.Services.AddSingleton<Capturador>();
builder.Services.AddSingleton<ITareasCola>(_ => new ColaTareasSql(Cs("Interfaz")));
builder.Services.AddSingleton<IRegistroRobot>(_ => new RegistroRobotSql(Cs("Interfaz"), optRobot.Maquina, "Rpa.AW", FlujosAw.Todos, version));
builder.Services.AddSingleton<IPedidosAw>(_ => new PedidosAwSql(Cs("AwSql")));
builder.Services.AddSingleton<IImportadorEdi, EjecutorEdiFlaUI>();
builder.Services.AddSingleton(sp => new ProcesadorPedidoEdi(sp.GetRequiredService<ITareasCola>(), sp.GetRequiredService<IPedidosAw>(),
    sp.GetRequiredService<IImportadorEdi>(), optEdi, optRobot.Maquina));
builder.Services.AddHostedService<Worker>();
builder.Logging.AddSimpleConsole(o => { o.TimestampFormat = "HH:mm:ss "; o.SingleLine = true; });

await builder.Build().RunAsync();
return 0;
