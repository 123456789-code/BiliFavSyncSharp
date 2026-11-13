using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BBDown;

namespace BiliFavSyncSharp;

internal sealed record DownloadRequest(string Bv, string Session, string Pool, int Previous, int Total, bool UseSystemProxy, string ProxyEndpoint = "");

internal static class Downloader {
	// The worker gets credentials over its private standard input pipe.
	public static async Task<int> WorkerAsync() {
		Console.OutputEncoding = new UTF8Encoding(false);
		Console.InputEncoding = new UTF8Encoding(false);
		try {
			DownloadRequest request = JsonSerializer.Deserialize<DownloadRequest>(await Console.In.ReadToEndAsync()) ?? throw new IOException("下载参数缺失。");
			LibraryFiles.CheckBvid(request.Bv);
			BundledEngine.ConfigureNetwork(request.UseSystemProxy, ProxyAddress.Resolve(request.UseSystemProxy, request.ProxyEndpoint));
			string ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
			if (!File.Exists(ffmpeg))
				throw new FileNotFoundException("内置 FFmpeg 缺失，请重新安装。");
			string pages = request.Previous == 0 ? "" : request.Previous + 1 == request.Total ? $"{request.Total}" : $"{request.Previous + 1}-{request.Total}";
			await BundledEngine.DownloadAsync(request.Bv, request.Session, request.Pool, ffmpeg, pages,
				static (percent, speed, final) => Console.Error.WriteLine(FormattableString.Invariant($"BFS-PROGRESS\t{percent}\t{speed}\t{(final ? 1 : 0)}")));
			return 0;
		}
		catch (Exception exception) {
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
	}

	public static async Task RunAsync(string session, string pool, string bv, int previous, int total, CancellationToken token, Action<string> verbose, bool use_system_proxy, string proxy_endpoint, Action<ConsoleFrame?>? preview) {
		LibraryFiles.CheckBvid(bv);
		var info = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = pool };
		info.ArgumentList.Add("--download-worker");
		var request = new DownloadRequest(bv, session, pool, previous, total, use_system_proxy, proxy_endpoint);
		var elapsed = Stopwatch.StartNew();
		verbose($"启动内置 BBDown · {bv} · P{previous + 1}–P{total}");
		int code = await DownloadConsole.RunAsync(info, verbose, token, preview, JsonSerializer.Serialize(request));
		verbose($"BBDown 已结束 · {bv} · 退出码 {code} · 用时 {elapsed.Elapsed.TotalSeconds:F1} 秒");
		if (code != 0)
			throw new IOException($"BBDown 下载 {bv} 失败，退出码 {code}。");
	}
}
