using System.Diagnostics;
using System.Text;

namespace BiliFavSyncSharp;

internal static class DownloadConsole {
	public static async Task<int> RunAsync(ProcessStartInfo info, Action<string> output, CancellationToken token, Action<ConsoleFrame?>? preview = null, string input = "") {
		token.ThrowIfCancellationRequested();
		info.UseShellExecute = false;
		info.CreateNoWindow = true;
		info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
		info.StandardInputEncoding = info.StandardOutputEncoding = info.StandardErrorEncoding = new UTF8Encoding(false);
		using Process child = Process.Start(info) ?? throw new IOException("无法启动下载工作进程。");
		var gate = new object();
		void Log(string text) {
			lock (gate)
				output(text);
		}
		Task stdout = ConsoleTranscript.ReadAsync(child.StandardOutput, Log, null);
		Task stderr = ConsoleTranscript.ReadAsync(child.StandardError, Log, preview);
		try {
			await child.StandardInput.WriteAsync(input.AsMemory(), token);
			child.StandardInput.Close();
			await child.WaitForExitAsync(token);
		}
		catch {
			try {
				if (!child.HasExited)
					child.Kill(true);
			}
			catch (InvalidOperationException) when (child.HasExited) { }
			await child.WaitForExitAsync();
			throw;
		}
		finally { await Task.WhenAll(stdout, stderr); }
		return child.ExitCode;
	}
}
