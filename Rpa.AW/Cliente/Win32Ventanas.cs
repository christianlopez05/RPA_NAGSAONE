// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Rpa.AW.Cliente;

// Utilidades Win32 que NO dependen de UI Automation. A+W (Gupta) bloquea su hilo de interfaz mientras
// carga y UIA responde "UIA Timeout" si se le consulta antes; con esto se espera a que la app responda.
public static class Win32Ventanas
{
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hWnd);

    public sealed record MensajeAceptado(string Texto, bool Cerrado, string Boton);

    // Cierra los cuadros de mensaje (#32770) visibles del proceso SIN usar UI Automation: mientras un mensaje
    // de A+W está abierto, A+W no responde a UIA. Lee su texto y elige el botón por su id estándar:
    //   - Pregunta Sí/No ("¿Quieres grabar los datos modificados?") → SIEMPRE "No" (o Cancelar).
    //     El robot nunca responde "Sí": grabar solo ocurre por su paso explícito de Grabar.
    //   - Aviso con un solo Ok → Ok.
    //   - Botones sin id reconocible → no se toca (se informa como no cerrado).
    // Comprueba que el mensaje se cerró; si no, insiste con WM_COMMAND del mismo botón.
    // responderSi: solo para el paso explícito de Grabar del robot. Si devuelve true para el texto de una pregunta
    // Sí/No (p. ej. "¿Quieres grabar los datos modificados?" tras escribir el retal), se responde Sí.
    public static List<MensajeAceptado> AceptarMensajes(int pid, Func<string, bool>? responderSi = null)
    {
        const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;
        var aceptados = new List<MensajeAceptado>();
        var dialogos = new List<IntPtr>();
        var erroresAw = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var p);
            if (p == pid && IsWindowVisible(h))
            {
                if (Clase(h) == "#32770") dialogos.Add(h);
                // Ventana propia de A+W "Error de base de datos" (sentencia SQL + mensaje, botón Ok).
                else if (Clase(h) == "Gupta:AccFrame" && Texto(h).StartsWith("Error", StringComparison.OrdinalIgnoreCase)) erroresAw.Add(h);
            }
            return true;
        }, IntPtr.Zero);

        foreach (var err in erroresAw)
        {
            var textos = new List<string>();
            IntPtr ok = IntPtr.Zero;
            EnumChildWindows(err, (h, _) =>
            {
                var c = Clase(h);
                if (c == "Edit") { var t = TextoControl(h).Trim(); if (t.Length > 0) textos.Add(t.Replace("\r\n", " ")); }
                if (c == "Button" && Texto(h).Replace("&", "").Equals("Ok", StringComparison.OrdinalIgnoreCase)) ok = h;
                return true;
            }, IntPtr.Zero);
            var texto = $"{Texto(err)}: {string.Join(" / ", textos)}";
            if (ok == IntPtr.Zero) { aceptados.Add(new MensajeAceptado(texto, false, "(sin botón Ok)")); continue; }
            SendMessage(ok, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
            aceptados.Add(new MensajeAceptado(texto, EsperarCierre(err, TimeSpan.FromSeconds(3)), "Ok"));
        }

        foreach (var dlg in dialogos)
        {
            var textos = new List<string>();
            var botones = new Dictionary<int, IntPtr>();
            EnumChildWindows(dlg, (h, _) =>
            {
                var c = Clase(h);
                if (c == "Static" && !string.IsNullOrWhiteSpace(Texto(h))) textos.Add(Texto(h));
                if (c == "Button" && IsWindowVisible(h)) botones.TryAdd(GetDlgCtrlID(h), h);
                return true;
            }, IntPtr.Zero);
            var titulo = Texto(dlg);
            var cuerpo = string.Join(" ", textos);
            var texto = string.IsNullOrWhiteSpace(titulo) ? cuerpo : $"{titulo}: {cuerpo}";

            int id = botones.ContainsKey(IDYES) && responderSi?.Invoke(texto) == true ? IDYES
                   : botones.ContainsKey(IDNO) ? IDNO
                   : botones.ContainsKey(IDYES) ? (botones.ContainsKey(IDCANCEL) ? IDCANCEL : 0)
                   : botones.ContainsKey(IDOK) ? IDOK
                   : botones.ContainsKey(IDCANCEL) ? IDCANCEL : 0;
            if (id == 0)
            {
                aceptados.Add(new MensajeAceptado(texto, false, "(ninguno: botones no reconocidos)"));
                continue;
            }
            var boton = botones[id];
            var nombre = Texto(boton).Replace("&", "");

            SendMessage(boton, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
            bool cerrado = EsperarCierre(dlg, TimeSpan.FromSeconds(2));
            if (!cerrado)
            {
                PostMessage(dlg, 0x0111, new IntPtr(id), boton);    // WM_COMMAND con el id del botón
                cerrado = EsperarCierre(dlg, TimeSpan.FromSeconds(2));
            }
            aceptados.Add(new MensajeAceptado(texto, cerrado, nombre));
        }
        return aceptados;
    }

    private static bool EsperarCierre(IntPtr dlg, TimeSpan max)
    {
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed < max)
        {
            if (!IsWindow(dlg) || !IsWindowVisible(dlg)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    // Ventana visible del proceso cuyo título contiene el texto (Win32, no se bloquea).
    public static IntPtr BuscarVentana(int pid, string tituloContiene)
    {
        IntPtr hallada = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var p);
            if (p == pid && IsWindowVisible(h) && Texto(h).Contains(tituloContiene, StringComparison.OrdinalIgnoreCase))
            {
                hallada = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return hallada;
    }

    private static string Texto(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
    private static string Clase(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }

    [DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // El robot escribe tecla por tecla: con Bloq Mayús activo se invierten mayúsculas/minúsculas
    // (una clave "Abc" llega como "aBC"). Se desactiva antes de escribir.
    public static void DesactivarBloqMayus()
    {
        const int VK_CAPITAL = 0x14;
        if ((GetKeyState(VK_CAPITAL) & 1) == 0) return;
        keybd_event(VK_CAPITAL, 0x45, 0x1, UIntPtr.Zero);         // presionar
        keybd_event(VK_CAPITAL, 0x45, 0x1 | 0x2, UIntPtr.Zero);   // soltar
        Thread.Sleep(100);
    }

    // Trae la ventana al frente (restaurándola si está minimizada): las teclas del robot van a la ventana activa.
    public static void TraerAlFrente(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        if (IsIconic(hWnd)) ShowWindow(hWnd, 9);   // SW_RESTORE
        SetForegroundWindow(hWnd);
        Thread.Sleep(150);
    }

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);

    // Jerarquía Win32 de ventanas hijas (clase, id, texto, rectángulo). Los controles Prof-UIS de A+W
    // (menú lateral, ribbon) no se exponen a UIA, pero sí tienen HWND propios.
    public static string DescribirHijos(IntPtr raiz)
    {
        var sb = new StringBuilder();
        var nivel = new Dictionary<IntPtr, int> { [raiz] = 0 };
        EnumChildWindows(raiz, (h, _) =>
        {
            var padre = GetParent(h);
            var n = nivel.TryGetValue(padre, out var np) ? np + 1 : 1;
            nivel[h] = n;
            GetWindowRect(h, out var r);
            sb.AppendLine($"{new string(' ', n * 2)}hwnd=0x{h.ToInt64():X} clase='{Clase(h)}' id={GetDlgCtrlID(h)} " +
                          $"texto='{Texto(h)}' visible={IsWindowVisible(h)} rect=({r.Left},{r.Top})-({r.Right},{r.Bottom})");
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }

    // ---- Pantallas MDI (cada pantalla de A+W es una ventana Gupta:AccFrame dentro del MDIClient) ----

    public static IntPtr BuscarHijaMdi(IntPtr principal, string titulo)
    {
        IntPtr hallada = IntPtr.Zero;
        EnumChildWindows(principal, (h, _) =>
        {
            if (Clase(h) == "Gupta:AccFrame" && IsWindowVisible(h)
                && Texto(h).Equals(titulo, StringComparison.OrdinalIgnoreCase)) { hallada = h; return false; }
            return true;
        }, IntPtr.Zero);
        return hallada;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageTexto(IntPtr h, int msg, IntPtr w, StringBuilder l);

    // WM_GETTEXT (a diferencia de GetWindowText, devuelve el contenido de controles de otro proceso).
    public static string TextoControl(IntPtr h)
    {
        var sb = new StringBuilder(1024);
        SendMessageTexto(h, 0x000D, (IntPtr)sb.Capacity, sb);
        return sb.ToString();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageNum(IntPtr h, int msg, IntPtr w, IntPtr l);

    // Opciones de un ComboBox nativo (CB_GETCOUNT / CB_GETLBTEXT; Windows los traslada entre procesos).
    public static List<string> ItemsCombo(IntPtr combo)
    {
        var r = new List<string>();
        int n = (int)SendMessageNum(combo, 0x0146, IntPtr.Zero, IntPtr.Zero);   // CB_GETCOUNT
        for (int i = 0; i < n; i++)
        {
            int largo = (int)SendMessageNum(combo, 0x0149, (IntPtr)i, IntPtr.Zero);   // CB_GETLBTEXTLEN
            var sb = new StringBuilder(Math.Max(largo, 0) + 2);
            if (largo > 0) SendMessageTexto(combo, 0x0148, (IntPtr)i, sb);           // CB_GETLBTEXT
            r.Add(sb.ToString());
        }
        return r;
    }

    public static int SeleccionCombo(IntPtr combo) => (int)SendMessageNum(combo, 0x0147, IntPtr.Zero, IntPtr.Zero);   // CB_GETCURSEL

    // Estado de un botón de opción/casilla (BM_GETCHECK): 1 = marcado.
    public static int EstadoMarca(IntPtr boton) => (int)SendMessageNum(boton, 0x00F0, IntPtr.Zero, IntPtr.Zero);

    // Cierra una pantalla MDI de A+W sin grabar: WM_CLOSE y, si pregunta "¿grabar los datos modificados?",
    // AceptarMensajes responde No. Devuelve los mensajes que hubo y si quedó cerrada.
    public static (bool Cerrada, List<MensajeAceptado> Mensajes) CerrarHijaMdi(IntPtr hija, int pid)
    {
        var mensajes = new List<MensajeAceptado>();
        PostMessage(hija, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed < TimeSpan.FromSeconds(8))
        {
            Thread.Sleep(300);
            mensajes.AddRange(AceptarMensajes(pid));
            if (!IsWindow(hija) || !IsWindowVisible(hija)) return (true, mensajes);
        }
        return (false, mensajes);
    }

    public static void ActivarHijaMdi(IntPtr hija)
    {
        SendMessage(GetParent(hija), 0x0222, hija, IntPtr.Zero);   // WM_MDIACTIVATE al MDIClient
        Thread.Sleep(300);
    }

    // Control con el foco en el hilo de interfaz de la ventana (sirve con otros procesos, sin UIA).
    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr padre, IntPtr hijo);

    public static (IntPtr Hwnd, int Id, string Clase) Foco(IntPtr ventana)
    {
        var g = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        GetGUIThreadInfo(GetWindowThreadProcessId(ventana, out _), ref g);
        return (g.hwndFocus, g.hwndFocus == IntPtr.Zero ? 0 : GetDlgCtrlID(g.hwndFocus), g.hwndFocus == IntPtr.Zero ? "" : Clase(g.hwndFocus));
    }

    public static bool FocoDentroDe(IntPtr ventana) { var f = Foco(ventana).Hwnd; return f != IntPtr.Zero && IsChild(ventana, f); }

    // ---- Mouse y teclado reales (para los controles dibujados de A+W, que no reaccionan a mensajes) ----
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    public static void Clic(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(80);
        mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);   // izquierdo abajo
        mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);   // izquierdo arriba
    }

    public static void DobleClic(int x, int y)
    {
        Clic(x, y);
        Thread.Sleep((int)Math.Min(100, GetDoubleClickTime() / 4));
        mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
    }

    // Tecla enviada directamente a un control (WM_KEYDOWN/WM_KEYUP), sin necesitar el foco ni la ventana al frente.
    // La grilla del detalle de A+W la procesa: Inicio/flechas mueven la línea seleccionada y A+W la carga en el formulario.
    public static void TeclaA(IntPtr control, byte vk)
    {
        PostMessage(control, 0x0100, (IntPtr)vk, (IntPtr)1);
        PostMessage(control, 0x0101, (IntPtr)vk, unchecked((IntPtr)(int)0xC0000001));
    }

    public static void Tecla(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 0x2, UIntPtr.Zero);
        Thread.Sleep(80);
    }

    // Atajo de A+W, p. ej. "Ctrl+N" (Nuevo) o "Ctrl+S" (Grabar). Admite Ctrl/Alt/Shift + letra, dígito o F1–F12.
    public static void Atajo(string atajo)
    {
        var partes = atajo.Split('+', StringSplitOptions.TrimEntries);
        var mods = new List<byte>();
        foreach (var m in partes[..^1])
            mods.Add(m.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 0x11, "ALT" => 0x12, "SHIFT" or "MAYUS" => 0x10,
                _ => throw new ArgumentException($"Modificador no reconocido en el atajo '{atajo}'.")
            });
        var k = partes[^1].ToUpperInvariant();
        byte vk = k is "ENTER" or "INTRO" ? (byte)0x0D
                : k is "ESC" or "ESCAPE" ? (byte)0x1B
                : k.Length == 1 && char.IsLetterOrDigit(k[0]) ? (byte)k[0]
                : k.StartsWith('F') && int.TryParse(k[1..], out var f) && f is >= 1 and <= 12 ? (byte)(0x6F + f)
                : throw new ArgumentException($"Tecla no reconocida en el atajo '{atajo}'.");
        foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
        Tecla(vk);
        foreach (var m in Enumerable.Reverse(mods)) keybd_event(m, 0, 0x2, UIntPtr.Zero);
        Thread.Sleep(150);
    }

    public static List<string> VentanasVisibles(int pid)
    {
        var r = new List<string>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var p);
            if (p == pid && IsWindowVisible(h))
            {
                var t = new StringBuilder(256); var c = new StringBuilder(256);
                GetWindowText(h, t, 256); GetClassName(h, c, 256);
                r.Add($"'{t}' (clase {c})");
            }
            return true;
        }, IntPtr.Zero);
        return r;
    }

    // Espera a que el proceso tenga ventana principal y responda. Informa cada ventana nueva que aparece.
    public static bool EsperarQueResponda(Process proceso, TimeSpan limite, Action<string>? informar = null)
    {
        var reloj = Stopwatch.StartNew();
        var vistas = new HashSet<string>();
        while (reloj.Elapsed < limite)
        {
            proceso.Refresh();
            if (proceso.HasExited) throw new InvalidOperationException($"A+W se cerró solo (código {proceso.ExitCode}).");
            foreach (var v in VentanasVisibles(proceso.Id))
                if (vistas.Add(v)) informar?.Invoke($"{reloj.Elapsed.TotalSeconds,5:0.0}s ventana: {v}");
            if (proceso.MainWindowHandle != IntPtr.Zero && proceso.Responding && reloj.Elapsed.TotalSeconds > 3) return true;
            Thread.Sleep(1000);
        }
        return false;
    }
}
