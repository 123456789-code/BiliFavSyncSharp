using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BBDown;

public static class BundledEngine {
	internal static Action<int, long, bool>? Progress { get; private set; }

	public static void ConfigureNetwork(bool use_system_proxy) {
		if (!use_system_proxy)
			HttpClient.DefaultProxy = new WebProxy();
	}

	public static async Task DownloadAsync(string bv, string session, string pool, string ffmpeg, string pages, Action<int, long, bool> progress) {
		var jar = Core.Util.HTTPUtil.SessionCookies;
		jar.Add(new Cookie("SESSDATA", session, "/", ".bilibili.com") { Secure = true });
		jar.Add(new Cookie("CURRENT_FNVAL", "4048", "/", ".bilibili.com") { Secure = true });
		Progress = progress;
		try {
			await Program.DoWorkAsync(new MyOption {
				Url = bv,
				Cookie = $"SESSDATA={session}",
				ForceHttp = false,
				WorkDir = pool,
				FFmpegPath = ffmpeg,
				SkipSubtitle = true,
				ShowAll = true,
				SelectPage = pages,
				FilePattern = "<bvid>/P1/<videoTitle>",
				MultiFilePattern = "<bvid>/P<pageNumberWithZero>/<pageTitle>"
			});
		}
		finally { Progress = null; }
	}
}
