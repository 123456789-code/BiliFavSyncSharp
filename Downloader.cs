using System.Diagnostics;

namespace BiliFavSyncSharp;

internal static class Downloader {
	public static async Task RunAsync(string session, string pool, string bv, int previous, int total, CancellationToken token) {
		LibraryFiles.CheckBvid(bv);
		string tools = Path.Combine(AppContext.BaseDirectory, "tools");
		string binary = Path.Combine(tools, "BBDown.exe");
		string ffmpeg = Path.Combine(tools, "ffmpeg.exe");
		if (!File.Exists(binary) || !File.Exists(ffmpeg))
			throw new FileNotFoundException("内置下载组件缺失，请重新安装程序。");
		var info = new ProcessStartInfo(binary) {
			WorkingDirectory = pool,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		info.ArgumentList.Add(bv);
		if (previous > 0) {
			info.ArgumentList.Add("-p");
			info.ArgumentList.Add(previous + 1 == total ? $"{total}" : $"{previous + 1}-{total}");
		}
		string[] options = ["-c", $"SESSDATA={session}", "--ffmpeg-path", ffmpeg, "--work-dir", pool, "--skip-subtitle", "--skip-ai", "true", "-mt", "true", "--force-http", "true", "--show-all", "-F", "<bvid>/P1/<videoTitle>", "-M", "<bvid>/P<pageNumberWithZero>/<pageTitle>"];
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
