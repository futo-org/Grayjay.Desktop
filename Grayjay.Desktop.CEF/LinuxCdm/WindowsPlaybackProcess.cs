using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Grayjay.Desktop.CEF.LinuxCdm;

// Configure memory and ownership before the helper executes any instructions.
// Only its three standard pipe handles are inherited; no launcher is required.
internal sealed class WindowsPlaybackProcess : IDisposable
{
    internal Process Process { get; private set; } = null!;
    internal StreamWriter Input { get; private set; } = null!;
    internal StreamReader Output { get; private set; } = null!;
    internal StreamReader Error { get; private set; } = null!;
    private PlaybackProcessJob? job;
    private int disposed;

    internal static WindowsPlaybackProcess Start(string executable, string directory, IEnumerable<string> arguments,
        ulong memoryLimitBytes = 0)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var child = new WindowsPlaybackProcess();
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null, errorRead = null, errorWrite = null;
        IntPtr attributes = IntPtr.Zero, handles = IntPtr.Zero;
        bool initializedAttributes = false;
        var information = new ProcessInformation();
        try
        {
            var security = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
            Check(CreatePipe(out inputRead, out inputWrite, ref security, 0));
            Check(CreatePipe(out outputRead, out outputWrite, ref security, 0));
            Check(CreatePipe(out errorRead, out errorWrite, ref security, 0));
            foreach (var parent in new[] { inputWrite, outputRead, errorRead }) Check(SetHandleInformation(parent, 1, 0));
            UIntPtr size = UIntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(checked((int)size.ToUInt64()));
            Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
            initializedAttributes = true;
            handles = Marshal.AllocHGlobal(3 * IntPtr.Size);
            Marshal.WriteIntPtr(handles, 0, inputRead.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size, outputWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, 2 * IntPtr.Size, errorWrite.DangerousGetHandle());
            Check(UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x20002, handles, (UIntPtr)(3 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero));
            var startup = new StartupInformationEx
            {
                Startup = new StartupInformation
                {
                    Size = Marshal.SizeOf<StartupInformationEx>(), Flags = 0x100,
                    Input = inputRead.DangerousGetHandle(), Output = outputWrite.DangerousGetHandle(), Error = errorWrite.DangerousGetHandle()
                },
                Attributes = attributes
            };
            var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
            Check(CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                0x4 | 0x08000000 | 0x00080000 | 0x4000, IntPtr.Zero, directory, ref startup, out information));
            child.Process = Process.GetProcessById(information.Id);
            child.job = new PlaybackProcessJob(child.Process, memoryLimitBytes);
            child.Input = new StreamWriter(new FileStream(inputWrite, FileAccess.Write), new UTF8Encoding(false), 64 * 1024) { NewLine = "\n" };
            inputWrite = null;
            child.Output = new StreamReader(new FileStream(outputRead, FileAccess.Read), new UTF8Encoding(false), false, 64 * 1024);
            outputRead = null;
            child.Error = new StreamReader(new FileStream(errorRead, FileAccess.Read), new UTF8Encoding(false), false, 4096);
            errorRead = null;
            if (ResumeThread(information.Thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            return child;
        }
        catch
        {
            if (information.Process != IntPtr.Zero) TerminateProcess(information.Process, 125);
            child.Dispose();
            throw;
        }
        finally
        {
            foreach (var pipe in new[] { inputRead, inputWrite, outputRead, outputWrite, errorRead, errorWrite }) pipe?.Dispose();
            if (information.Thread != IntPtr.Zero) CloseHandle(information.Thread);
            if (information.Process != IntPtr.Zero) CloseHandle(information.Process);
            if (initializedAttributes) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
        }
    }

    internal static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            result.Append(ch); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        job?.Dispose();
        foreach (var resource in new IDisposable?[] { Input, Output, Error, Process })
            try { resource?.Dispose(); }
            catch (IOException) { }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Size; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public int Id, ThreadId; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInformation
    {
        public int Size; public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public short Show, ReservedSize; public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInformationEx { public StartupInformation Startup; public IntPtr Attributes; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, int mask, int flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, UIntPtr attribute, IntPtr value, UIntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInformationEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
