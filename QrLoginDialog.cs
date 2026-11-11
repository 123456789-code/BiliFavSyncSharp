using QRCoder;

namespace BiliFavSyncSharp;

internal sealed class QrLoginDialog : Form {
	private readonly PictureBox Picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
	private readonly Label Status = new() { AutoSize = true, Padding = new Padding(8) };
	private readonly Button Retry;
	private CancellationTokenSource? Request;
	public LoginSession? Account { get; private set; }

	public QrLoginDialog() {
		UiTheme.Apply(this);
		Text = "哔哩哔哩扫码登录";
		ClientSize = new Size(360, 430);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = MinimizeBox = false;
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.Controls.Add(Picture, 0, 0);
		layout.Controls.Add(Status, 0, 1);
		var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
		Retry = UiTheme.Button("刷新二维码", async (_, _) => await ScanAsync());
		Button cancel = UiTheme.Button("取消", (_, _) => Close());
		CancelButton = cancel;
		actions.Controls.AddRange([Retry, cancel]);
		layout.Controls.Add(actions, 0, 2);
		Controls.Add(layout);
		Shown += async (_, _) => await ScanAsync();
		FormClosing += (_, _) => Request?.Cancel();
		FormClosed += (_, _) => Picture.Image?.Dispose();
	}

	private async Task ScanAsync() {
		if (Request is not null)
			return;
		using var cancellation = new CancellationTokenSource();
		Request = cancellation;
		Retry.Enabled = false;
		var previous = Picture.Image;
		Picture.Image = null;
		previous?.Dispose();
		Status.Text = "正在获取二维码…";
		try {
			using var client = new PassportClient();
			LoginCode code = await client.GenerateAsync(cancellation.Token);
			cancellation.Token.ThrowIfCancellationRequested();
			using var qr = QRCodeGenerator.GenerateQrCode(code.Url, QRCodeGenerator.ECCLevel.M);
			using var png = new PngByteQRCode(qr);
			using var stream = new MemoryStream(png.GetGraphic(6));
			using Image bitmap = Image.FromStream(stream);
			Picture.Image = new Bitmap(bitmap);
			Status.Text = "请打开哔哩哔哩手机 App 扫码";
			DateTime expires = DateTime.UtcNow.AddMinutes(3);
			while (DateTime.UtcNow < expires) {
				await Task.Delay(2000, cancellation.Token);
				LoginPoll poll = await client.PollAsync(code.Key, cancellation.Token);
				cancellation.Token.ThrowIfCancellationRequested();
				switch (poll.Code) {
				case 0:
					Account = poll.Account;
					DialogResult = DialogResult.OK;
					return;
				case 86101:
					continue;
				case 86090:
					Status.Text = "已扫码，请在手机上确认登录";
					continue;
				case 86038:
					Status.Text = "二维码已过期，请点击刷新";
					return;
				default:
					throw new InvalidDataException($"扫码状态异常（{poll.Code}）。");
				}
			}
			Status.Text = "二维码已过期，请点击刷新";
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
		catch (Exception exception) {
			if (!IsDisposed)
				Status.Text = exception is InvalidDataException ? exception.Message : "无法连接登录服务，请重试";
		}
		finally {
			Request = null;
			if (!IsDisposed)
				Retry.Enabled = true;
		}
	}
}
