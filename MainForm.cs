namespace BiliFavSyncSharp;

internal sealed class MainForm : Form {
	private AppSettings Settings;
	private readonly NotifyIcon TrayIcon = new();
	private readonly Label Reminder = new() { AutoSize = true, ForeColor = UiTheme.Muted, Text = "视频库使用硬链接。修改任意链接的文件内容会影响同一视频的其他链接。", Margin = new Padding(24, 0, 16, 12) };
	private bool Exiting;
	private bool Editing;
	private bool UpdatingSwitch;
	private string Session = "";
	private CancellationTokenSource? Active;
	private bool ClosePending;
	private readonly Button SyncButton;
	private readonly Button RebuildButton;
	private readonly Button StopButton;
	private readonly Button SettingsButton;
	private readonly CheckBox Hourly = new() { AutoSize = true, Text = "每小时同步", Anchor = AnchorStyles.Right };
	private readonly System.Windows.Forms.Timer Timer = new() { Interval = 3600000 };
	private readonly Label Summary = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = UiTheme.Muted };
	private readonly Label Status = new() { AutoSize = true, Text = "就绪", Margin = new Padding(24, 0, 0, 16) };
	private readonly DataGridView Grid = new();
	private readonly ListBox Log = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, BorderStyle = BorderStyle.None, Margin = new Padding(24, 8, 24, 12) };

	public MainForm(AppSettings? settings = null, bool start_hidden = false) {
		Settings = settings ?? new();
		try { Session = SessionProtection.Restore(Settings.ProtectedSession); }
		catch (Exception) { Status.Text = "登录凭据无法解密，请重新填写 SESSDATA。"; }
		UiTheme.Apply(this);
		Text = "BiliFavSyncSharp";
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(1080, 680);
		MinimumSize = new Size(720, 480);
		var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 7, Dock = DockStyle.Fill };
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		foreach (SizeType kind in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize })
			layout.RowStyles.Add(new RowStyle(kind, layout.RowStyles.Count == 3 ? 72 : 28));
		Controls.Add(layout);
		var header = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.White, Padding = new Padding(24, 18, 16, 18), Margin = Padding.Empty };
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		header.Controls.Add(new Label { Text = "B站收藏夹同步", AutoSize = true, Font = new Font(Font.FontFamily, 22, FontStyle.Bold) }, 0, 0);
		SettingsButton = UiTheme.Button("程序设置", (_, _) => EditSettings());
		header.Controls.Add(SettingsButton, 1, 0);
		layout.Controls.Add(header, 0, 0);
		var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(24, 14, 16, 8) };
		SyncButton = UiTheme.Button("立即同步", async (_, _) => await RunAsync(false), true);
		RebuildButton = UiTheme.Button("重建硬链接", async (_, _) => await RunAsync(true));
		StopButton = UiTheme.Button("取消任务", (_, _) => { Hourly.Checked = false; Active?.Cancel(); });
		StopButton.Enabled = false;
		toolbar.Controls.AddRange([SyncButton, RebuildButton, StopButton]);
		layout.Controls.Add(toolbar, 0, 1);
		var summary = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(24, 4, 24, 8) };
		summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		summary.Controls.Add(Summary, 0, 0);
		summary.Controls.Add(Hourly, 1, 0);
		layout.Controls.Add(summary, 0, 2);
		Grid.Dock = DockStyle.Fill;
		Grid.Margin = new Padding(24, 0, 24, 0);
		Grid.ReadOnly = true;
		Grid.AllowUserToAddRows = false;
		Grid.AllowUserToDeleteRows = false;
		Grid.RowHeadersVisible = false;
		Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
		Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		Grid.BackgroundColor = Color.White;
		Grid.BorderStyle = BorderStyle.None;
		Grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
		Grid.GridColor = UiTheme.Border;
		Grid.EnableHeadersVisualStyles = false;
		Grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 238, 244);
		Grid.ColumnHeadersHeight = 40;
		Grid.RowTemplate.Height = 38;
		Grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 254);
		Grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 239, 252);
		Grid.DefaultCellStyle.SelectionForeColor = UiTheme.Ink;
		foreach (string caption in new[] { "视频标题", "BV 号", "UP 主", "分 P", "收藏夹" })
			Grid.Columns.Add(caption, caption);
		Grid.Columns[0].FillWeight = 200;
		Grid.Columns[3].FillWeight = 42;
		layout.Controls.Add(Grid, 0, 3);
		layout.Controls.Add(Log, 0, 4);
		layout.Controls.Add(Status, 0, 5);
		layout.Controls.Add(Reminder, 0, 6);
		Reminder.Visible = Settings.ShowReminder;
		TrayIcon.Icon = SystemIcons.Application;
		TrayIcon.Text = "B站收藏夹同步";
		TrayIcon.ContextMenuStrip = new ContextMenuStrip();
		TrayIcon.ContextMenuStrip.Items.Add("显示主窗口", null, (_, _) => ShowWindow());
		TrayIcon.ContextMenuStrip.Items.Add("退出程序", null, (_, _) => { Exiting = true; Close(); });
		TrayIcon.DoubleClick += (_, _) => ShowWindow();
		TrayIcon.Visible = Settings.StayInTray;
		Hourly.Checked = Settings.AutomaticSync;
		Hourly.CheckedChanged += async (_, _) => {
			if (UpdatingSwitch)
				return;
			bool previous = Settings.AutomaticSync;
			Settings.AutomaticSync = Hourly.Checked;
			try { AppStore.Save(AppStore.SettingsPath, Settings); }
			catch (Exception exception) {
				UpdatingSwitch = true;
				Settings.AutomaticSync = previous;
				Hourly.Checked = previous;
				UpdatingSwitch = false;
				MessageBox.Show(this, exception.Message, "定时设置保存失败");
				return;
			}
			Timer.Stop();
			if (Hourly.Checked && Active is null)
				await RunAsync(false);
		};
		Timer.Tick += async (_, _) => {
			if (!Editing)
				await RunAsync(false);
		};
		Shown += async (_, _) => {
			if (start_hidden && Settings.StayInTray)
				Hide();
			if (!Hourly.Checked)
				return;
			if (Ready(false))
				await RunAsync(false);
			else
				Status.Text = "自动同步等待配置。请在程序设置中填写登录凭据和下载路径。";
		};
		FormClosing += (_, args) => {
			if (args.CloseReason == CloseReason.UserClosing && Settings.StayInTray && !Exiting) {
				args.Cancel = true;
				Hide();
				return;
			}
			Exiting = true;
			Timer.Stop();
			if (Active is null)
				return;
			args.Cancel = true;
			ClosePending = true;
			Active.Cancel();
		};
		FormClosed += (_, _) => {
			Timer.Dispose();
			TrayIcon.Visible = false;
			TrayIcon.ContextMenuStrip?.Dispose();
			TrayIcon.Dispose();
		};
		RefreshLibrary();
	}

	private void ShowWindow() {
		Show();
		WindowState = FormWindowState.Normal;
		Activate();
	}

	protected override void OnHandleCreated(EventArgs args) {
		base.OnHandleCreated(args);
		Program.ChangeWindowMessageFilterEx(Handle, Program.ShowMessage, 1, IntPtr.Zero);
	}

	protected override void WndProc(ref Message message) {
		if (message.Msg == Program.ShowMessage)
			ShowWindow();
		base.WndProc(ref message);
	}

	private bool EditSettings() {
		Editing = true;
		using var dialog = new SettingsDialog(Settings, Session);
		try {
			if (dialog.ShowDialog(this) != DialogResult.OK)
				return false;
		}
		finally { Editing = false; }
		Settings = dialog.Settings;
		Session = dialog.Credential;
		Reminder.Visible = Settings.ShowReminder;
		TrayIcon.Visible = Settings.StayInTray;
		RefreshLibrary();
		if (Hourly.Checked && Ready(false) && !Timer.Enabled && Active is null)
			Timer.Start();
		return true;
	}

	private void RefreshLibrary() {
		Grid.Rows.Clear();
		Summary.Text = "请选择视频库目录";
		if (Settings.StorageRoot.Length == 0)
			return;
		try {
			LibraryIndex index = AppStore.LoadIndex(Settings.StorageRoot);
			foreach ((string bv, LibraryVideo video) in index.Videos)
				Grid.Rows.Add(video.Title, bv, video.Author, video.Parts, string.Join("、", video.Folders));
			Summary.Text = $"视频 {index.Videos.Count} 部    上次同步  {(index.Config.LastSync.Length == 0 ? "尚无记录" : index.Config.LastSync)}";
		}
		catch (Exception exception) {
			Summary.Text = $"读取视频库失败 · {exception.Message}";
		}
	}

	private bool Ready(bool rebuild) {
		return Settings.StorageRoot.Length > 0 && (rebuild || (Settings.UserId > 0 && Session.Length > 0 && File.Exists(Settings.Downloader)));
	}

	private async Task RunAsync(bool rebuild) {
		if (Active is not null)
			return;
		Timer.Stop();
		if (!Ready(rebuild) && (!EditSettings() || !Ready(rebuild))) {
			Hourly.Checked = false;
			Status.Text = rebuild ? "请选择视频库目录" : "请在设置中填写 UID、SESSDATA、视频库和 BBDown 路径";
			return;
		}
		using var cancellation = new CancellationTokenSource();
		Active = cancellation;
		SettingsButton.Enabled = SyncButton.Enabled = RebuildButton.Enabled = false;
		StopButton.Enabled = true;
		Status.Text = rebuild ? "正在重建硬链接…" : "正在同步收藏夹…";
		Log.Items.Clear();
		IProgress<string> progress = new Progress<string>(line => {
			if (IsDisposed)
				return;
			if (Log.Items.Count >= 500)
				Log.Items.RemoveAt(0);
			Log.Items.Add(line);
			Log.TopIndex = Log.Items.Count - 1;
		});
		try {
			await Task.Run(async () => {
				using var writer = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "latest.log"), false) { AutoFlush = true };
				void Report(string message) {
					string line = $"{DateTime.Now:HH:mm:ss}  {message}";
					writer.WriteLine(line);
					progress.Report(line);
				}
				var engine = new SyncEngine(Settings, Session, Report);
				try {
					await engine.RunAsync(rebuild, cancellation.Token);
					Report("任务完成");
				}
				catch (Exception exception) {
					Report(exception.Message);
					throw;
				}
			});
			Status.Text = "任务完成";
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
			Status.Text = "任务已取消";
		}
		catch (Exception exception) {
			Status.Text = $"任务失败 · {exception.Message}";
		}
		finally {
			Active = null;
			SettingsButton.Enabled = SyncButton.Enabled = RebuildButton.Enabled = true;
			StopButton.Enabled = false;
			RefreshLibrary();
			if (ClosePending)
				Close();
			else if (Hourly.Checked) {
				Timer.Start();
				Status.Text += $"    下次同步 {DateTime.Now.AddHours(1):HH:mm}";
			}
		}
	}
}
