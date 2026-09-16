using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KiloSessionHost;

internal sealed class ConPtySession : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint Infinite = 0xFFFFFFFF;

    private readonly IntPtr _pseudoConsole;
    private readonly IntPtr _processHandle;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly object _resizeGate = new();
    private bool _disposed;

    public int ProcessId { get; }
    public Stream Output => _output;

    private ConPtySession(IntPtr pseudoConsole, IntPtr processHandle, int processId, FileStream input, FileStream output)
    {
        _pseudoConsole = pseudoConsole;
        _processHandle = processHandle;
        ProcessId = processId;
        _input = input;
        _output = output;
    }

    public static ConPtySession Start(string executable, string arguments, string workingDirectory, short cols = 120, short rows = 40)
    {
        IntPtr inputRead = IntPtr.Zero;
        IntPtr inputWrite = IntPtr.Zero;
        IntPtr outputRead = IntPtr.Zero;
        IntPtr outputWrite = IntPtr.Zero;
        IntPtr pseudoConsole = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr processHandle = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;

        try
        {
            var security = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                InheritHandle = true,
                SecurityDescriptor = IntPtr.Zero
            };

            if (!CreatePipe(out inputRead, out inputWrite, ref security, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe(input) failed.");
            if (!SetHandleInformation(inputWrite, HandleFlagInherit, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation(input) failed.");

            if (!CreatePipe(out outputRead, out outputWrite, ref security, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe(output) failed.");
            if (!SetHandleInformation(outputRead, HandleFlagInherit, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation(output) failed.");

            var hr = CreatePseudoConsole(new Coord(cols, rows), inputRead, outputWrite, 0, out pseudoConsole);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            CloseHandle(inputRead);
            inputRead = IntPtr.Zero;
            CloseHandle(outputWrite);
            outputWrite = IntPtr.Zero;

            IntPtr attributeListSize = IntPtr.Zero;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
            if (attributeListSize == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to size process attribute list.");

            attributeList = Marshal.AllocHGlobal(attributeListSize);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");

            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");
            }

            var startup = new StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            startup.lpAttributeList = attributeList;

            var commandLine = new StringBuilder();
            commandLine.Append('"').Append(executable).Append('"');
            if (!string.IsNullOrWhiteSpace(arguments)) commandLine.Append(' ').Append(arguments);

            if (!CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startup,
                    out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcess failed for {executable}.");
            }

            processHandle = processInfo.hProcess;
            threadHandle = processInfo.hThread;
            var processId = unchecked((int)processInfo.dwProcessId);

            CloseHandle(threadHandle);
            threadHandle = IntPtr.Zero;

            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = IntPtr.Zero;

            var inputSafe = new SafeFileHandle(inputWrite, ownsHandle: true);
            inputWrite = IntPtr.Zero;
            var outputSafe = new SafeFileHandle(outputRead, ownsHandle: true);
            outputRead = IntPtr.Zero;

            // CreatePipe returns synchronous handles. Marking these FileStreams async/overlapped
            // can fail after the child has already started, leaving the child orphaned.
            var inputStream = new FileStream(inputSafe, FileAccess.Write, 4096, isAsync: false);
            var outputStream = new FileStream(outputSafe, FileAccess.Read, 32768, isAsync: false);

            return new ConPtySession(pseudoConsole, processHandle, processId, inputStream, outputStream);
        }
        catch
        {
            if (threadHandle != IntPtr.Zero) CloseHandle(threadHandle);
            if (processHandle != IntPtr.Zero)
            {
                _ = TerminateProcess(processHandle, 1);
                _ = WaitForSingleObject(processHandle, 2000);
                CloseHandle(processHandle);
            }
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (pseudoConsole != IntPtr.Zero) ClosePseudoConsole(pseudoConsole);
            if (inputRead != IntPtr.Zero) CloseHandle(inputRead);
            if (inputWrite != IntPtr.Zero) CloseHandle(inputWrite);
            if (outputRead != IntPtr.Zero) CloseHandle(outputRead);
            if (outputWrite != IntPtr.Zero) CloseHandle(outputWrite);
            throw;
        }
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _input.WriteAsync(data, cancellationToken);
        await _input.FlushAsync(cancellationToken);
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed) return;
        cols = Math.Clamp(cols, 20, short.MaxValue);
        rows = Math.Clamp(rows, 5, short.MaxValue);
        lock (_resizeGate)
        {
            var hr = ResizePseudoConsole(_pseudoConsole, new Coord((short)cols, (short)rows));
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }
    }

    public Task WaitForExitAsync() => Task.Run(() => WaitForSingleObject(_processHandle, Infinite));

    public async Task TerminateTreeAsync()
    {
        if (_disposed) return;

        try
        {
            await WriteAsync(new byte[] { 0x03 });
        }
        catch
        {
        }

        if (WaitForSingleObject(_processHandle, 1500) == WaitTimeout)
        {
            try
            {
                using var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {ProcessId} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (killer is not null) await killer.WaitForExitAsync();
            }
            catch
            {
            }
        }

        if (WaitForSingleObject(_processHandle, 5000) == WaitTimeout)
        {
            _ = TerminateProcess(_processHandle, 1);
            _ = WaitForSingleObject(_processHandle, 2000);
        }
    }

    public bool HasExited => _disposed || WaitForSingleObject(_processHandle, 0) == WaitObject0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Closing the pseudo console first closes its pipe endpoints. That lets any
        // synchronous output read return EOF instead of deadlocking FileStream.Dispose().
        if (_pseudoConsole != IntPtr.Zero) ClosePseudoConsole(_pseudoConsole);
        _input.Dispose();
        _output.Dispose();
        if (_processHandle != IntPtr.Zero) CloseHandle(_processHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
        public Coord(short x, short y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
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
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SecurityAttributes lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

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
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
