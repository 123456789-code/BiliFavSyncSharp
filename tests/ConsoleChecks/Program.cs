using System.Diagnostics;
using System.Text;
using BiliFavSyncSharp;

namespace ConsoleChecks;

internal static class Program {
	private static async Task<int> Main(string[] args) {
		const string ARGUMENT = "带 空格\"和尾斜杠\\";
		if (args.Length > 1 && args[0] == "child") {
			Console.OutputEncoding = Encoding.UTF8;
			if (Console.IsOutputRedirected || Console.IsErrorRedirected)
				return 96;
			if (args[2] != ARGUMENT)
				return 98;
			Console.WriteLine("标准输出已启动");
			Console.Error.WriteLine("标准错误示例");
			Console.Write("进度 10%");
			while (!File.Exists(args[1]))
				await Task.Delay(20);
			Console.Write("\r进度 99%\n");
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
		int code = await DownloadConsole.RunAsync(info, line => {
			seen.Add(line);
			if (line.Contains("10%"))
				File.WriteAllText(signal, "ok");
		}, timeout.Token);
		if (code != 7 || new[] { "标准输出已启动", "标准错误示例", "10%", "99%", "tail-without-newline" }.Any(item => !seen.Any(line => line.Contains(item))))
			throw new InvalidOperationException($"输出或退出码不符：{code} / {string.Join(" | ", seen)}");
		File.Delete(signal);
		using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		bool live_cancel = false;
		try {
			await DownloadConsole.RunAsync(info, line => {
				if (line.Contains("10%")) {
					live_cancel = true;
					cancel.Cancel();
				}
			}, cancel.Token);
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
			help.ArgumentList.Add("--help");
			var lines = new List<string>();
			using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			int status = await DownloadConsole.RunAsync(help, lines.Add, limit.Token);
			if (status != 0 || !lines.Any(line => line.Contains("--work-dir")))
				throw new InvalidOperationException($"未捕获内置 BBDown 帮助输出（退出码 {status}）。");
		}
		Console.WriteLine("PASS: real console, quoting, stdout/stderr, live progress, EOF tail, exit code and cancellation.");
		return 0;
	}
}
