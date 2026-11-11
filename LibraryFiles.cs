using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace BiliFavSyncSharp;

internal static class LibraryFiles {
	[StructLayout(LayoutKind.Sequential)]
	private struct FileIdentity {
		public uint Attributes;
		public FILETIME Creation, Access, Written;
		public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileIdentity identity);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateHardLinkW(string link, string existing, IntPtr reserved);

	public static void CheckBvid(string bv) {
		if (!Regex.IsMatch(bv, @"\ABV[0-9A-Za-z]{10}\z"))
			throw new InvalidDataException($"BV 号格式错误：{bv}");
	}

	private static string Component(string name) {
		foreach (char invalid in Path.GetInvalidFileNameChars())
			name = name.Replace(invalid, '_');
		name = name.TrimEnd(' ', '.');
		if (name.Length == 0)
			throw new InvalidDataException("收藏夹或视频名称为空。");
		return Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase) ? $"_{name}" : name;
	}

	public static string FolderPath(string root, string folder) {
		string name = Component(folder);
		if (name.Equals(".pool", StringComparison.OrdinalIgnoreCase))
			throw new IOException("收藏夹名称与下载池冲突。");
		return Path.Combine(root, name);
	}

	private static void CheckDirectory(string path) {
		if (Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
			throw new IOException($"目录是重解析点：{path}");
	}

	public static void EnsureDirectory(string path) {
		CheckDirectory(path);
		Directory.CreateDirectory(path);
	}

	private static FileIdentity Identity(string path) {
		using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (!GetFileInformationByHandle(handle, out FileIdentity info))
			throw new Win32Exception(Marshal.GetLastWin32Error());
		return info;
	}

	private static void Link(string source, string target) {
		if (File.Exists(target)) {
			FileIdentity first = Identity(source);
			FileIdentity second = Identity(target);
			if (first.Volume == second.Volume && first.IndexHigh == second.IndexHigh && first.IndexLow == second.IndexLow)
				return;
			throw new IOException($"目标已有不同文件：{target}");
		}
		if (!CreateHardLinkW(target, source, IntPtr.Zero))
			throw new IOException($"创建硬链接失败：{target}", new Win32Exception(Marshal.GetLastWin32Error()));
	}

	public static void LinkVideo(string root, string bv, string title, string folder, int parts, CancellationToken token) {
		CheckBvid(bv);
		string pool = Path.Combine(root, ".pool");
		CheckDirectory(pool);
		string video = Path.Combine(pool, bv);
		CheckDirectory(video);
		string parent = FolderPath(root, folder);
		EnsureDirectory(parent);
		string destination = Path.Combine(parent, Component(title));
		EnsureDirectory(destination);
		var seen = new HashSet<int>();
		foreach (string page in Directory.EnumerateDirectories(video).Order(StringComparer.OrdinalIgnoreCase)) {
			token.ThrowIfCancellationRequested();
			string name = Path.GetFileName(page);
			if (!name.StartsWith('P') || !int.TryParse(name.AsSpan(1), out int number) || number < 1 || number > parts)
				continue;
			CheckDirectory(page);
			string? media = Directory.EnumerateFiles(page)
				.Where(static path => MediaExtensions.Contains(Path.GetExtension(path)))
				.Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
			if (media is null)
				continue;
			Link(media, Path.Combine(destination, $"{name} - {Path.GetFileName(media)}"));
			seen.Add(number);
		}
		if (seen.Count != parts || parts < 1)
			throw new IOException($"{bv} 下载池缺少完整视频分 P（{seen.Count}/{parts}）。");
		string description = Path.Combine(video, "desc.txt");
		if (File.Exists(description))
			Link(description, Path.Combine(destination, "desc.txt"));
	}

	private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".flv", ".webm", ".mov", ".avi", ".ts", ".m4v" };
}
