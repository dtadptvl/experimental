using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace KiloSessionManager;

internal static class DetachedProcess
{
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateBreakawayFromJob = 0x01000000;

    public static int Start(string executable, string arguments, string workingDirectory)
    {
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        var command = new StringBuilder($"\"{executable}\" {arguments}");

        if (!CreateProcessW(
                executable,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateNoWindow | CreateNewProcessGroup | CreateBreakawayFromJob,
                IntPtr.Zero,
                workingDirectory,
                ref startup,
                out var processInfo))
        {
            var firstError = Marshal.GetLastWin32Error();
            command = new StringBuilder($"\"{executable}\" {arguments}");
            if (!CreateProcessW(
                    executable,
                    command,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateNoWindow | CreateNewProcessGroup,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startup,
                    out processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Unable to launch detached SessionHost. First attempt error: {firstError}.");
            }
        }

        try
        {
            return unchecked((int)processInfo.dwProcessId);
        }
        finally
        {
            CloseHandle(processInfo.hThread);
            CloseHandle(processInfo.hProcess);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
