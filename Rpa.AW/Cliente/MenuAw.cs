// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Rpa.AW.Cliente;

// Menú lateral de A+W (Prof-UIS): un "page navigator" con una página por módulo (Datos básicos, Documentos,
// Producción, Estadística, Almacén, …) y en cada página un árbol (Gupta:CExtTreeCtrl). Nada de esto se
// expone a UI Automation, pero los árboles son TreeView nativos: se leen con mensajes TVM_* (texto por
// memoria del proceso de A+W, que es de 32 bits). Abrir una opción exige un doble clic real del mouse:
// seleccionar el ítem por mensaje no basta (A+W abre el último ítem en que se hizo clic).
// Coordenadas: el robot no declara DPI awareness, igual que A+W, así que todo va en coordenadas lógicas.
public static class MenuAw
{
    private const int TVM_EXPAND = 0x1102, TVM_GETITEMRECT = 0x1104, TVM_GETNEXTITEM = 0x110A,
                      TVM_ENSUREVISIBLE = 0x1114, TVM_GETITEMW = 0x113E;
    private const int TVGN_ROOT = 0, TVGN_NEXT = 1, TVGN_CHILD = 4, TVGN_CARET = 9, TVE_EXPAND = 2;

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr w, EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Win32Ventanas.RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref PUNTO p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int acceso, bool heredar, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool IsWow64Process(IntPtr p, out bool wow64);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, int s, int t, int pr);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr p, IntPtr a, int s, int t);
    [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr p, IntPtr a, byte[] b, int s, out IntPtr n);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] b, int s, out IntPtr n);
    [StructLayout(LayoutKind.Sequential)] private struct PUNTO { public int X, Y; }

    // Abre una opción del menú lateral y devuelve la ventana MDI de la pantalla. Si la pantalla ya está
    // abierta solo la activa. "ruta" son los textos del árbol separados por " > " (p. ej.
    // "Gestión de planchas restantes" o "Consultas almacén > Historial almacén"); se busca en todas las páginas.
    public static IntPtr AbrirPantalla(IntPtr principal, string ruta, string titulo, TimeSpan max, Action<string>? informar = null)
    {
        var abierta = Win32Ventanas.BuscarHijaMdi(principal, titulo);
        if (abierta != IntPtr.Zero)
        {
            Win32Ventanas.ActivarHijaMdi(abierta);
            informar?.Invoke($"La pantalla '{titulo}' ya estaba abierta: se activa.");
            return abierta;
        }

        var partes = ruta.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        GetWindowThreadProcessId(principal, out var pid);
        var proc = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);   // VM_OPERATION|READ|WRITE|QUERY
        if (proc == IntPtr.Zero) throw new InvalidOperationException("No se pudo acceder al proceso de A+W para leer su menú.");
        try
        {
            IsWow64Process(proc, out var es32);
            IntPtr arbol = IntPtr.Zero, item = IntPtr.Zero;
            foreach (var a in Hijos(principal, "Gupta:CExtTreeCtrl"))
            {
                item = BuscarItem(proc, es32, a, partes);
                if (item != IntPtr.Zero) { arbol = a; break; }
            }
            if (item == IntPtr.Zero) throw new InvalidOperationException($"No se encontró '{ruta}' en el menú de A+W.");

            Win32Ventanas.TraerAlFrente(principal);
            if (!Visible(arbol)) MostrarPagina(principal, arbol, informar);

            // Tras cambiar de página el navegador anima el árbol: se espera a que deje de moverse.
            EsperarQueSeQuede(arbol, TimeSpan.FromSeconds(5));

            // 1) Seleccionar con un clic y confirmar qué ítem quedó seleccionado (TVGN_CARET). En A+W el clic real
            //    cae algo desplazado respecto de la posición que informa el árbol (≈ una fila más abajo, por el
            //    escalado de pantalla de Gupta): si se seleccionó otro ítem, se corrige por la diferencia entre
            //    ambas filas y se reintenta. Un clic simple en el menú solo selecciona, no abre nada.
            // 2) Abrir: doble clic en la posición ya confirmada.
            SendMessage(arbol, TVM_ENSUREVISIBLE, IntPtr.Zero, item);
            var r = RectItem(proc, arbol, item);
            int alto = Math.Max(1, r.Bottom - r.Top);
            var p = new PUNTO { X = r.Left + Math.Min(40, (r.Right - r.Left) / 2), Y = (r.Top + r.Bottom) / 2 };
            ClientToScreen(arbol, ref p);
            bool seleccionado = false;
            for (int intento = 1; intento <= 5 && !seleccionado; intento++)
            {
                Win32Ventanas.Clic(p.X, p.Y);
                Thread.Sleep(400);
                var actual = SendMessage(arbol, TVM_GETNEXTITEM, (IntPtr)TVGN_CARET, IntPtr.Zero);
                seleccionado = actual == item;
                if (seleccionado) break;

                int dy;
                if (actual != IntPtr.Zero)
                {
                    var ra = RectItem(proc, arbol, actual);
                    dy = (r.Top + r.Bottom) / 2 - (ra.Top + ra.Bottom) / 2;
                    if (dy == 0) dy = -alto;
                    informar?.Invoke($"Menú: el clic seleccionó '{Texto(proc, es32, arbol, actual)}'; se corrige {dy} px.");
                }
                else dy = -alto;
                p.Y += dy;
            }
            if (!seleccionado) throw new InvalidOperationException($"No se pudo seleccionar '{ruta}' en el menú de A+W.");
            informar?.Invoke($"Menú: '{ruta}' seleccionado; doble clic ({p.X},{p.Y}).");
            Win32Ventanas.DobleClic(p.X, p.Y);

            var reloj = Stopwatch.StartNew();
            while (reloj.Elapsed < max)
            {
                Win32Ventanas.AceptarMensajes((int)pid);
                var hija = Win32Ventanas.BuscarHijaMdi(principal, titulo);
                if (hija != IntPtr.Zero) return hija;
                Thread.Sleep(500);
            }
            throw new TimeoutException($"A+W no abrió la pantalla '{titulo}' tras el doble clic en '{ruta}'.");
        }
        finally { CloseHandle(proc); }
    }

    // Cambia de página en el navegador (botones dibujados, sin HWND). Se hace clic, de arriba abajo, en la
    // franja de botones bajo el árbol visible hasta que aparece el árbol buscado. Cambiar de página no
    // modifica datos; si un clic cae en el botón de configuración del pie, el menú que abre se cierra con Esc.
    private static void MostrarPagina(IntPtr principal, IntPtr arbol, Action<string>? informar)
    {
        var nav = Hijos(principal, "Gupta:CExtPageNavigatorWnd").FirstOrDefault();
        if (nav == IntPtr.Zero) throw new InvalidOperationException("No se encontró el navegador de páginas del menú de A+W.");
        GetWindowRect(nav, out var rn);
        var visible = Hijos(nav, "Gupta:CExtTreeCtrl").FirstOrDefault(Visible);
        int desde = rn.Top + 30;
        if (visible != IntPtr.Zero) { GetWindowRect(visible, out var rv); desde = rv.Bottom + 12; }

        for (int y = desde; y < rn.Bottom - 4; y += 8)
        {
            Win32Ventanas.Clic(rn.Left + 80, y);
            Thread.Sleep(400);
            if (Visible(arbol)) { informar?.Invoke($"Menú: página cambiada (clic en y={y})."); return; }
        }
        Win32Ventanas.Tecla(0x1B);   // Esc por si quedó abierto el menú del pie
        throw new InvalidOperationException("No se pudo mostrar la página del menú que contiene la opción.");
    }

    private static void EsperarQueSeQuede(IntPtr h, TimeSpan max)
    {
        var reloj = Stopwatch.StartNew();
        GetWindowRect(h, out var antes);
        int iguales = 0;
        while (reloj.Elapsed < max && iguales < 3)
        {
            Thread.Sleep(250);
            GetWindowRect(h, out var ahora);
            iguales = ahora.Equals(antes) ? iguales + 1 : 0;
            antes = ahora;
        }
    }

    private static bool Visible(IntPtr h)
    {
        if (!IsWindowVisible(h)) return false;
        GetWindowRect(h, out var r);
        return r.Right - r.Left > 20 && r.Bottom - r.Top > 20 && r.Left >= -2;
    }

    private static IntPtr BuscarItem(IntPtr proc, bool es32, IntPtr arbol, string[] partes)
    {
        IntPtr padre = IntPtr.Zero;
        var item = SendMessage(arbol, TVM_GETNEXTITEM, (IntPtr)TVGN_ROOT, IntPtr.Zero);
        for (int i = 0; i < partes.Length; i++)
        {
            while (item != IntPtr.Zero && !string.Equals(Texto(proc, es32, arbol, item), partes[i], StringComparison.OrdinalIgnoreCase))
                item = SendMessage(arbol, TVM_GETNEXTITEM, (IntPtr)TVGN_NEXT, item);
            if (item == IntPtr.Zero) return IntPtr.Zero;
            if (i == partes.Length - 1) return item;
            SendMessage(arbol, TVM_EXPAND, (IntPtr)TVE_EXPAND, item);
            padre = item;
            item = SendMessage(arbol, TVM_GETNEXTITEM, (IntPtr)TVGN_CHILD, padre);
        }
        return IntPtr.Zero;
    }

    // Lista de todas las opciones de los árboles (para explorar): "  Texto" con sangría por nivel.
    public static string DescribirMenu(IntPtr principal)
    {
        GetWindowThreadProcessId(principal, out var pid);
        var proc = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);
        var sb = new StringBuilder();
        try
        {
            IsWow64Process(proc, out var es32);
            foreach (var a in Hijos(principal, "Gupta:CExtTreeCtrl"))
            {
                sb.AppendLine($"=== Árbol hwnd=0x{a.ToInt64():X} visible={Visible(a)}");
                void Rec(IntPtr it, int nivel)
                {
                    for (; it != IntPtr.Zero; it = SendMessage(a, TVM_GETNEXTITEM, (IntPtr)TVGN_NEXT, it))
                    {
                        sb.AppendLine($"{new string(' ', nivel * 2)}{Texto(proc, es32, a, it)}");
                        if (nivel < 4) Rec(SendMessage(a, TVM_GETNEXTITEM, (IntPtr)TVGN_CHILD, it), nivel + 1);
                    }
                }
                Rec(SendMessage(a, TVM_GETNEXTITEM, (IntPtr)TVGN_ROOT, IntPtr.Zero), 1);
            }
        }
        finally { CloseHandle(proc); }
        return sb.ToString();
    }

    // TVM_GETITEMW con la estructura TVITEMW del proceso destino (32 o 64 bits) y el texto en su memoria.
    private static string Texto(IntPtr proc, bool es32, IntPtr arbol, IntPtr item)
    {
        var mem = VirtualAllocEx(proc, IntPtr.Zero, 4096, 0x3000, 0x04);
        try
        {
            var texto = mem + 128;
            byte[] tv;
            if (es32)
            {
                tv = new byte[40];
                BitConverter.GetBytes(0x1).CopyTo(tv, 0);                                  // TVIF_TEXT
                BitConverter.GetBytes((uint)item.ToInt64()).CopyTo(tv, 4);
                BitConverter.GetBytes((uint)texto.ToInt64()).CopyTo(tv, 16);
                BitConverter.GetBytes(1000).CopyTo(tv, 20);
            }
            else
            {
                tv = new byte[56];
                BitConverter.GetBytes(0x1).CopyTo(tv, 0);
                BitConverter.GetBytes(item.ToInt64()).CopyTo(tv, 8);
                BitConverter.GetBytes(texto.ToInt64()).CopyTo(tv, 24);
                BitConverter.GetBytes(1000).CopyTo(tv, 32);
            }
            WriteProcessMemory(proc, mem, tv, tv.Length, out _);
            SendMessage(arbol, TVM_GETITEMW, IntPtr.Zero, mem);
            var buf = new byte[2000];
            ReadProcessMemory(proc, texto, buf, buf.Length, out _);
            var s = Encoding.Unicode.GetString(buf);
            var fin = s.IndexOf('\0');
            return fin >= 0 ? s[..fin] : s;
        }
        finally { VirtualFreeEx(proc, mem, 0, 0x8000); }
    }

    // TVM_GETITEMRECT: el RECT entra con el handle del ítem en sus primeros bytes y sale en coordenadas cliente.
    private static Win32Ventanas.RECT RectItem(IntPtr proc, IntPtr arbol, IntPtr item)
    {
        var mem = VirtualAllocEx(proc, IntPtr.Zero, 64, 0x3000, 0x04);
        try
        {
            var b = new byte[16];
            BitConverter.GetBytes(item.ToInt64()).CopyTo(b, 0);
            WriteProcessMemory(proc, mem, b, 16, out _);
            SendMessage(arbol, TVM_GETITEMRECT, (IntPtr)1, mem);   // 1 = solo el texto
            ReadProcessMemory(proc, mem, b, 16, out _);
            return new Win32Ventanas.RECT
            {
                Left = BitConverter.ToInt32(b, 0), Top = BitConverter.ToInt32(b, 4),
                Right = BitConverter.ToInt32(b, 8), Bottom = BitConverter.ToInt32(b, 12)
            };
        }
        finally { VirtualFreeEx(proc, mem, 0, 0x8000); }
    }

    private static List<IntPtr> Hijos(IntPtr raiz, string clase)
    {
        var r = new List<IntPtr>();
        EnumChildWindows(raiz, (h, _) =>
        {
            var s = new StringBuilder(256);
            GetClassName(h, s, 256);
            if (s.ToString() == clase) r.Add(h);
            return true;
        }, IntPtr.Zero);
        return r;
    }
}
