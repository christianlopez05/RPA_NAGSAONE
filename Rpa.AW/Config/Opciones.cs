namespace Rpa.AW.Config;

public class RobotOptions
{
    // TEST / PROD: solo informativo (aparece en logs y en el latido); lo que manda es AwSql y el cliente A+W configurado.
    public string Ambiente { get; set; } = "TEST";
    public string Maquina { get; set; } = Environment.MachineName;
    public int IntervaloColaSegundos { get; set; } = 10;
    public string CarpetaCapturas { get; set; } = @"C:\RPA_NAGSAONE\AW\Capturas";
    public bool CapturaEnCadaPaso { get; set; }
    // true (o --una-vez): procesa lo pendiente y termina, en vez de quedarse esperando.
    public bool UnaVez { get; set; }
    // Pantalla mínima recomendada, en coordenadas LÓGICAS (resolución ÷ escala; 1920×1080 al 125 % = 1536×864).
    public int AnchoMinimoPantalla { get; set; } = 1366;
    public int AltoMinimoPantalla { get; set; } = 768;
}

public class AwOptions
{
    public string RutaEjecutable { get; set; } = string.Empty;
    public string NombreProceso { get; set; } = string.Empty;
    public string TituloVentanaContiene { get; set; } = "A+W";
    public int SegundosEsperaControl { get; set; } = 20;
    public bool RequiereLogin { get; set; } = true;
    // Usuario de A+W PROPIO del robot, nunca el de una persona (docs/rpa-robots.md, regla 5).
    public string Usuario { get; set; } = string.Empty;
    // Base de datos de A+W a la que entrar (DEMO2026 en pruebas, VETRIKO en producción).
    public string BaseDatos { get; set; } = string.Empty;
    // Opcional: servidor a escribir en el login (vacío = el que trae A+W configurado).
    public string Servidor { get; set; } = string.Empty;
    // La clave de A+W solo se lee de una variable de entorno del usuario de Windows del robot, nunca de un archivo
    // (docs/rpa-robots.md, regla 6): setx RPA_AW_CLAVE "clave".
    public string VariableClave { get; set; } = "RPA_AW_CLAVE";

    public string LeerClave() =>
        Environment.GetEnvironmentVariable(VariableClave, EnvironmentVariableTarget.User)
        ?? Environment.GetEnvironmentVariable(VariableClave)
        ?? throw new InvalidOperationException(
            $"Falta la clave de A+W del robot: defina la variable de entorno {VariableClave} del usuario de Windows del robot.");
}
