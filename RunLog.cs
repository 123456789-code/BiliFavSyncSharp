namespace BiliFavSyncSharp;

internal sealed class RunLog : IDisposable {
	private readonly StreamWriter Writer;
	private readonly Action<string> Display;
	private readonly Func<bool> Detailed;
	private readonly HashSet<string> Secrets = new(StringComparer.Ordinal);
	private bool WriteFailed;

	public RunLog(string path, Action<string> display, Func<bool> detailed) {
		Writer = new StreamWriter(path, false) { AutoFlush = true };
		Display = display;
		Detailed = detailed;
	}

	public void Protect(params string?[] values) {
		foreach (string? value in values) {
			if (!string.IsNullOrEmpty(value))
				Secrets.Add(value);
		}
	}

	public void Detail(string message) {
		if (Detailed())
			Write($"[详细] {message}");
	}

	public void Write(string message) {
		foreach (string secret in Secrets)
			message = message.Replace(secret, "[已隐藏]", StringComparison.Ordinal);
		string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message.ReplaceLineEndings(" ")}";
		if (!WriteFailed) {
			try { Writer.WriteLine(line); }
			catch (IOException) {
				WriteFailed = true;
				Display("日志文件写入失败，本轮剩余消息仅在窗口显示。");
			}
		}
		Display(line);
	}

	public void Dispose() {
		try { Writer.Dispose(); }
		catch (IOException) { Display("日志文件关闭时写入失败。"); }
	}
}
