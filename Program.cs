using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BiliFavSyncSharp;

internal static class Program {
	internal static readonly uint ShowMessage = RegisterWindowMessageW("BiliFavSyncSharp.ShowWindow.v1");

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern uint RegisterWindowMessageW(string name);

	[DllImport("user32.dll")]
	private static extern bool PostMessageW(IntPtr window, uint message, IntPtr parameter, IntPtr data);

	[DllImport("user32.dll")]
	internal static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filter);

	[STAThread]
	private static int Main(string[] args) {
		if (args is ["--download-worker"])
			return Downloader.WorkerAsync().GetAwaiter().GetResult();
		if (args is ["--download-route", "system" or "direct"]) {
			BBDown.BundledEngine.ConfigureNetwork(args[1] == "system");
			var endpoint = new Uri("https://api.bilibili.com/");
			var proxy = System.Net.Http.HttpClient.DefaultProxy;
			Console.WriteLine(proxy.IsBypassed(endpoint) || proxy.GetProxy(endpoint) == endpoint ? "DIRECT" : "PROXY");
			return 0;
		}
		ApplicationConfiguration.Initialize();
		try {
			if (args is ["--version"])
				return 0;
			string sid = StartupActions.CurrentSid;
			string name = $"Local\\BiliFavSyncSharp-{sid}";
			if (args is ["--configure-startup", string enabled, string elevated, string account]) {
				if (sid != account || !StartupRegistration.IsAdministrator())
					throw new IOException("请使用当前 Windows 账户授权。");
				StartupRegistration.Apply(bool.Parse(enabled), bool.Parse(elevated));
				return 0;
			}
			if (args is ["--check-install"] or ["--check-uninstall"]) {
				if (Mutex.TryOpenExisting(name, out Mutex? active)) {
					active.Dispose();
					throw new IOException("请从托盘或主窗口退出 B站收藏夹同步后重试。");
				}
				if (args[0] == "--check-uninstall" && StartupRegistration.IsRegisteredForCurrentExecutable())
					throw new IOException("请先在程序设置中关闭登录自启，再退出程序并卸载。");
				return 0;
			}
			int restart = Array.IndexOf(args, "--elevated-user");
			if (restart >= 0 && (restart + 1 >= args.Length || args[restart + 1] != sid || !StartupRegistration.IsAdministrator()))
				throw new IOException("请使用同一 Windows 账户授权启动。");
			bool tray = args.Contains("--tray");
			if (Mutex.TryOpenExisting(name, out Mutex? existing)) {
				existing.Dispose();
				if (!tray)
					PostMessageW(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero);
				return 0;
			}
			AppSettings settings = AppStore.LoadSettings();
			if (settings.RunAsAdministrator && !StartupRegistration.IsAdministrator()) {
				try {
					using var child = StartupActions.Elevate([.. args, "--elevated-user", sid]);
					return 0;
				}
				catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) {
					if (MessageBox.Show("已取消提权。使用普通权限打开设置？", "启动权限", MessageBoxButtons.YesNo) != DialogResult.Yes)
						return 0;
				}
			}
			using var instance = new Mutex(true, name, out bool created);
			if (!created) {
				if (!tray)
					PostMessageW(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero);
				return 0;
			}
			using var installation_guard = new Mutex(false, @"Global\BiliFavSyncSharp.Running");
			using var form = new MainForm(settings, tray);
			Application.Run(form);
			return 0;
		}
		catch (Exception exception) {
			MessageBox.Show(exception.Message, "B站收藏夹同步", MessageBoxButtons.OK, MessageBoxIcon.Error);
			return 1;
		}
	}
}
