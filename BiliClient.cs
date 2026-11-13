using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace BiliFavSyncSharp;

internal sealed class BiliClient : IDisposable {
	private readonly HttpClient Client;

	private readonly Action<string> Verbose;

	public BiliClient(long uid, string session, Action<string> verbose, bool use_system_proxy, string proxy_endpoint, HttpMessageHandler? handler = null) {
		Client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = use_system_proxy, Proxy = ProxyAddress.Resolve(use_system_proxy, proxy_endpoint) }) {
			BaseAddress = new Uri("https://api.bilibili.com/"),
			Timeout = TimeSpan.FromSeconds(30)
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
		for (int attempt = 1; attempt <= 2; attempt++) {
			try {
				JsonElement data = await FetchAsync("x/web-interface/nav", token);
				if (!data.GetProperty("isLogin").GetBoolean())
					throw new InvalidDataException("登录已过期，请在程序设置中重新登录。");
				return;
			}
			catch (OperationCanceledException) when (!token.IsCancellationRequested) {
				if (attempt == 2)
					throw new TimeoutException("B站登录校验连续两次超时，尚未开始下载。请核对程序设置中的代理协议、地址和端口。");
				Verbose("登录校验 30 秒超时，即将按原网络设置重试一次。");
				await Task.Delay(1000, token);
			}
		}
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
