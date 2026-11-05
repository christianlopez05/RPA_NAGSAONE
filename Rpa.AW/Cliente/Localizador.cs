// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;

namespace Rpa.AW.Cliente;

// Traduce los selectores de appsettings ("AutomationId:txtX > Name:Guardar") a búsquedas FlaUI,
// esperando a que el control aparezca (A+W puede tardar en abrir ventanas).
public class Localizador
{
    private readonly UIA3Automation _automation;
    private readonly IReadOnlyDictionary<string, string> _selectores;
    private readonly TimeSpan _espera;

    public Localizador(UIA3Automation automation, IReadOnlyDictionary<string, string> selectores, TimeSpan espera)
    {
        _automation = automation;
        _selectores = selectores;
        _espera = espera;
    }

    public bool EstaConfigurado(string clave) =>
        _selectores.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v) && !v.StartsWith("POR_DEFINIR");

    // Claves usadas que todavía no tienen valor real (se valida al arrancar el robot).
    public IEnumerable<string> PendientesDe(IEnumerable<string> claves) => claves.Where(c => !EstaConfigurado(c));

    // Valor crudo del selector (para los que no son UIA: "Atajo:Ctrl+N", rutas del menú lateral).
    public string Valor(string clave) =>
        EstaConfigurado(clave) ? _selectores[clave].Trim() : throw new SelectorNoConfiguradoException(clave);

    public AutomationElement Buscar(AutomationElement raiz, string clave, TimeSpan? espera = null)
    {
        if (!EstaConfigurado(clave))
            throw new SelectorNoConfiguradoException(clave);

        var limite = DateTime.Now + (espera ?? _espera);
        while (true)
        {
            var el = Resolver(raiz, _selectores[clave]);
            if (el != null) return el;
            if (DateTime.Now >= limite)
                throw new TimeoutException($"No apareció el control '{clave}' ({_selectores[clave]}) en {(espera ?? _espera).TotalSeconds:0} s.");
            Thread.Sleep(300);
        }
    }

    public AutomationElement? BuscarOpcional(AutomationElement raiz, string clave) =>
        EstaConfigurado(clave) ? Resolver(raiz, _selectores[clave]) : null;

    private AutomationElement? Resolver(AutomationElement raiz, string selector)
    {
        AutomationElement? actual = raiz;
        foreach (var nivel in selector.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            actual = actual?.FindFirstDescendant(Condicion(nivel));
            if (actual == null) return null;
        }
        return actual;
    }

    private ConditionBase Condicion(string nivel)
    {
        var i = nivel.IndexOf(':');
        if (i <= 0) throw new FormatException($"Selector '{nivel}' inválido: use Tipo:Valor (AutomationId, Name o ClassName).");
        var tipo = nivel[..i].Trim();
        var valor = nivel[(i + 1)..].Trim();
        var cf = _automation.ConditionFactory;
        return tipo.ToLowerInvariant() switch
        {
            "automationid" => cf.ByAutomationId(valor),
            "name" => cf.ByName(valor),
            "classname" => cf.ByClassName(valor),
            _ => throw new FormatException($"Tipo de selector '{tipo}' no soportado (AutomationId, Name o ClassName).")
        };
    }
}

public class SelectorNoConfiguradoException : Exception
{
    public SelectorNoConfiguradoException(string clave)
        : base($"El selector '{clave}' no está configurado en appsettings.json (Selectores). Inspecciónelo con FlaUInspect.") { }
}
