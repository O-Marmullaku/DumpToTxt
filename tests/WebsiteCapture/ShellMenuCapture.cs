using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

internal static class ShellMenuCapture
{
    // The caller must create the owner on its isolated desktop before entering this method.
    internal static void Capture(string fixture, string output, Form owner)
    {
        IShellItem? item = null;
        IContextMenu? context = null;
        nint menu = 0;
        try {
            Guid itemId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(fixture), 0, ref itemId, out item));
            Guid handler = new("3981e225-f559-11d3-8e3a-00c04f6837d5"), menuId = new("000214e4-0000-0000-c000-000000000046");
            item.BindToHandler(0, ref handler, ref menuId, out var unknown);
            context = (IContextMenu)unknown;
            menu = CreatePopupMenu();
            if (menu == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.ThrowExceptionForHR(context.QueryContextMenu(menu, 0, 1, 0x7fff, 0));
            int dumpIndex = -1;
            for (int i = 0; i < GetMenuItemCount(menu); i++) {
                var label = new StringBuilder(512);
                GetMenuString(menu, (uint)i, label, label.Capacity, 0x400);
                if (label.ToString().Contains("DumpToTxt", StringComparison.OrdinalIgnoreCase)) dumpIndex = i;
            }
            if (dumpIndex < 0) throw new InvalidOperationException("Install DumpToTxt before capturing its shell entry.");
            bool highlighted = false, captured = false;
            Exception? error = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) => {
                try {
                    if (!highlighted) {
                        if (!HiliteMenuItem(owner.Handle, menu, (uint)dumpIndex, 0x480)) throw new Win32Exception();
                        highlighted = true;
                        return;
                    }
                    timer.Stop();
                    nint popup = 0;
                    EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) => {
                        var name = new StringBuilder(256);
                        GetClassName(hwnd, name, name.Capacity);
                        if (name.ToString() != "#32768") return true;
                        popup = hwnd;
                        return false;
                    }, 0);
                    if (popup == 0 || !GetWindowRect(popup, out var rect)) throw new InvalidOperationException("Native shell popup was not found.");
                    using var bitmap = new Bitmap(rect.right - rect.left, rect.bottom - rect.top);
                    using (var graphics = Graphics.FromImage(bitmap)) {
                        var dc = graphics.GetHdc();
                        try { if (!PrintWindow(popup, dc, 2)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
                        finally { graphics.ReleaseHdc(dc); }
                    }
                    bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                    captured = true;
                } catch (Exception ex) { error = ex; timer.Stop(); }
                EndMenu();
            };
            timer.Start();
            // TPM_RETURNCMD only returns a selection; it never invokes a shell command.
            TrackPopupMenuEx(menu, 0x100, 80, 80, owner.Handle, 0);
            timer.Stop();
            if (error != null) throw new InvalidOperationException("Shell screenshot failed.", error);
            if (!captured) throw new InvalidOperationException("Shell menu closed before capture.");
        } finally {
            if (menu != 0) DestroyMenu(menu);
            if (context != null) Marshal.ReleaseComObject(context);
            if (item != null) Marshal.ReleaseComObject(item);
        }
    }
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellItem {
        void BindToHandler(nint bind, ref Guid handler, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }
    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IContextMenu {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
    delegate bool EnumProc(nint window, nint param);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, out IShellItem item);
    [DllImport("user32.dll", SetLastError = true)] static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(nint menu, uint item, StringBuilder label, int count, uint flags);
    [DllImport("user32.dll")] static extern bool HiliteMenuItem(nint hwnd, nint menu, uint item, uint flags);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint param);
    [DllImport("user32.dll")] static extern bool EndMenu();
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint id, EnumProc callback, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
