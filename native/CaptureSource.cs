using System.Runtime.InteropServices;
using System.Text;

namespace Frame;
public record CaptureSource(string Name, string Filter, bool Synthetic = false)
{
    public override string ToString() => Name;
    public static List<CaptureSource> List()
    {
        var items = new List<CaptureSource>(); int index = 0;
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Rect r, nint data) => {
            items.Add(new($"Tela {++index} · {r.Right-r.Left} × {r.Bottom-r.Top}", $"hmonitor={monitor.ToInt64()}")); return true;
        }, 0);
        EnumWindows((window, _) => {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            GetWindowThreadProcessId(window, out var pid);
            if (pid == Environment.ProcessId) return true;
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            if (title.Length == 0 || title.ToString() == "Program Manager") return true;
            GetClientRect(window, out var r); if (r.Right < 100 || r.Bottom < 100) return true;
            items.Add(new("Janela · " + title, $"hwnd={window.ToInt64()}")); return true;
        }, 0);
        items.Add(new("Animação de teste · não captura sua tela", "", true));
        return items;
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rect, nint data);
    delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint data);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,nint data);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(nint window,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(nint window,out Rect rect);
}
