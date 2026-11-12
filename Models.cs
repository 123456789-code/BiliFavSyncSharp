using System.Text.Json;
using System.Text.Json.Serialization;

namespace BiliFavSyncSharp;

internal sealed class AppSettings {
	public bool StartAtLogon { get; set; }
	public bool RunAsAdministrator { get; set; }
	public bool StayInTray { get; set; }
	public bool ShowReminder { get; set; } = true;
	public bool DetailedLogging { get; set; }
	public bool UseSystemProxy { get; set; } = true;
	public bool AutomaticSync { get; set; }
	public string ProtectedLogin { get; set; } = "";
	public string ProtectedSession { get; set; } = "";
	public long UserId { get; set; }
	public string StorageRoot { get; set; } = "";
}

internal sealed class LibraryIndex {
	[JsonPropertyName("sync_config")]
	public SyncConfig Config { get; init; } = new();

	[JsonPropertyName("library")]
	public Dictionary<string, LibraryVideo> Videos { get; init; } = [];

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; init; }
}

internal sealed class SyncConfig {
	[JsonPropertyName("Ignore")]
	public List<long> Ignore { get; init; } = [];

	[JsonPropertyName("last_sync")]
	public string LastSync { get; set; } = "";

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; init; }
}

internal sealed class LibraryVideo {
	[JsonPropertyName("title")]
	public required string Title { get; init; }

	[JsonPropertyName("author")]
	public string Author { get; init; } = "";

	[JsonPropertyName("authorid")]
	public long AuthorId { get; init; }

	[JsonPropertyName("pub_date")]
	public string Published { get; init; } = "";

	[JsonPropertyName("parts")]
	public int Parts { get; set; }

	[JsonPropertyName("folder")]
	public required List<string> Folders { get; init; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; init; }
}

internal sealed record VideoDetail(LibraryVideo Video, string Description);
internal sealed record FavoriteFolder(long Id, string Name);
