using System.Net;
using System.Net.Sockets;
using System.Text;
using BBDown;

namespace EngineChecks;

internal static class Program {
	private static async Task Main(string[] args) {
		bool proxy = args[0] == "system";
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		using var server = new TcpListener(IPAddress.Loopback, 0);
		server.Start();
		int port = ((IPEndPoint)server.LocalEndpoint).Port;
		Environment.SetEnvironmentVariable("HTTP_PROXY", $"http://127.0.0.1:{(proxy ? port : 1)}");
		Environment.SetEnvironmentVariable("NO_PROXY", "");
		BundledEngine.ConfigureNetwork(proxy);
		string url = proxy ? "http://media.invalid/video" : $"http://127.0.0.1:{port}/video";
		byte[] media = "fixture-media"u8.ToArray();
		async Task ServeAsync() {
			using TcpClient socket = await server.AcceptTcpClientAsync(deadline.Token);
			using NetworkStream stream = socket.GetStream();
			using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
			string? line = await reader.ReadLineAsync(deadline.Token);
			if (line != $"GET {(proxy ? url : "/video")} HTTP/1.1")
				throw new IOException($"意外的网络路由：{line}");
			while (await reader.ReadLineAsync(deadline.Token) is string { Length: > 0 }) { }
			await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {media.Length}\r\nConnection: close\r\n\r\n"), deadline.Token);
			await stream.WriteAsync(media, deadline.Token);
		}
		string target = Path.Combine(Path.GetFullPath(args[1]), $"{Guid.NewGuid():N}.mp4");
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		Task serve = ServeAsync();
		await BBDownDownloadUtil.DownloadFile(url, target, new BBDownDownloadUtil.DownloadConfig()).WaitAsync(deadline.Token);
		await serve;
		if (!File.ReadAllBytes(target).SequenceEqual(media))
			throw new InvalidDataException("下载内容校验失败。");
		File.Delete(target);
		Console.WriteLine($"PASS: embedded media download / {args[0]}");
	}
}
