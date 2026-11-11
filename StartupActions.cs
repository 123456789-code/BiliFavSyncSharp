using System.Diagnostics;
using System.Security.Principal;

namespace BiliFavSyncSharp;

internal static class StartupActions {
	public static string CurrentSid {
		get {
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			return identity.User!.Value;
		}
	}

	public static Process Elevate(IEnumerable<string> arguments) {
		var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
		foreach (string arg in arguments)
			info.ArgumentList.Add(arg);
		return Process.Start(info) ?? throw new IOException("无法启动权限设置程序。");
	}

	public static async Task ConfigureAsync(bool enabled, bool elevated) {
		if (StartupRegistration.IsAdministrator()) {
			StartupRegistration.Apply(enabled, elevated);
			return;
		}
		if (!enabled || !elevated) {
			try {
				StartupRegistration.Apply(enabled, elevated);
				return;
			}
			catch (Exception exception) when (exception.HResult == unchecked((int)0x80070005)) { }
		}
		using Process child = Elevate(["--configure-startup", enabled.ToString(), elevated.ToString(), CurrentSid]);
		await child.WaitForExitAsync();
		if (child.ExitCode != 0)
			throw new IOException("自启任务设置失败。请检查任务状态，并使用同一 Windows 账户授权。");
	}
}
