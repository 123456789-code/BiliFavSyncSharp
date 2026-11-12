using System.Diagnostics;

namespace BiliFavSyncSharp;

internal static class Downloader {
	public static async Task RunAsync(string session, string pool, string bv, int previous, int total, CancellationToken token, Action<string> verbose) {
		LibraryFiles.CheckBvid(bv);
		string tools = Path.Combine(AppContext.BaseDirectory, "tools");
		string binary = Path.Combine(tools, "BBDown.exe");
		string ffmpeg = Path.Combine(tools, "ffmpeg.exe");
		if (!File.Exists(binary) || !File.Exists(ffmpeg))
			throw new FileNotFoundException("内置下载组件缺失，请重新安装程序。");
		var info = new ProcessStartInfo(binary) { WorkingDirectory = pool };
		info.ArgumentList.Add(bv);
		if (previous > 0) {
			info.ArgumentList.Add("-p");
			info.ArgumentList.Add(previous + 1 == total ? $"{total}" : $"{previous + 1}-{total}");
		}
		string[] options = ["-c", $"SESSDATA={session}", "--ffmpeg-path", ffmpeg, "--work-dir", pool, "--skip-subtitle", "--skip-ai", "true", "-mt", "true", "--force-http", "true", "--show-all", "-F", "<bvid>/P1/<videoTitle>", "-M", "<bvid>/P<pageNumberWithZero>/<pageTitle>"];
		foreach (string option in options)
			info.ArgumentList.Add(option);
		token.ThrowIfCancellationRequested();
		var elapsed = Stopwatch.StartNew();
		verbose($"启动 BBDown · {bv} · P{previous + 1}–P{total}");
		int code = await DownloadConsole.RunAsync(info, verbose, token);
		verbose($"BBDown 已结束 · {bv} · 退出码 {code} · 用时 {elapsed.Elapsed.TotalSeconds:F1} 秒");
		if (code != 0)
			throw new IOException($"BBDown 下载 {bv} 失败，退出码 {code}。");
	}
}
