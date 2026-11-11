using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BiliFavSyncSharp;

internal static class SessionProtection {
	public static string ProtectLogin(LoginSession account) {
		return Protect(JsonSerializer.Serialize(account));
	}

	public static LoginSession RestoreLogin(string value) {
		return JsonSerializer.Deserialize<LoginSession>(Restore(value)) ?? throw new InvalidDataException("无法读取扫码登录状态。");
	}

	public static string Protect(string value) {
		if (value.Length == 0)
			return "";
		byte[] plain = Encoding.UTF8.GetBytes(value);
		try {
			return Convert.ToBase64String(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
		}
		finally {
			CryptographicOperations.ZeroMemory(plain);
		}
	}

	public static string Restore(string value) {
		if (value.Length == 0)
			return "";
		byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
		try {
			return Encoding.UTF8.GetString(plain);
		}
		finally {
			CryptographicOperations.ZeroMemory(plain);
		}
	}
}
