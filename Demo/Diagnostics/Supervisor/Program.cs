using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ReoGridDiagnosticSupervisor.exe <application.exe> <session-directory>");
    return 2;
}

string sessionDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(sessionDirectory);
string logPath = Path.Combine(sessionDirectory, "supervisor.log");
string desktopName = "ReoGridDiagnostic_" + Guid.NewGuid().ToString("N");
IntPtr desktop = Native.CreateDesktop(desktopName, null, IntPtr.Zero, 0, 0x01FF, IntPtr.Zero);
if (desktop == IntPtr.Zero)
{
    LogFailure(logPath, new Win32Exception(Marshal.GetLastWin32Error()).ToString());
    return 3;
}

try
{
    string application = Path.GetFullPath(args[0]);
    string commandLine = Quote(application) + " --diagnostic " + Quote(sessionDirectory) + " " + Quote(desktopName);
    var startup = new Native.StartupInfo
    {
        Size = Marshal.SizeOf<Native.StartupInfo>(),
        Desktop = "WinSta0\\" + desktopName,
        Flags = 1,
        ShowWindow = 1,
    };
    if (!Native.CreateProcess(application, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, 0x00000400,
        IntPtr.Zero, Path.GetDirectoryName(application), ref startup, out Native.ProcessInformation process))
    {
        LogFailure(logPath, new Win32Exception(Marshal.GetLastWin32Error()).ToString());
        return 4;
    }

    try
    {
        Native.WaitForSingleObject(process.Process, uint.MaxValue);
    }
    finally
    {
        Native.CloseHandle(process.Thread);
        Native.CloseHandle(process.Process);
    }
}
finally
{
    Native.CloseDesktop(desktop);
}

return 0;

static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
static void LogFailure(string path, string message) => File.AppendAllText(path, DateTimeOffset.UtcNow + " " + message + Environment.NewLine);

internal static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Length;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateDesktop(string name, string? device, IntPtr deviceMode, int flags, uint access, IntPtr securityAttributes);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
