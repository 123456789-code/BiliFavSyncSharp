using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace BiliFavSyncSharp;

internal sealed class BiliClient : IDisposable {
	private readonly HttpClient Client;

	private readonly Action<string> Verbose;

	public BiliClient(long uid, string session, Action<string> verbose, bool use_system_proxy) {
		Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = use_system_proxy }) {
			BaseAddress = new Uri("https://api.bilibili.com/"),
			Timeout = TimeSpan.FromSeconds(15)
		};
		Verbose = verbose;
		Client.DefaultRequestHeaders.Add("Cookie", $"SESSDATA={session}");
		Client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) BiliFavSyncSharp/1.0");
		Client.DefaultRequestHeaders.Referrer = new Uri($"https://space.bilibili.com/{uid}/favlist");
	}

	private async Task<JsonElement> FetchAsync(string endpoint, CancellationToken token) {
		string route = endpoint.Split('?')[0];
		Verbose($"请求 B站接口 · {route}");
		long start = System.Diagnostics.Stopwatch.GetTimestamp();
		using HttpResponseMessage http = await Client.GetAsync(endpoint, token);
		Verbose($"{route} · HTTP {(int)http.StatusCode} · {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms");
		http.EnsureSuccessStatusCode();
		JsonElement response = await http.Content.ReadFromJsonAsync<JsonElement>(token);
		if (response.GetProperty("code").GetInt32() != 0)
			throw new InvalidDataException($"B站 API {route} · 错误码 {response.GetProperty("code")}");
		return response.GetProperty("data");
	}

	public async Task CheckLoginAsync(CancellationToken token) {
		JsonElement data = await FetchAsync("x/web-interface/nav", token);
		if (!data.GetProperty("isLogin").GetBoolean())
			throw new InvalidDataException("登录已过期，请在程序设置中更新 SESSDATA。");
	}

	public async Task<List<FavoriteFolder>> FoldersAsync(long uid, CancellationToken token) {
		JsonElement data = await FetchAsync($"x/v3/fav/folder/created/list-all?up_mid={uid}", token);
		if (data.ValueKind == JsonValueKind.Null || data.GetProperty("list").ValueKind == JsonValueKind.Null)
			return [];
		return [.. data.GetProperty("list").EnumerateArray().Select(static item => new FavoriteFolder(item.GetProperty("id").GetInt64(), item.GetProperty("title").GetString()!))];
	}

	public async Task<List<string>> VideosAsync(long id, CancellationToken token) {
		JsonElement data = await FetchAsync($"x/v3/fav/resource/ids?media_id={id}&platform=web", token);
		JsonElement items = data.ValueKind == JsonValueKind.Object ? data.GetProperty("medias") : data;
		if (items.ValueKind == JsonValueKind.Null)
			return [];
		return [.. items.EnumerateArray().Where(static item => item.TryGetProperty("bvid", out JsonElement bv) && bv.ValueKind == JsonValueKind.String).Select(static item => item.GetProperty("bvid").GetString()!).Where(static bv => bv.Length > 0).Distinct()];
	}

	public async Task<VideoDetail> DetailAsync(string bv, CancellationToken token) {
		JsonElement data = await FetchAsync($"x/web-interface/view?bvid={Uri.EscapeDataString(bv)}", token);
		JsonElement owner = data.GetProperty("owner");
		return new VideoDetail(new LibraryVideo {
			Title = data.GetProperty("title").GetString()!,
			Author = owner.GetProperty("name").GetString()!,
			AuthorId = owner.GetProperty("mid").GetInt64(),
			Published = DateTimeOffset.FromUnixTimeSeconds(data.GetProperty("pubdate").GetInt64()).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
			Parts = data.GetProperty("videos").GetInt32(),
			Folders = []
		}, data.GetProperty("desc").GetString() ?? "");
	}

	public void Dispose() {
		Client.Dispose();
	}
}
