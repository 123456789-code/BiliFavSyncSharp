namespace BiliFavSyncSharp;

internal static class UiTheme {
	public static readonly Color Ink = Color.FromArgb(32, 37, 43);
	public static readonly Color Blue = Color.FromArgb(15, 108, 189);
	public static readonly Color Canvas = Color.FromArgb(243, 245, 248);
	public static readonly Color Muted = Color.FromArgb(99, 111, 125);
	public static readonly Color Border = Color.FromArgb(221, 227, 234);

	public static void Apply(Form window) {
		window.AutoScaleDimensions = new SizeF(96, 96);
		window.AutoScaleMode = AutoScaleMode.Dpi;
		window.Font = new Font("Microsoft YaHei UI", 10);
		window.BackColor = Canvas;
		window.ForeColor = Ink;
	}

	public static Button Button(string caption, EventHandler action, bool primary = false) {
		var button = new Button {
			Text = caption,
			AutoSize = true,
			MinimumSize = new Size(104, 40),
			Padding = new Padding(12, 5, 12, 5),
			Margin = new Padding(0, 0, 8, 0),
			BackColor = primary ? Blue : Color.White,
			ForeColor = primary ? Color.White : Ink,
			FlatStyle = FlatStyle.Flat,
			Cursor = Cursors.Hand
		};
		button.FlatAppearance.BorderColor = primary ? Blue : Border;
		button.Click += action;
		return button;
	}
}
