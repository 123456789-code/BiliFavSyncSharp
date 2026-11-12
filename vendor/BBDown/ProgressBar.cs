using System;
using System.Diagnostics;

namespace BBDown;

internal sealed class ProgressBar : IDisposable {
	private readonly object Gate = new();
	private long LastTick = Stopwatch.GetTimestamp();
	private long LastBytes;
	private int Percent;
	private long Speed;
	private bool Started;

	public void Report(double fraction, long bytes = 0) {
		lock (Gate) {
			Percent = double.IsFinite(fraction) ? (int)(Math.Clamp(fraction, 0, 1) * 100) : 0;
			long now = Stopwatch.GetTimestamp();
			double seconds = Stopwatch.GetElapsedTime(LastTick, now).TotalSeconds;
			if (Started && seconds < 0.25)
				return;
			Speed = Started ? (long)(Math.Max(0, bytes - LastBytes) / seconds) : 0;
			LastTick = now;
			LastBytes = bytes;
			Started = true;
			BundledEngine.Progress?.Invoke(Percent, Speed, false);
		}
	}

	public void Dispose() {
		lock (Gate) {
			if (Started)
				BundledEngine.Progress?.Invoke(Percent, Speed, true);
		}
	}
}
