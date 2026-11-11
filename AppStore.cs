using System.Text.Json;
using System.Text.Json.Serialization;

namespace BiliFavSyncSharp;

internal static class AppStore {
	public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiliFavSyncSharp");
	public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

	public static AppSettings LoadSettings() {
		Directory.CreateDirectory(DataDirectory);
		if (File.Exists(SettingsPath))
			return Read<AppSettings>(SettingsPath);
		string[] previous = [
			Path.Combine(AppContext.BaseDirectory, "settings.json"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BiliFavSyncSharp", "settings.json")
		];
		foreach (string path in previous) {
			if (File.Exists(path)) {
				AppSettings settings = Read<AppSettings>(path);
				Save(SettingsPath, settings);
				return settings;
			}
		}
		return new AppSettings();
	}
	private static readonly JsonSerializerOptions Options = new() {
		WriteIndented = true,
		RespectNullableAnnotations = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString
	};

	public static T Read<T>(string path) where T : class {
		using FileStream stream = File.OpenRead(path);
		return JsonSerializer.Deserialize<T>(stream, Options) ?? throw new InvalidDataException($"文件为空：{path}");
	}

	public static LibraryIndex LoadIndex(string root) {
		string path = Path.Combine(root, "data.json");
		return File.Exists(path) ? Read<LibraryIndex>(path) : new LibraryIndex { Config = new(), Videos = [] };
	}

	public static void Save<T>(string path, T value) {
		string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
		try {
			using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
				JsonSerializer.Serialize(stream, value, Options);
				stream.Flush(true);
			}
			File.Move(temporary, path, true);
		}
		finally {
			File.Delete(temporary);
		}
	}
}
