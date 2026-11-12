using System.Diagnostics;
using System.Text;
using BiliFavSyncSharp;

namespace ConsoleChecks;

internal static class Program {
	private static async Task<int> Main(string[] args) {
		const string ARGUMENT = "带 空格\"和尾斜杠\\";
		if (args.Length > 1 && args[0] == "child") {
			Console.OutputEncoding = Encoding.UTF8;
			if (!Console.IsOutputRedirected || !Console.IsErrorRedirected)
				return 96;
			if (args[2] != ARGUMENT)
				return 98;
			Console.WriteLine("标准输出已启动");
			Console.Error.WriteLine("标准错误示例");
			Console.Error.WriteLine("BFS-PROGRESS\t10\t1048576\t0");
			while (!File.Exists(args[1]))
				await Task.Delay(20);
			for (int frame = 0; frame < 800; frame++) {
				Console.Error.WriteLine("BFS-PROGRESS\t99\t2097152\t0");
			}
			Console.Error.WriteLine("BFS-PROGRESS\t99\t2097152\t1");
			Console.Write("tail-without-newline");
			return 7;
		}
		string root = Directory.CreateDirectory(args[0]).FullName;
		string signal = Path.Combine(root, $"{Guid.NewGuid():N}.signal");
		var info = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = root };
		foreach (string argument in new[] { "child", signal, ARGUMENT })
			info.ArgumentList.Add(argument);
		var seen = new List<string>();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		int code = await DownloadConsole.RunAsync(info, seen.Add, timeout.Token, frame => {
			if (frame?.Percent == 10 && frame.Text.Contains("1.0 MiB/s"))
				File.WriteAllText(signal, "ok");
		});
		if (seen.Count(line => line.Contains("下载进度")) != 1 || seen.Count > 6)
			throw new InvalidOperationException($"进度重绘污染日志队列，共 {seen.Count} 条。");
		if (code != 7 || new[] { "标准输出已启动", "标准错误示例", "99%", "tail-without-newline" }.Any(item => !seen.Any(line => line.Contains(item))))
			throw new InvalidOperationException($"输出或退出码不符：{code} / {string.Join(" | ", seen)}");
		File.Delete(signal);
		using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		bool live_cancel = false;
		try {
			await DownloadConsole.RunAsync(info, _ => { }, cancel.Token, frame => {
				if (frame?.Percent == 10) {
					live_cancel = true;
					cancel.Cancel();
				}
			});
			throw new InvalidOperationException("未取消子进程。");
		}
		catch (OperationCanceledException) when (live_cancel) { }
		var displayed = new List<string>();
		string path = Path.Combine(root, "capture.log");
		using (var log = new RunLog(path, displayed.Add, () => true)) {
			log.Protect("fixture%2Fsecret");
			log.Detail("[BBDown] fixture/secret bili_jct=anothersecret; diagnostic");
		}
		if (!File.ReadAllLines(path).SequenceEqual(displayed) || displayed.Single().Contains("secret") || !displayed.Single().Contains("diagnostic"))
			throw new InvalidOperationException("下载日志脱敏失败。");
		if (args.Length > 1) {
			var help = new ProcessStartInfo(args[1]) { WorkingDirectory = root };
			help.ArgumentList.Add("--download-worker");
			var lines = new List<string>();
			using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			int status = await DownloadConsole.RunAsync(help, lines.Add, limit.Token, input: """{"Bv":"fixture","Session":"","Pool":"","Previous":0,"Total":1,"UseSystemProxy":false}""");
			if (status != 1 || !lines.Any(line => line.Contains("BV 号格式错误")))
				throw new InvalidOperationException($"内置工作进程入口验证失败（退出码 {status}）。");
		}
		Console.WriteLine("PASS: worker pipes, 800 in-place frames, error lines, EOF tail, exit code and cancellation.");
		return 0;
	}
}
