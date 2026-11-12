using System.Text;

namespace BiliFavSyncSharp;

internal sealed class ConsoleTranscript(Action<string> output) {
	private enum Escape { Text, Start, Control, Title, TitleEnd }
	private readonly StringBuilder Line = new();
	private readonly StringBuilder Parameters = new();
	private Escape State;
	private int Cursor;
	private int Row = 1;
	private string? LastShown;
	private bool Rewound;

	public static async Task ReadAsync(Stream stream, Action<string> output) {
		var terminal = new ConsoleTranscript(output);
		using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
		char[] buffer = new char[4096];
		while (true) {
			Task<int> pending = reader.ReadAsync(buffer, 0, buffer.Length);
			if (!pending.IsCompleted) {
				try { await pending.WaitAsync(TimeSpan.FromMilliseconds(200)); }
				catch (TimeoutException) { terminal.Show(); }
			}
			int count = await pending;
			if (count == 0)
				break;
			for (int index = 0; index < count; index++)
				terminal.Accept(buffer[index]);
		}
		terminal.Show();
	}

	private void Show() {
		string text = Line.ToString().TrimEnd();
		if (text != LastShown && text.Length > 0) {
			output($"[BBDown] {text}");
			LastShown = text;
		}
	}

	private void EndLine() {
		Show();
		Line.Clear();
		Cursor = 0;
		LastShown = null;
		Rewound = false;
	}

	private void Move(int position) {
		Rewound |= position < Cursor;
		Cursor = Math.Clamp(position, 0, 32767);
	}

	private void Control(char command) {
		string[] parts = Parameters.ToString().Split(';');
		int first = int.TryParse(parts[0], out int number) ? number : 0;
		switch (command) {
		case 'G':
			Move(Math.Max(1, first) - 1);
			break;
		case 'C':
			Move(Cursor + Math.Max(first, 1));
			break;
		case 'D':
			Move(Cursor - Math.Max(first, 1));
			break;
		case 'H':
		case 'f':
			int row = Math.Max(first, 1);
			if (row != Row) {
				EndLine();
				Row = row;
			}
			Move(parts.Length > 1 && int.TryParse(parts[1], out int column) ? column - 1 : 0);
			break;
		case 'K':
			if (first == 2)
				Line.Clear();
			else if (first == 0 && Cursor < Line.Length)
				Line.Length = Cursor;
			break;
		}
	}

	private void Accept(char ch) {
		switch (State) {
		case Escape.Start:
			State = ch switch { '[' => Escape.Control, ']' => Escape.Title, _ => Escape.Text };
			Parameters.Clear();
			return;
		case Escape.Control:
			if (ch is >= '@' and <= '~') {
				Control(ch);
				State = Escape.Text;
			}
			else if (Parameters.Length < 64)
				Parameters.Append(ch);
			return;
		case Escape.Title:
			if (ch == '\a')
				State = Escape.Text;
			else if (ch == '\x1b')
				State = Escape.TitleEnd;
			return;
		case Escape.TitleEnd:
			State = ch == '\\' ? Escape.Text : Escape.Title;
			return;
		}
		switch (ch) {
		case '\x1b':
			State = Escape.Start;
			return;
		case '\n':
			EndLine();
			Row++;
			return;
		case '\r':
			Move(0);
			return;
		case '\b':
			Move(Cursor - 1);
			return;
		case '\t':
			Move((Cursor / 8 + 1) * 8);
			return;
		}
		if (char.IsControl(ch))
			return;
		if (Rewound) {
			Show();
			Rewound = false;
		}
		if (Cursor > Line.Length)
			Line.Append(' ', Cursor - Line.Length);
		if (Cursor == Line.Length)
			Line.Append(ch);
		else
			Line[Cursor] = ch;
		Cursor++;
	}
}
