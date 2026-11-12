namespace BiliFavSyncSharp;

internal sealed class SettingsDialog : Form {
	private readonly TextBox Uid = new();
	private readonly TextBox Session = new() { UseSystemPasswordChar = true };
	private readonly TextBox Root = new();

	private readonly CheckBox SystemProxy = new() { Text = "使用系统代理（登录、同步和下载）", AutoSize = true };
	private readonly CheckBox Startup = new() { Text = "登录 Windows 后自动启动", AutoSize = true };
	private readonly CheckBox Elevated = new() { Text = "以管理员权限启动（下次启动生效）", AutoSize = true };
	private readonly CheckBox Tray = new() { Text = "关闭窗口后在托盘继续运行", AutoSize = true };
	private readonly CheckBox Reminder = new() { Text = "显示主窗口使用提醒", AutoSize = true };
	private readonly CheckBox Remember = new() { Text = "记住登录状态（当前 Windows 用户加密）", AutoSize = true };
	private bool CredentialEdited;
	private bool RememberChosen;
	private bool RepairRequested;
	private bool Saving;

	public LoginSession? Account { get; private set; }
	public AppSettings Settings { get; private set; }
	public string Credential => Session.Text.Trim();

	public SettingsDialog(AppSettings settings, string session, LoginSession? account = null) {
		Settings = settings;
		Account = account;
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
		var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Padding = new Padding(22) };
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Controls.Add(grid);
		AddField(grid, 0, "用户 UID", Uid);
		AddField(grid, 1, "SESSDATA", Session);
		AddField(grid, 2, "视频库目录", Root);
		grid.Controls.Add(UiTheme.Button("选择目录", (_, _) => {
			using var picker = new FolderBrowserDialog { SelectedPath = Root.Text };
			if (picker.ShowDialog(this) == DialogResult.OK)
				Root.Text = picker.SelectedPath;
		}), 2, 2);
		SystemProxy.Checked = settings.UseSystemProxy;
		Startup.Checked = settings.StartAtLogon;
		Elevated.Checked = settings.RunAsAdministrator;
		Tray.Checked = settings.StayInTray;
		Reminder.Checked = settings.ShowReminder;
		Remember.Checked = settings.ProtectedSession.Length > 0 || settings.ProtectedLogin.Length > 0;
		Remember.Click += (_, _) => RememberChosen = true;
		Session.TextChanged += (_, _) => {
			CredentialEdited = true;
			Account = null;
		};
		grid.Controls.Add(UiTheme.Button("扫码登录", (_, _) => {
			using var dialog = new QrLoginDialog(SystemProxy.Checked);
			if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Account is not LoginSession login)
				return;
			bool use_login_uid = Uid.Text.Trim().Length == 0 || Uid.Text.Trim() == Account?.Uid.ToString();
			CredentialEdited = true;
			Session.Text = login.Session;
			Account = login;
			if (use_login_uid)
				Uid.Text = login.Uid.ToString();
		}), 2, 1);
		var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(12) };
		options.Controls.AddRange([Remember, SystemProxy, Startup, Elevated, Tray, Reminder]);
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
		grid.Controls.Add(options, 0, 3);
		grid.SetColumnSpan(options, 3);
		grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
		var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
		Button save = UiTheme.Button("保存", async (_, _) => await SaveAsync(), true);
		Button cancel = UiTheme.Button("取消", (_, _) => DialogResult = DialogResult.Cancel);
		actions.Controls.AddRange([save, cancel]);
		grid.Controls.Add(actions, 0, 5);
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
		if (CredentialEdited && Credential.Length > 0 && !Remember.Checked && !RememberChosen) {
			DialogResult choice = MessageBox.Show(this, "是否在这台电脑上记住登录？“是”会使用当前 Windows 用户加密保存，重启或覆盖安装后继续使用。“否”仅在本次运行使用。", "保存登录状态", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
			if (choice == DialogResult.Cancel)
				return;
			Remember.Checked = choice == DialogResult.Yes;
			RememberChosen = true;
		}
		bool preserve_login = !CredentialEdited && Remember.Checked && (Settings.ProtectedLogin.Length > 0 || Settings.ProtectedSession.Length > 0);
		bool update_task = RepairRequested || Startup.Checked != Settings.StartAtLogon || (Startup.Checked && Elevated.Checked != Settings.RunAsAdministrator);
		bool task_updated = false;
		Saving = true;
		Enabled = false;
		try {
			var updated = new AppSettings {
				StorageRoot = Root.Text.Trim(),
				UserId = uid,
				StartAtLogon = Startup.Checked,
				RunAsAdministrator = Elevated.Checked,
				StayInTray = Tray.Checked,
				ShowReminder = Reminder.Checked,
				AutomaticSync = Settings.AutomaticSync,
				DetailedLogging = Settings.DetailedLogging,
				UseSystemProxy = SystemProxy.Checked,
				ProtectedSession = preserve_login ? Settings.ProtectedSession : Remember.Checked && Account is null ? SessionProtection.Protect(Credential) : "",
				ProtectedLogin = preserve_login ? Settings.ProtectedLogin : Remember.Checked && Account is not null ? SessionProtection.ProtectLogin(Account) : ""
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
