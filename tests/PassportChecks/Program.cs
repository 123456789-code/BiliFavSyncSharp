using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BiliFavSyncSharp;

namespace PassportChecks;

internal static class Program {
	private sealed class Script : HttpMessageHandler {
		private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> Steps = new();
		public int Remaining => Steps.Count;

		public void Timeout() {
			Steps.Enqueue(_ => throw new TaskCanceledException("synthetic timeout"));
		}

		public void Expect(string prefix, string body, string? session = null, string? form = null, bool issue_cookies = false, bool include_uid = true) {
			Steps.Enqueue(request => {
				Check(request.RequestUri!.IsAbsoluteUri && request.RequestUri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal));
				Check(request.Method == (form is null ? HttpMethod.Get : HttpMethod.Post));
				string cookies = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? header) ? header.Single() : "";
				Check(session is null ? cookies.Length == 0 : cookies.Split(';', StringSplitOptions.TrimEntries).Contains($"SESSDATA={session}"));
				if (form is not null)
					Check(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains(form, StringComparison.Ordinal));
				var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
				if (issue_cookies) {
					IEnumerable<string> issued = include_uid
						? ["SESSDATA=next", "bili_jct=csrf2", "DedeUserID=123"]
						: ["SESSDATA=next", "bili_jct=csrf2"];
					foreach (string cookie in issued)
						response.Headers.TryAddWithoutValidation("Set-Cookie", $"{cookie}; Path=/; Domain=.bilibili.com; Secure");
				}
				return response;
			});
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation_token) {
			cancellation_token.ThrowIfCancellationRequested();
			return Task.FromResult(Steps.Dequeue()(request));
		}
	}

	private static void Check(bool ok) {
		if (!ok)
			throw new InvalidOperationException("认证流程与预期不符。");
	}

	private static async Task CheckGzipDecompressionAsync() {
		using var handler = PassportClient.CreateHandler(false, "");
		Check(handler.AutomaticDecompression == DecompressionMethods.All);
		using var server = new TcpListener(IPAddress.Loopback, 0);
		server.Start();
		int port = ((IPEndPoint)server.LocalEndpoint).Port;
		byte[] compressed;
		using (var buffer = new MemoryStream()) {
			using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
				gzip.Write("""<html><body><div id="1-name">gzip-challenge</div></body></html>"""u8);
			compressed = buffer.ToArray();
		}
		Task serve = Task.Run(async () => {
			using TcpClient socket = await server.AcceptTcpClientAsync();
			using NetworkStream stream = socket.GetStream();
			using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
			while (await reader.ReadLineAsync() is { Length: > 0 }) { }
			byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Encoding: gzip\r\nContent-Length: {compressed.Length}\r\nConnection: close\r\n\r\n");
			await stream.WriteAsync(headers);
			await stream.WriteAsync(compressed);
		});
		using var client = new HttpClient(handler, disposeHandler: false);
		string html = await client.GetStringAsync($"http://127.0.0.1:{port}/correspond/1/test");
		await serve;
		Check(html.Contains("gzip-challenge", StringComparison.Ordinal));
	}

	private static async Task Main() {
		await CheckGzipDecompressionAsync();
		using var nav = new Script();
		using var videos = new BiliClient(123, "fixture", _ => { }, true, "http://127.0.0.1:8888", nav);
		nav.Timeout();
		nav.Expect("https://api.bilibili.com/x/web-interface/nav", """{"code":0,"data":{"isLogin":true}}""", "fixture");
		await videos.CheckLoginAsync(default);
		Check(nav.Remaining == 0);
		nav.Timeout();
		nav.Timeout();
		try {
			await videos.CheckLoginAsync(default);
			throw new InvalidOperationException("连续超时未中止");
		}
		catch (TimeoutException) { Check(nav.Remaining == 0); }
		Check(ProxyAddress.Resolve(true, "socks5://127.0.0.1:1080")?.Address?.Scheme == "socks5");
		Check(ProxyAddress.Resolve(true, "") is null);
		try {
			ProxyAddress.Resolve(true, "http://user:password@localhost:80");
			throw new InvalidOperationException("接受了带凭据的地址");
		}
		catch (ArgumentException) { }
		const string API = "https://passport.bilibili.com/x/passport-login/web/";
		using var script = new Script();
		using var client = new PassportClient(script);
		script.Expect($"{API}qrcode/generate", """{"code":0,"data":{"url":"https://account.bilibili.com/h5-app/scan","qrcode_key":"fixture"}}""");
		LoginCode qr = await client.GenerateAsync(default);
		foreach (int state in new[] { 86101, 86090, 86038 }) {
			script.Expect($"{API}qrcode/poll?qrcode_key=fixture", System.Text.Json.JsonSerializer.Serialize(new { code = 0, data = new { code = state } }));
			LoginPoll poll = await client.PollAsync(qr.Key, default);
			Check(poll.Code == state && poll.Account is null);
		}
		try {
			await client.PollAsync(qr.Key, new CancellationToken(true));
			throw new InvalidOperationException("取消失败。");
		}
		catch (OperationCanceledException) { }
		script.Expect($"{API}qrcode/poll", """{"code":0,"data":{"code":0,"refresh_token":"qr-token"}}""", issue_cookies: true);
		LoginSession login = (await client.PollAsync(qr.Key, default)).Account!;
		Check(login.Uid == 123 && login.Session == "next" && login.RefreshToken == "qr-token");

		var old = new LoginSession("before", "csrf1", 123, "old-token");
		script.Expect($"{API}cookie/info?csrf=csrf1", """{"code":0,"data":{"refresh":false}}""", "before");
		LoginSession current = old;
		Check(await client.MaintainAsync(old, updated => current = updated, default) == SessionMaintenance.UpToDate);
		Check(await client.MaintainAsync(current, _ => throw new InvalidOperationException("重复检查。"), default) == SessionMaintenance.Skipped);

		script.Expect($"{API}cookie/info?csrf=csrf1", """{"code":0,"data":{"refresh":true,"timestamp":1700000000000}}""", "before");
		script.Expect("https://www.bilibili.com/correspond/1/", """<div class="box" id="1-name">challenge</div>""", "before");
		script.Expect($"{API}cookie/refresh", """{"code":0,"data":{"refresh_token":"new-token"}}""", "before", "refresh_csrf=challenge", issue_cookies: true, include_uid: false);
		script.Expect($"{API}confirm/refresh", """{"code":-1}""", "next", "refresh_token=old-token");
		try {
			await client.MaintainAsync(old, updated => current = updated, default);
			throw new InvalidOperationException("未检测到确认失败。");
		}
		catch (InvalidDataException) { }
		Check(current.PendingConfirmation == "old-token" && current.RefreshToken == "new-token" && current.Uid == 123);
		script.Expect($"{API}confirm/refresh", """{"code":0}""", "next", "refresh_token=old-token");
		Check(await client.MaintainAsync(current, updated => current = updated, default) == SessionMaintenance.Refreshed);
		Check(current.PendingConfirmation.Length == 0 && current.Session == "next" && script.Remaining == 0);
		script.Expect($"{API}confirm/refresh", """{"code":-400}""", "next", "refresh_token=old-token");
		script.Expect($"{API}cookie/info?csrf=csrf2", """{"code":0,"data":{"refresh":false}}""", "next");
		Check(await client.MaintainAsync(current with { PendingConfirmation = "old-token" }, updated => current = updated, default) == SessionMaintenance.Refreshed);
		Check(current.PendingConfirmation.Length == 0 && script.Remaining == 0);
		Console.WriteLine("PASS: gzip decompression, QR states, cancellation, login, cooldown check, rotation and interrupted confirmation");
	}
}
