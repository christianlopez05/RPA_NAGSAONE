// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Rpa.AW.Cliente;

namespace Rpa.AW.Pantallas;

// Acciones comunes sobre controles de A+W. Cada pantalla concreta hereda de aquí (patrón Page Object):
// si A+W cambia una pantalla, solo se toca su clase y sus selectores.
public abstract class PantallaBase
{
    protected readonly Localizador Loc;
    protected readonly Window Ventana;

    protected PantallaBase(Localizador loc, Window ventana)
    {
        Loc = loc;
        Ventana = ventana;
    }

    // A+W (Gupta) solo registra lo que se TECLEA: asignar el valor por UIA no dispara sus validaciones
    // (p. ej. buscar la designación del artículo). Se vacía el campo, se teclea, se sale con TAB y se verifica.
    // Si A+W responde con un mensaje (artículo inexistente, valor inválido…) se cierra y se lanza su texto.
    protected void Escribir(string clave, string texto)
    {
        var caja = Loc.Buscar(Ventana, clave).AsTextBox();
        Win32Ventanas.TraerAlFrente(Hwnd);
        try { caja.Text = string.Empty; } catch { /* algunos campos no admiten asignación */ }
        caja.Focus();
        Thread.Sleep(150);
        Win32Ventanas.DesactivarBloqMayus();
        Keyboard.Type(texto);
        Keyboard.Type(VirtualKeyShort.TAB);
        Thread.Sleep(500);

        var mensajes = Win32Ventanas.AceptarMensajes(Ventana.Properties.ProcessId.Value);
        if (mensajes.Count > 0)
            throw new MensajeAwException(clave, string.Join(" | ", mensajes.Select(m => m.Texto)));

        var quedo = Leer(clave);
        if (!string.Equals(quedo, texto.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException($"El campo {clave} quedó con '{quedo}' en vez de '{texto}'.");
    }

    protected string Leer(string clave)
    {
        var el = Loc.Buscar(Ventana, clave, TimeSpan.FromSeconds(5));
        var valor = el.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
        if (valor == null && el.Properties.NativeWindowHandle.TryGetValue(out var h))
            valor = Win32Ventanas.TextoControl(h);   // combos de Gupta: WM_GETTEXT da el texto elegido
        return (valor ?? string.Empty).Trim();
    }

    // Combos de Gupta: se elige con el TECLADO, como una persona (Inicio + flecha abajo hasta la opción), para que
    // A+W reciba sus eventos de cambio. La posición se calcula leyendo las opciones del combo (CB_GETLBTEXT).
    // Se compara sin distinguir mayúsculas e ignorando espacios repetidos.
    protected void Seleccionar(string clave, string texto)
    {
        var el = Loc.Buscar(Ventana, clave);
        var h = el.Properties.NativeWindowHandle.Value;
        var opciones = Win32Ventanas.ItemsCombo(h);
        var indice = opciones.FindIndex(o => Normalizar(o) == Normalizar(texto));
        if (indice < 0)
            throw new InvalidOperationException(
                $"'{texto}' no está entre las {opciones.Count} opciones de {clave}" +
                (opciones.Count > 0 ? $" (p. ej. '{string.Join("', '", opciones.Take(5))}')." : "."));

        Win32Ventanas.TraerAlFrente(Hwnd);
        el.Focus();
        Thread.Sleep(150);
        Keyboard.Type(VirtualKeyShort.HOME);
        for (int i = 0; i < indice; i++) { Keyboard.Type(VirtualKeyShort.DOWN); Thread.Sleep(30); }
        Thread.Sleep(200);
        Keyboard.Type(VirtualKeyShort.TAB);
        Thread.Sleep(400);

        var mensajes = Win32Ventanas.AceptarMensajes(Ventana.Properties.ProcessId.Value);
        if (mensajes.Count > 0)
            throw new MensajeAwException(clave, string.Join(" | ", mensajes.Select(m => m.Texto)));
        if (Win32Ventanas.SeleccionCombo(h) != indice)
            throw new InvalidOperationException($"{clave} quedó con '{Win32Ventanas.TextoControl(h)}' en vez de '{texto}'.");
    }

    // Botón de opción (Gupta:GPRadio): foco + Espacio; si no queda marcado, Invoke por UIA. Se verifica con BM_GETCHECK.
    protected void Marcar(string clave)
    {
        var el = Loc.Buscar(Ventana, clave);
        var h = el.Properties.NativeWindowHandle.Value;
        if (Win32Ventanas.EstadoMarca(h) == 1) return;

        Win32Ventanas.TraerAlFrente(Hwnd);
        el.Focus();
        Thread.Sleep(150);
        Keyboard.Type(VirtualKeyShort.SPACE);
        Thread.Sleep(300);
        if (Win32Ventanas.EstadoMarca(h) != 1)
        {
            try { el.Patterns.Invoke.PatternOrDefault?.Invoke(); } catch { }
            Thread.Sleep(300);
        }
        if (Win32Ventanas.EstadoMarca(h) != 1)
            throw new InvalidOperationException($"No se pudo marcar {clave} ('{el.Name}').");
    }

    private static string Normalizar(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    // "Atajo:Ctrl+N" = combinación de teclas (los botones del ribbon de A+W son dibujados y no se exponen a UIA).
    protected void Clic(string clave)
    {
        var valor = Loc.Valor(clave);
        if (valor.StartsWith("Atajo:", StringComparison.OrdinalIgnoreCase))
        {
            Win32Ventanas.TraerAlFrente(Hwnd);
            Win32Ventanas.Atajo(valor["Atajo:".Length..]);
            return;
        }
        Loc.Buscar(Ventana, clave).AsButton().Invoke();
    }

    // Abre (o activa, si ya está abierta) la pantalla del menú lateral cuya ruta está en el selector, p. ej.
    // "Gestión de planchas restantes". El título de la pantalla es el último tramo de la ruta.
    protected IntPtr AbrirMenu(string clave)
    {
        var ruta = Loc.Valor(clave);
        var titulo = ruta.Split('>', StringSplitOptions.TrimEntries)[^1];
        return MenuAw.AbrirPantalla(Hwnd, ruta, titulo, TimeSpan.FromSeconds(30));
    }

    protected IntPtr Hwnd => Ventana.Properties.NativeWindowHandle.Value;

    // Si A+W abrió una ventana modal (error, advertencia, confirmación), devuelve su texto.
    protected string? MensajeVentanaModal()
    {
        var modales = Ventana.ModalWindows;
        if (modales.Length == 0) return null;
        var textos = modales[0]
            .FindAllDescendants(c => c.ByControlType(ControlType.Text))
            .Select(t => t.Name)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        return $"{modales[0].Title}: {string.Join(" ", textos)}".Trim();
    }

    // Cierra modales que hayan quedado abiertos tras un error, para no bloquear el siguiente movimiento.
    protected void CerrarModales()
    {
        foreach (var m in Ventana.ModalWindows)
        {
            try { m.Close(); } catch { /* si no cierra, el siguiente intento lo reportará */ }
        }
    }
}

// A+W respondió con un cuadro de mensaje al escribir un campo (el texto es el del mensaje).
public class MensajeAwException : Exception
{
    public string Campo { get; }
    public MensajeAwException(string campo, string mensaje) : base($"A+W al escribir {campo}: {mensaje}") => Campo = campo;
}
