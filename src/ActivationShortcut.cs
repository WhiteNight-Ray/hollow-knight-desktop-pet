using System.Runtime.InteropServices;

namespace HollowKnightPet;

internal sealed class SideButtonGesture
{
    int captured;
    public (bool Consume,bool Activate) Process(int button,bool pressed,int selected)
    {
        if(button is not (1 or 2)) return (false,false);
        int mask=1<<button;
        if(!pressed)
        {
            bool consume=(captured&mask)!=0;captured&=~mask;
            return (consume,false);
        }
        if((captured&mask)!=0) return (true,false);
        if(button!=selected) return (false,false);
        captured|=mask;
        return (true,true);
    }
}

internal sealed class ActivationShortcut : IDisposable
{
    internal const int Message=0x8001;
    readonly SideButtonGesture gesture=new();
    readonly HookProc callback;
    readonly nint target;
    readonly Thread listener;
    readonly ManualResetEventSlim ready=new(false);
    uint listenerId;
    nint hook;
    int button=1;
    public int Button {get=>Volatile.Read(ref button);set=>Volatile.Write(ref button,value);}
    public bool Installed=>hook!=0;
    public ActivationShortcut(nint target,int button=1)
    {
        this.target=target;Button=button;callback=OnMouse;
        listener=new Thread(Listen) {IsBackground=true,Name="Pet mouse shortcut"};
        listener.Start();ready.Wait();
    }
    void Listen()
    {
        listenerId=GetCurrentThreadId();
        PeekMessage(out _,0,0,0,0); // Create the thread's message queue before announcing readiness.
        hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0); // WH_MOUSE_LL
        ready.Set();
        try
        {
            if(hook!=0) while(GetMessage(out _,0,0,0)>0) { }
        }
        finally {if(hook!=0) {UnhookWindowsHookEx(hook);hook=0;}}
    }
    nint OnMouse(int code,nint message,nint data)
    {
        if(code>=0 && (message==0x20B || message==0x20C))
        {
            // MSLLHOOKSTRUCT.mouseData follows POINT; its high word identifies XBUTTON1/2.
            int button=(int)((uint)Marshal.ReadInt32(data,8)>>16);
            var action=gesture.Process(button,message==0x20B,Button);
            if(action.Activate)
            {
                // Keep the hook non-blocking: changing foreground may synchronously wait on another app.
                // Finish processing the mouse event before asking the UI thread to activate.
                PostMessage(target,Message,0,0);
            }
            if(action.Consume) return 1; // Consume down and up to prevent browser navigation.
        }
        return CallNextHookEx(hook,code,message,data);
    }
    public void Dispose()
    {
        PostThreadMessage(listenerId,0x12,0,0); // WM_QUIT; the listener owns hook cleanup.
        listener.Join(1000);ready.Dispose();GC.KeepAlive(callback);
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ThreadMessage {public nint Hwnd;public uint Message;public nuint WParam;public nint LParam;public uint Time;public int X,Y;public uint Private;}
    delegate nint HookProc(int code,nint message,nint data);
    [DllImport("user32.dll",SetLastError=true)] static extern nint SetWindowsHookEx(int id,HookProc callback,nint module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("user32.dll")] static extern bool PostMessage(nint hwnd,int message,nint wParam,nint lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint GetModuleHandle(string? module);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool PeekMessage(out ThreadMessage message,nint hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] static extern int GetMessage(out ThreadMessage message,nint hwnd,uint min,uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread,int message,nint wParam,nint lParam);
}
