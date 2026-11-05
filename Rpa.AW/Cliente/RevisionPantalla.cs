// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using System.Runtime.InteropServices;

namespace Rpa.AW.Cliente;

// Revisión de la pantalla al arrancar. El único paso del robot sensible al tamaño es el menú lateral de A+W:
// con poca altura el navegador de páginas oculta botones y la opción "Almacén" puede no quedar accesible.
// Se mide en coordenadas lógicas (el robot, igual que A+W, no declara DPI awareness): 1920×1080 al 125 % = 1536×864.
public static class RevisionPantalla
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int indice);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int indice);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int accion, int param, out RECT r, int ini);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, DESKTOPVERTRES = 117, DESKTOPHORZRES = 118, SPI_GETWORKAREA = 0x30;

    public sealed record Medida(int AnchoLogico, int AltoLogico, int AnchoFisico, int AltoFisico, int AltoUtil, int EscalaPct)
    {
        public override string ToString() =>
            $"{AnchoFisico}×{AltoFisico} al {EscalaPct} % (área lógica {AnchoLogico}×{AltoLogico}, alto útil sin barra de tareas {AltoUtil})";
    }

    public static Medida Medir()
    {
        int ancho = GetSystemMetrics(SM_CXSCREEN), alto = GetSystemMetrics(SM_CYSCREEN);
        int anchoF = ancho, altoF = alto;
        var hdc = GetDC(IntPtr.Zero);
        if (hdc != IntPtr.Zero)
        {
            anchoF = GetDeviceCaps(hdc, DESKTOPHORZRES);
            altoF = GetDeviceCaps(hdc, DESKTOPVERTRES);
            ReleaseDC(IntPtr.Zero, hdc);
        }
        int util = SystemParametersInfo(SPI_GETWORKAREA, 0, out var r, 0) ? r.Bottom - r.Top : alto;
        int escala = ancho > 0 ? (int)Math.Round(anchoF * 100.0 / ancho) : 100;
        return new Medida(ancho, alto, anchoF, altoF, util, escala);
    }

    // Devuelve el motivo si la pantalla es más chica que el mínimo (lógico) configurado; null si está bien.
    public static string? Problema(Medida m, int anchoMinimo, int altoMinimo)
    {
        if (m.AnchoLogico <= 0 || m.AltoLogico <= 0)
            return "no se pudo medir la pantalla (¿sesión sin escritorio o RDP desconectado?)";
        if (m.AnchoLogico < anchoMinimo || m.AltoLogico < altoMinimo)
            return $"el área lógica {m.AnchoLogico}×{m.AltoLogico} es menor que el mínimo recomendado {anchoMinimo}×{altoMinimo}";
        return null;
    }
}
