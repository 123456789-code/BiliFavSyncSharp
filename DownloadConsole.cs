using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BiliFavSyncSharp;

internal static class DownloadConsole {
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct StartupInfo {
		public int Size;
		public IntPtr Reserved, Desktop, Title;
		public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
		public short Show, ReservedSize;
		public IntPtr ReservedData, Input, Output, Error, Attributes;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessInfo {
		public IntPtr Process, Thread;
		public int ProcessId, ThreadId;
	}

	[DllImport("kernel32.dll")]
	private static extern int CreatePseudoConsole(int size, SafePipeHandle input, SafePipeHandle output, uint flags, out IntPtr console);

	[DllImport("kernel32.dll")]
	private static extern void ClosePseudoConsole(IntPtr console);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool UpdateProcThreadAttribute(IntPtr list, int flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);

	[DllImport("kernel32.dll")]
	private static extern void DeleteProcThreadAttributeList(IntPtr list);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool CreateProcessW(string executable, StringBuilder command, IntPtr process_security, IntPtr thread_security, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetExitCodeProcess(SafeWaitHandle process, out int code);

	// Native CreateProcess requires a single Windows command line. Quote every argument,
	// including trailing backslashes before the closing quote.
	private static string Quote(string argument) {
		var result = new StringBuilder("\"");
		int slashes = 0;
		foreach (char ch in argument) {
			if (ch == '\\') {
				slashes++;
				continue;
			}
			result.Append('\\', ch == '"' ? 2 * slashes + 1 : slashes).Append(ch);
			slashes = 0;
		}
		return result.Append('\\', 2 * slashes).Append('"').ToString();
	}

	private static ProcessInfo Start(ProcessStartInfo info, IntPtr console) {
		nuint size = 0;
		_ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
		IntPtr list = Marshal.AllocHGlobal(checked((int)size));
		try {
			if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
				throw new Win32Exception(Marshal.GetLastWin32Error());
			try {
				if (!UpdateProcThreadAttribute(list, 0, 0x00020016, console, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
					throw new Win32Exception(Marshal.GetLastWin32Error());
				// STARTF_USESTDHANDLES with null handles lets conhost initialize this child's console handles even when our own streams are redirected.
				var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x100, Attributes = list };
				var command = new StringBuilder(string.Join(' ', info.ArgumentList.Prepend(info.FileName).Select(Quote)));
				if (!CreateProcessW(info.FileName, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, info.WorkingDirectory, ref startup, out ProcessInfo process))
					throw new Win32Exception(Marshal.GetLastWin32Error());
				return process;
			}
			finally { DeleteProcThreadAttributeList(list); }
		}
		finally { Marshal.FreeHGlobal(list); }
	}

	public static async Task<int> RunAsync(ProcessStartInfo info, Action<string> output, CancellationToken token) {
		token.ThrowIfCancellationRequested();
		using var input_pipe = new AnonymousPipeServerStream(PipeDirection.Out);
		using var output_pipe = new AnonymousPipeServerStream(PipeDirection.In);
		// COORD contains two 16-bit fields, width then height.
		int result = CreatePseudoConsole((60 << 16) | 1024, input_pipe.ClientSafePipeHandle, output_pipe.ClientSafePipeHandle, 0, out IntPtr console);
		Marshal.ThrowExceptionForHR(result);
		input_pipe.DisposeLocalCopyOfClientHandle();
		output_pipe.DisposeLocalCopyOfClientHandle();
		Task drain = Task.Run(() => ConsoleTranscript.ReadAsync(output_pipe, output));
		try {
			ProcessInfo child = Start(info, console);
			using var thread = new SafeWaitHandle(child.Thread, true);
			using var finished = new EventWaitHandle(false, EventResetMode.AutoReset);
			finished.SafeWaitHandle = new SafeWaitHandle(child.Process, true);
			Task wait = Task.Run(() => finished.WaitOne());
			try {
				Task first = await Task.WhenAny(wait, drain).WaitAsync(token);
				await first;
				await wait.WaitAsync(token);
			}
			catch {
				if (!finished.WaitOne(0)) {
					try {
						using Process process = Process.GetProcessById(child.ProcessId);
						process.Kill(true);
					}
					catch (ArgumentException) { }
					catch (InvalidOperationException) { }
				}
				await wait;
				throw;
			}
			if (!GetExitCodeProcess(finished.SafeWaitHandle, out int code))
				throw new Win32Exception(Marshal.GetLastWin32Error());
			return code;
		}
		finally {
			// Drain concurrently while conhost closes, including its final UTF-8 output.
			if (drain.IsFaulted)
				output_pipe.Dispose();
			ClosePseudoConsole(console);
			await drain;
		}
	}
}
