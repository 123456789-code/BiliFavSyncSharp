using System.Diagnostics;

namespace BiliFavSyncSharp;

internal static class Downloader {
	public static async Task RunAsync(string binary, string session, string pool, string bv, int previous, int total, CancellationToken token) {
		LibraryFiles.CheckBvid(bv);
		if (!File.Exists(binary))
			throw new FileNotFoundException("请在设置中选择 BBDown.exe。");
		var info = new ProcessStartInfo(binary) {
			WorkingDirectory = Path.GetDirectoryName(binary)!,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		info.ArgumentList.Add(bv);
		if (previous > 0) {
			info.ArgumentList.Add("-p");
			info.ArgumentList.Add(previous + 1 == total ? $"{total}" : $"{previous + 1}-{total}");
		}
		string[] options = ["-c", $"SESSDATA={session}", "--work-dir", pool, "--skip-subtitle", "--skip-ai", "true", "-mt", "true", "--force-http", "true", "--show-all", "-F", "<bvid>/P1/<videoTitle>", "-M", "<bvid>/P<pageNumberWithZero>/<pageTitle>"];
		foreach (string option in options)
			info.ArgumentList.Add(option);
		token.ThrowIfCancellationRequested();
		using Process process = Process.Start(info) ?? throw new IOException("无法启动 BBDown。");
		try {
			await process.WaitForExitAsync(token);
		}
		catch (OperationCanceledException) {
			try {
				if (!process.HasExited)
					process.Kill(true);
			}
			catch (InvalidOperationException) when (process.HasExited) { }
			await process.WaitForExitAsync();
			throw;
		}
		if (process.ExitCode != 0)
			throw new IOException($"BBDown 下载 {bv} 失败，退出码 {process.ExitCode}。");
	}
}
