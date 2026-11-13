using System.Net;

namespace BiliFavSyncSharp;

internal static class ProxyAddress {
	public static WebProxy? Resolve(bool enabled, string? address) {
		if (!enabled || string.IsNullOrWhiteSpace(address))
			return null;
		if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri? uri)
			|| uri.Scheme is not ("http" or "https" or "socks5")
			|| uri.Host.Length == 0 || uri.Port == 0 || uri.AbsolutePath is not ("" or "/")
			|| uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
			throw new ArgumentException("请输入 HTTP、HTTPS 或 SOCKS5 代理地址和端口。地址中不能包含账号、密码或路径。");
		if (uri.Port < 0)
			throw new ArgumentException("请填写代理端口。");
		return new WebProxy(uri);
	}
}
