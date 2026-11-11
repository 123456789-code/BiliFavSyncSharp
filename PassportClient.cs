using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BiliFavSyncSharp;

internal sealed record LoginSession(string Session, string Csrf, long Uid, string RefreshToken, string PendingConfirmation = "", DateTime CheckedUtc = default);
internal sealed record LoginCode(string Url, string Key);
internal sealed record LoginPoll(int Code, LoginSession? Account);

internal sealed class PassportClient : IDisposable {
	private const string PASSPORT = "https://passport.bilibili.com/x/passport-login/web/";
	private const string PUBLIC_KEY = "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDLgd2OAkcGVtoE3ThUREbio0EgUc/prcajMKXvkCKFCWhJYJcLkcM2DKKcSeFpD/j6Boy538YXnR6VhcuUJOhH2x71nzPjfdTcqMz7djHum0qSZA0AyCBDABUqCrfNgCiJ00Ra7GmRj+YCK1NJEuewlb40JNrRuoEUXpabUzGB8QIDAQAB";
	private readonly HttpClient Client;

	public PassportClient(HttpMessageHandler? handler = null) {
		Client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
		Client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 BiliFavSyncSharp/1.2");
		Client.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
	}

	private async Task<HttpResponseMessage> SendAsync(string url, LoginSession? account, Dictionary<string, string>? form, CancellationToken token) {
		using var request = new HttpRequestMessage(form is null ? HttpMethod.Get : HttpMethod.Post, url);
		if (account is not null)
			request.Headers.Add("Cookie", $"SESSDATA={account.Session}; bili_jct={account.Csrf}; DedeUserID={account.Uid}");
		if (form is not null)
			request.Content = new FormUrlEncodedContent(form);
		HttpResponseMessage response = await Client.SendAsync(request, token);
		if (response.IsSuccessStatusCode)
			return response;
		using (response)
			throw new HttpRequestException($"B站登录服务返回 HTTP {(int)response.StatusCode}。");
	}

	private static async Task<JsonElement> DataAsync(HttpResponseMessage response, CancellationToken token) {
		JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(token);
		return Data(result);
	}

	private static JsonElement Data(JsonElement result) {
		int code = result.GetProperty("code").GetInt32();
		if (code != 0)
			throw new InvalidDataException(code is -101 or -111 or 86095 ? $"登录凭据已失效或不匹配（{code}），请在设置中重新扫码。" : $"B站登录接口返回 {code}。");
		return result.TryGetProperty("data", out JsonElement data) ? data : default;
	}

	public async Task<LoginCode> GenerateAsync(CancellationToken token) {
		using HttpResponseMessage response = await SendAsync($"{PASSPORT}qrcode/generate", null, null, token);
		JsonElement data = await DataAsync(response, token);
		string url = data.GetProperty("url").GetString()!;
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || uri.Host is not ("passport.bilibili.com" or "account.bilibili.com"))
			throw new InvalidDataException("登录二维码地址无效。");
		return new LoginCode(url, Required(data, "qrcode_key"));
	}

	private static string Required(JsonElement data, string name) {
		string? value = data.GetProperty(name).GetString();
		return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("B站返回的登录信息不完整。");
	}

	private static LoginSession ReadAccount(HttpResponseMessage response, JsonElement data) {
		var origin = new Uri(PASSPORT);
		var jar = new CookieContainer();
		if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? headers)) {
			foreach (string header in headers)
				jar.SetCookies(origin, header);
		}
		CookieCollection cookies = jar.GetCookies(origin);
		string? session = cookies["SESSDATA"]?.Value;
		string? csrf = cookies["bili_jct"]?.Value;
		if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(csrf) || !long.TryParse(cookies["DedeUserID"]?.Value, out long uid) || uid <= 0)
			throw new InvalidDataException("登录响应缺少完整 Cookie，请重试扫码。");
		return new LoginSession(session, csrf, uid, Required(data, "refresh_token"));
	}

	public async Task<LoginPoll> PollAsync(string key, CancellationToken token) {
		using HttpResponseMessage response = await SendAsync($"{PASSPORT}qrcode/poll?qrcode_key={Uri.EscapeDataString(key)}", null, null, token);
		JsonElement data = await DataAsync(response, token);
		int code = data.GetProperty("code").GetInt32();
		return new LoginPoll(code, code == 0 ? ReadAccount(response, data) : null);
	}

	public async Task MaintainAsync(LoginSession account, Action<LoginSession> save, CancellationToken token) {
		if (account.PendingConfirmation.Length == 0) {
			if (account.CheckedUtc.Date == DateTime.UtcNow.Date)
				return;
			using HttpResponseMessage info = await SendAsync($"{PASSPORT}cookie/info", account, null, token);
			JsonElement data = await DataAsync(info, token);
			if (!data.GetProperty("refresh").GetBoolean()) {
				save(account with { CheckedUtc = DateTime.UtcNow });
				return;
			}
			using RSA rsa = RSA.Create();
			rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PUBLIC_KEY), out _);
			string timestamp = data.GetProperty("timestamp").GetInt64().ToString(CultureInfo.InvariantCulture);
			string path = Convert.ToHexStringLower(rsa.Encrypt(Encoding.UTF8.GetBytes($"refresh_{timestamp}"), RSAEncryptionPadding.OaepSHA256));
			using HttpResponseMessage page = await SendAsync($"https://www.bilibili.com/correspond/1/{path}", account, null, token);
			string html = await page.Content.ReadAsStringAsync(token);
			Match match = Regex.Match(html, """<div\s+id=["']1-name["']\s*>\s*([a-zA-Z0-9_-]+)\s*</div>""", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
			if (!match.Success)
				throw new InvalidDataException("无法取得 B站续期确认信息。");
			using HttpResponseMessage refresh = await SendAsync($"{PASSPORT}cookie/refresh", account, new() {
				["csrf"] = account.Csrf,
				["refresh_csrf"] = match.Groups[1].Value,
				["source"] = "main_web",
				["refresh_token"] = account.RefreshToken
			}, token);
			LoginSession next = ReadAccount(refresh, await DataAsync(refresh, token));
			if (next.Uid != account.Uid)
				throw new InvalidDataException("续期返回了不同的用户。");
			account = next with { PendingConfirmation = account.RefreshToken };
			save(account);
		}

		using HttpResponseMessage confirm = await SendAsync($"{PASSPORT}confirm/refresh", account, new() {
			["csrf"] = account.Csrf,
			["refresh_token"] = account.PendingConfirmation
		}, token);
		JsonElement reply = await confirm.Content.ReadFromJsonAsync<JsonElement>(token);
		if (reply.GetProperty("code").GetInt32() == -400) {
			// 确认可能已送达，而上一轮未能保存确认结果。先独立核对新会话。
			using HttpResponseMessage info = await SendAsync($"{PASSPORT}cookie/info", account, null, token);
			JsonElement status = await DataAsync(info, token);
			if (status.GetProperty("refresh").GetBoolean())
				throw new InvalidDataException("登录续期确认失败，请在设置中重新扫码。");
		}
		else {
			_ = Data(reply);
		}
		save(account with { PendingConfirmation = "", CheckedUtc = DateTime.UtcNow });
	}

	public void Dispose() {
		Client.Dispose();
	}
}
