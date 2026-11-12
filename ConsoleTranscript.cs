using System.Globalization;

namespace BiliFavSyncSharp;

internal sealed record ConsoleFrame(string Text, int Percent);

internal static class ConsoleTranscript {
	public static async Task ReadAsync(TextReader reader, Action<string> output, Action<ConsoleFrame?>? preview) {
		ConsoleFrame? last = null;
		while (await reader.ReadLineAsync() is string line) {
			if (line.StartsWith("BFS-PROGRESS\t", StringComparison.Ordinal)) {
				string[] fields = line.Split('\t');
				if (fields.Length == 4 && int.TryParse(fields[1], CultureInfo.InvariantCulture, out int percent) && percent is >= 0 and <= 100
					&& long.TryParse(fields[2], CultureInfo.InvariantCulture, out long speed) && speed >= 0 && fields[3] is "0" or "1") {
					last = new ConsoleFrame($"{percent}% · {FormatSpeed(speed)}", percent);
					preview?.Invoke(last);
					if (fields[3] == "1") {
						output($"[BBDown] 下载进度 · {last.Text}");
						last = null;
						preview?.Invoke(null);
					}
					continue;
				}
			}
			output($"[BBDown] {line}");
		}
		if (last is not null) {
			output($"[BBDown] 下载进度 · {last.Text}");
			preview?.Invoke(null);
		}
	}

	private static string FormatSpeed(long bytes) {
		return bytes switch {
			>= 1048576 => $"{bytes / 1048576d:F1} MiB/s",
			>= 1024 => $"{bytes / 1024d:F0} KiB/s",
			_ => $"{bytes} B/s"
		};
	}
}
