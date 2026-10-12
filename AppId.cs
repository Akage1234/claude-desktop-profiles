using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

// Sets System.AppUserModel.* on windows and shortcuts so Windows groups/pins them separately.
public static class AppId
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint c);
        void GetAt(uint i, out PROPERTYKEY k);
        void GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        void SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
        void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }

    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, out IPropertyStore ps);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);

    static readonly Guid FMT = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    const uint ID = 5, RELAUNCH_CMD = 2, RELAUNCH_ICON = 3, RELAUNCH_NAME = 4;
    static readonly HashSet<IntPtr> done = new HashSet<IntPtr>();

    static void Set(IPropertyStore s, uint pid, string v)
    {
        var k = new PROPERTYKEY { fmtid = FMT, pid = pid };
        var pv = new PROPVARIANT { vt = 31 /* VT_LPWSTR */, p = Marshal.StringToCoTaskMemUni(v) };
        try { s.SetValue(ref k, ref pv); } finally { Marshal.FreeCoTaskMem(pv.p); }
    }

    public static void TagWindows(uint[] pids, string id, string cmd, string icon, string name)
    {
        var want = new HashSet<uint>(pids);
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (want.Contains(pid) && IsWindowVisible(h) && GetWindow(h, 4 /* GW_OWNER */) == IntPtr.Zero && done.Add(h))
            {
                var iid = typeof(IPropertyStore).GUID;
                IPropertyStore s;
                if (SHGetPropertyStoreForWindow(h, ref iid, out s) == 0)
                {
                    // Relaunch props must be set before the ID.
                    Set(s, RELAUNCH_CMD, cmd); Set(s, RELAUNCH_ICON, icon); Set(s, RELAUNCH_NAME, name); Set(s, ID, id);
                    Marshal.ReleaseComObject(s);
                }
            }
            return true;
        }, IntPtr.Zero);
    }

    public static void TagShortcut(string lnk, string id)
    {
        var o = new CShellLink();
        ((IPersistFile)o).Load(lnk, 2 /* STGM_READWRITE */);
        var s = (IPropertyStore)o;
        Set(s, ID, id);
        s.Commit();
        ((IPersistFile)o).Save(lnk, true);
        Marshal.ReleaseComObject(o);
    }
}
