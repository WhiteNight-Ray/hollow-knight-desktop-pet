using System.Runtime.InteropServices;
using System.Text;

namespace HollowKnightPet;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public readonly Rectangle Value => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X=x; Y=y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { public int W, H; public Size(int w, int h) { W=w; H=h; } }
    [StructLayout(LayoutKind.Sequential, Pack=1)] internal struct Blend { public byte Operation, Flags, Alpha, Format; }
    internal delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, nint param);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] internal static extern int Frame(nint hwnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] internal static extern int Attribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool UpdateLayeredWindow(nint hwnd, nint dstDC, ref Point dst, ref Size size, nint srcDC, ref Point src, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);

    internal static void Present(nint hwnd, Bitmap bitmap, int x, int y)
    {
        nint screen = GetDC(0), memory = CreateCompatibleDC(screen);
        nint handle = bitmap.GetHbitmap(Color.FromArgb(0)), previous = SelectObject(memory, handle);
        try
        {
            var dst = new Point(x,y); var src = new Point(0,0); var size = new Size(bitmap.Width,bitmap.Height);
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(hwnd,screen,ref dst,ref size,memory,ref src,0,ref blend,2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SelectObject(memory,previous); DeleteObject(handle); DeleteDC(memory); ReleaseDC(0,screen); }
    }
}

internal static class Desktop
{
    internal static ScreenEdges EdgesAt(Screen screen,int y)
    {
        var b=screen.WorkingArea;
        bool leftNeighbor=Screen.AllScreens.Any(s=>s.DeviceName!=screen.DeviceName && s.Bounds.Right==screen.Bounds.Left && y>=s.Bounds.Top && y<s.Bounds.Bottom);
        bool rightNeighbor=Screen.AllScreens.Any(s=>s.DeviceName!=screen.DeviceName && s.Bounds.Left==screen.Bounds.Right && y>=s.Bounds.Top && y<s.Bounds.Bottom);
        return new ScreenEdges(leftNeighbor ? null : b.Left,rightNeighbor ? null : b.Right);
    }
    internal static List<Platform> Scan()
    {
        var windows = new List<Platform>();
        var covers = new List<RectangleF>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd,out uint pid);
            if (pid == Environment.ProcessId) return true;
            if (Native.Attribute(hwnd,14,out int cloaked,4) == 0 && cloaked != 0) return true;
            var cls = new StringBuilder(256); Native.GetClassName(hwnd,cls,cls.Capacity);
            string name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "tooltips_class32") return true;
            int ex = Native.GetWindowLong(hwnd,-20);
            if ((ex & 0x80) != 0 || (ex & 0x20) != 0) return true;
            if (Native.Frame(hwnd,9,out var r,16) != 0 && !Native.GetWindowRect(hwnd,out r)) return true;
            RectangleF rect = r.Value;
            if (rect.Width < 80 || rect.Height < 45) return true;
            // EnumWindows is front-to-back: a fully obscured window is not a platform.
            if (!covers.Any(c => c.Contains(rect))) windows.Add(new Platform(hwnd,rect));
            covers.Add(rect);
            return true;
        },0);
        return windows;
    }

    internal static List<Platform> VisibleRoofs(IReadOnlyList<Platform> windows)
    {
        var result = new List<Platform>();
        var covers = new List<RectangleF>();
        foreach (var p in windows)
        {
            var spans = new List<(float left,float right)> { (p.Bounds.Left,p.Bounds.Right) };
            foreach (var c in covers.Where(c => c.Top <= p.Bounds.Top && c.Bottom > p.Bounds.Top))
            {
                var next = new List<(float,float)>();
                foreach (var (a,b) in spans)
                {
                    if (c.Right <= a || c.Left >= b) next.Add((a,b));
                    else { if (c.Left > a) next.Add((a,c.Left)); if(c.Right < b) next.Add((c.Right,b)); }
                }
                spans = next;
            }
            foreach (var (a,b) in spans)
                if (b-a > 2) result.Add(new Platform(p.Id,new RectangleF(a,p.Bounds.Top,b-a,p.Bounds.Height)));
            covers.Add(p.Bounds);
        }
        return result;
    }

    internal static List<WindowEdge> VisibleSides(IReadOnlyList<Platform> windows)
    {
        var result = new List<WindowEdge>();
        var covers = new List<RectangleF>();
        foreach (var window in windows.Where(p => !p.Floor))
        {
            var r = window.Bounds;
            foreach (int side in new[] { 1, -1 })
            {
                float x = side > 0 ? r.Left : r.Right;
                var spans = new List<(float top, float bottom)> { (r.Top, r.Bottom) };
                foreach (var cover in covers.Where(c => c.Left <= x && c.Right >= x))
                {
                    var next = new List<(float,float)>();
                    foreach (var (a,b) in spans)
                    {
                        if (cover.Bottom <= a || cover.Top >= b) next.Add((a,b));
                        else { if (cover.Top > a) next.Add((a,cover.Top)); if (cover.Bottom < b) next.Add((cover.Bottom,b)); }
                    }
                    spans = next;
                }
                foreach (var (a,b) in spans)
                    if (b-a > 2) result.Add(new WindowEdge(window.Id,side,x,a,b,r));
            }
            covers.Add(r);
        }
        return result;
    }

    internal static List<Platform> Floors() => Screen.AllScreens.Select((s,i) =>
        new Platform(-(i+1),new RectangleF(s.WorkingArea.Left,s.WorkingArea.Bottom,s.WorkingArea.Width,10000),true)).ToList();
}
