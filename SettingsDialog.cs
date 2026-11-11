namespace BiliFavSyncSharp;

internal sealed class SettingsDialog : Form {
	private readonly TextBox Uid = new();
	private readonly TextBox Session = new() { UseSystemPasswordChar = true };
	private readonly TextBox Root = new();
	private readonly TextBox Binary = new();

	private readonly CheckBox Startup = new() { Text = "登录 Windows 后自动启动", AutoSize = true };
	private readonly CheckBox Elevated = new() { Text = "以管理员权限启动（下次启动生效）", AutoSize = true };
	private readonly CheckBox Tray = new() { Text = "关闭窗口后在托盘继续运行", AutoSize = true };
	private readonly CheckBox Reminder = new() { Text = "显示主窗口使用提醒", AutoSize = true };
	private readonly CheckBox Remember = new() { Text = "在本机记住 SESSDATA（当前 Windows 用户加密）", AutoSize = true };
	private bool RepairRequested;
	private bool Saving;

	public AppSettings Settings { get; private set; }
	public string Credential => Session.Text.Trim();

	public SettingsDialog(AppSettings settings, string session) {
		Settings = settings;
		UiTheme.Apply(this);
		Text = "程序设置";
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(760, 610);
		MinimumSize = new Size(620, 550);
		MaximizeBox = false;
		Uid.Text = settings.UserId > 0 ? settings.UserId.ToString() : "";
		Session.Text = session;
		Session.PlaceholderText = "离线重建可留空";
		Root.Text = settings.StorageRoot;
		Binary.Text = settings.Downloader;
		var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7, Padding = new Padding(22) };
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Controls.Add(grid);
		AddField(grid, 0, "用户 UID", Uid);
		AddField(grid, 1, "SESSDATA", Session);
		AddField(grid, 2, "视频库目录", Root);
		AddField(grid, 3, "BBDown 路径", Binary);
		grid.Controls.Add(UiTheme.Button("选择目录", (_, _) => {
			using var picker = new FolderBrowserDialog { SelectedPath = Root.Text };
			if (picker.ShowDialog(this) == DialogResult.OK)
				Root.Text = picker.SelectedPath;
		}), 2, 2);
		grid.Controls.Add(UiTheme.Button("选择文件", (_, _) => {
			using var picker = new OpenFileDialog { Filter = "BBDown 程序|BBDown.exe|可执行文件|*.exe" };
			if (picker.ShowDialog(this) == DialogResult.OK)
				Binary.Text = picker.FileName;
		}), 2, 3);
		Startup.Checked = settings.StartAtLogon;
		Elevated.Checked = settings.RunAsAdministrator;
		Tray.Checked = settings.StayInTray;
		Reminder.Checked = settings.ShowReminder;
		Remember.Checked = settings.ProtectedSession.Length > 0;
		var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(12) };
		options.Controls.AddRange([Remember, Startup, Elevated, Tray, Reminder]);
		var maintenance = new FlowLayoutPanel { AutoSize = true };
		maintenance.Controls.Add(UiTheme.Button("核对自启状态", (_, _) => {
			try { MessageBox.Show(this, StartupRegistration.Describe(), "Windows 自启状态"); }
			catch (Exception exception) { MessageBox.Show(this, exception.Message, "核对失败"); }
		}));
		Button repair = UiTheme.Button("同步自启任务", (sender, _) => {
			RepairRequested = true;
			((Button)sender!).Text = "保存时同步";
		});
		maintenance.Controls.Add(repair);
		options.Controls.Add(maintenance);
		grid.Controls.Add(options, 0, 4);
		grid.SetColumnSpan(options, 3);
		grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
		var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
		Button save = UiTheme.Button("保存", async (_, _) => await SaveAsync(), true);
		Button cancel = UiTheme.Button("取消", (_, _) => DialogResult = DialogResult.Cancel);
		actions.Controls.AddRange([save, cancel]);
		grid.Controls.Add(actions, 0, 6);
		grid.SetColumnSpan(actions, 3);
		AcceptButton = save;
		CancelButton = cancel;
		FormClosing += (_, args) => args.Cancel = Saving;
	}

	private static void AddField(TableLayoutPanel grid, int row, string caption, TextBox input) {
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
		grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
		input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
		grid.Controls.Add(input, 1, row);
	}

	private async Task SaveAsync() {
		if (Root.Text.Trim().Length > 0 && !Path.IsPathFullyQualified(Root.Text.Trim())) {
			MessageBox.Show(this, "请选择视频库的绝对路径。");
			return;
		}
		long uid = 0;
		if (Uid.Text.Length > 0 && (!long.TryParse(Uid.Text.Trim(), out uid) || uid <= 0)) {
			MessageBox.Show(this, "UID 应为正整数。");
			return;
		}
		if (Credential.Any(static character => char.IsControl(character) || character == ';')) {
			MessageBox.Show(this, "请只填写 SESSDATA 的值。");
			return;
		}
		bool update_task = RepairRequested || Startup.Checked != Settings.StartAtLogon || (Startup.Checked && Elevated.Checked != Settings.RunAsAdministrator);
		bool task_updated = false;
		Saving = true;
		Enabled = false;
		try {
			var updated = new AppSettings {
				StorageRoot = Root.Text.Trim(),
				Downloader = Binary.Text.Trim(),
				UserId = uid,
				StartAtLogon = Startup.Checked,
				RunAsAdministrator = Elevated.Checked,
				StayInTray = Tray.Checked,
				ShowReminder = Reminder.Checked,
				AutomaticSync = Settings.AutomaticSync,
				ProtectedSession = Remember.Checked ? SessionProtection.Protect(Credential) : ""
			};
			if (update_task) {
				await StartupActions.ConfigureAsync(updated.StartAtLogon, updated.RunAsAdministrator);
				task_updated = true;
			}
			AppStore.Save(AppStore.SettingsPath, updated);
			Settings = updated;
			Saving = false;
			DialogResult = DialogResult.OK;
		}
		catch (Exception exception) {
			string message = exception.Message;
			if (task_updated) {
				try { await StartupActions.ConfigureAsync(Settings.StartAtLogon, Settings.RunAsAdministrator); }
				catch (Exception restore) { message += $"\r\n自启任务未能还原，请核对自启状态：{restore.Message}"; }
			}
			MessageBox.Show(this, message, "设置保存失败");
		}
		finally {
			Saving = false;
			Enabled = true;
		}
	}
}
