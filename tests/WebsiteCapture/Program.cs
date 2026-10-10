using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DumpToTxt.App;
using DumpToTxt.Core;

internal static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.GetFullPath("artifacts/website");
    [STAThread]
    static int Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        if (args.Length == 2 && args[0] == "--capture") {
            nint input = Native.OpenInputDesktop(0, false, 1);
            if (input == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                string actual = DesktopName(Native.GetThreadDesktop(Native.GetCurrentThreadId()));
                if (actual != args[1] || actual == DesktopName(input)) throw new InvalidOperationException("Desktop isolation failed.");
                File.WriteAllText(Path.Combine(Output, "capture.log"), "Isolated desktop: " + actual + "\n");
            } finally { Native.CloseDesktop(input); }
            try { Capture(); File.AppendAllText(Path.Combine(Output, "capture.log"), "Capture complete\n"); } catch(Exception ex) { File.AppendAllText(Path.Combine(Output, "capture.log"), ex.ToString()); return 1; }
            return 0;
        }
        // Start on a separate desktop before STA/WinForms can create any window handles.
        string name = "DumpToTxtCapture-" + Guid.NewGuid().ToString("N");
        nint desktop = Native.CreateDesktop(name, 0, 0, 0, 0x01ff, 0);
        if (desktop == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var startup = new Native.Startup { cb = Marshal.SizeOf<Native.Startup>(), desktop = "winsta0\\" + name };
            var command = new StringBuilder("\"" + Environment.ProcessPath + "\" --capture " + name);
            if (!Native.CreateProcess(null, command, 0, 0, false, 0x08000000, 0, null, ref startup, out var process)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                if (Native.WaitForSingleObject(process.process, 60000) != 0) {
                    Native.TerminateProcess(process.process, 1);
                    throw new TimeoutException("Capture child exceeded 60 seconds.");
                }
                if (!Native.GetExitCodeProcess(process.process, out uint code)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return (int)code;
            } finally { Native.CloseHandle(process.thread); Native.CloseHandle(process.process); }
        } finally { Native.CloseDesktop(desktop); }
    }
    static string DesktopName(nint handle) {
        var name = new StringBuilder(256);
        if (!Native.GetUserObjectInformation(handle, 2, name, name.Capacity * 2, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return name.ToString();
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Capture()
    {
        string fixture = Path.Combine(Output, "weekend-project");
        Directory.CreateDirectory(Path.Combine(fixture, "src"));
        Directory.CreateDirectory(Path.Combine(fixture, "docs"));
        File.WriteAllText(Path.Combine(fixture, "README.md"), "# Weekend project\n\nA small weather dashboard.\n\n## Run locally\n\nnpm install\nnpm run dev\n");
        File.WriteAllText(Path.Combine(fixture, "package.json"), "{\n  \"name\": \"weekend-project\",\n  \"private\": true,\n  \"scripts\": { \"dev\": \"vite\" }\n}\n");
        File.WriteAllText(Path.Combine(fixture, "src", "main.js"), "const city = document.querySelector('#city');\nconst weather = document.querySelector('#weather');\n\nexport function showWeather(forecast) {\n  city.textContent = forecast.city;\n  weather.textContent = `${forecast.temperature} °C`;\n}\n");
        File.WriteAllText(Path.Combine(fixture, "src", "styles.css"), "body {\n  margin: 0;\n  font-family: system-ui, sans-serif;\n  background: #f7f7f7;\n  color: #242424;\n}\n\nmain { max-width: 48rem; margin: 4rem auto; }\n");
        File.WriteAllText(Path.Combine(fixture, "docs", "notes.md"), "# Next steps\n\n- Add the five-day forecast.\n- Remember the selected city.\n- Show a clear message when offline.\n");
        typeof(DumpSelectionForm).Assembly.GetType("ApplicationConfiguration", true)!.GetMethod("Initialize")!.Invoke(null, null);
        var theme = typeof(DumpSelectionForm).Assembly.GetType("DumpToTxt.App.UiTheme", true)!;
        theme.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, new object?[] { UiThemeKind.Graphite, null });
        var config = DumpConfig.CreateDefault();
        config.Theme = UiThemeKind.Graphite;
        config.Style = OutputStyle.Markdown;
        config.PlayCompletionSound = false;
        using var form = new DumpSelectionForm(fixture, config);
        form.Show();
        PumpUntil(() => Field<bool>(form, "_scanComplete"), "scan");
        if (Field<Label>(form, "_scanStatus").Text != "Scan complete") throw new InvalidOperationException("Scan did not complete.");
        if (Field<Label>(form, "_validation").Text.Length != 0) throw new InvalidOperationException("Fixture scan reported an issue.");
        Field<TreeView>(form, "_tree").ExpandAll(); Application.DoEvents();
        Save(form, "review.png");
        Field<Button>(form, "_previewButton").PerformClick();
        PumpUntil(() => Field<RichTextBox>(form, "_preview").Text.Contains("# Weekend project"), "preview");
        Save(form, "preview.png");
        File.AppendAllText(Path.Combine(Output, "capture.log"), "Closing form\n");
        form.Close();
        File.AppendAllText(Path.Combine(Output, "capture.log"), "Closed form\n");
        Application.DoEvents();
    }
    static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    static void PumpUntil(Func<bool> ready, string state) {
        var until = DateTime.UtcNow.AddSeconds(30);
        do { Application.DoEvents(); if (ready()) { Application.DoEvents(); return; } Thread.Sleep(20); } while (DateTime.UtcNow < until);
        throw new TimeoutException(state);
    }
    static void Save(Form form, string name) {
        form.Refresh(); Application.DoEvents();
        // Capture the live client area; noninteractive desktops do not compose title-bar shadows.
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        using (var graphics = Graphics.FromImage(bitmap)) {
            var dc = graphics.GetHdc();
            try { if (!Native.PrintWindow(form.Handle, dc, 3)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            finally { graphics.ReleaseHdc(dc); }
        }
        string path = Path.Combine(Output, name);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        File.AppendAllText(Path.Combine(Output, "capture.log"), $"{path} {bitmap.Width}x{bitmap.Height} SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}\n");
    }
    static class Native {
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct Startup {
            internal int cb; internal string? reserved, desktop, title;
            internal uint x,y,xsize,ysize,xchars,ychars,fill,flags;
            internal ushort show,reserved2; internal nint reservedPtr,input,output,error;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Process { internal nint process,thread; internal uint pid,tid; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool CreateProcess(string? app,StringBuilder cmd,nint processAttrs,nint threadAttrs,bool inherit,uint flags,nint env,string? dir,ref Startup startup,out Process process);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(nint handle,uint ms);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetExitCodeProcess(nint process,out uint code);
        [DllImport("kernel32.dll")] internal static extern bool TerminateProcess(nint process,uint code);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
        [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern nint CreateDesktop(string name,nint device,nint mode,uint flags,uint access,nint security);
        [DllImport("user32.dll", SetLastError=true)] internal static extern nint OpenInputDesktop(uint flags,bool inherit,uint access);
        [DllImport("user32.dll")] internal static extern bool CloseDesktop(nint desktop);
        [DllImport("user32.dll")] internal static extern nint GetThreadDesktop(uint thread);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool GetUserObjectInformation(nint obj,int index,StringBuilder info,int length,out int needed);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool PrintWindow(nint window,nint dc,uint flags);
    }
}
