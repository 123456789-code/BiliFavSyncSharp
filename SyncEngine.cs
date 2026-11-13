namespace BiliFavSyncSharp;

internal sealed class SyncEngine(AppSettings settings, string session, Action<string> log, Action<string> verbose, Action<ConsoleFrame?>? preview = null) {
	public async Task RunAsync(bool rebuild, CancellationToken token) {
		string root = settings.StorageRoot;
		if (rebuild && !File.Exists(Path.Combine(root, "data.json")))
			throw new FileNotFoundException("所选目录没有 data.json，无法重建。");
		Directory.CreateDirectory(root);
		using var lease = new FileStream(Path.Combine(root, ".bilifavsync.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
		verbose($"已取得视频库写锁 · {root}");
		LibraryIndex index = AppStore.LoadIndex(root);
		string pool = Path.Combine(root, ".pool");
		LibraryFiles.EnsureDirectory(pool);
		verbose($"已读取 data.json · 本地 {index.Videos.Count} 部视频 · 下载池 {pool}");
		int failures = 0;
		if (rebuild) {
			foreach ((string bv, LibraryVideo video) in index.Videos) {
				foreach (string folder in video.Folders) {
					token.ThrowIfCancellationRequested();
					try {
						LibraryFiles.LinkVideo(root, bv, video.Title, folder, video.Parts, token, verbose);
						log($"已重建 · {folder} / {video.Title}");
					}
					catch (Exception exception) when (exception is not OperationCanceledException) {
						log($"{bv} · {exception.Message}");
						failures++;
					}
				}
			}
		}
		else {
			verbose(!settings.UseSystemProxy ? "网络模式 · 直连" : string.IsNullOrWhiteSpace(settings.ProxyEndpoint) ? "网络模式 · 使用系统默认代理规则" : "网络模式 · 使用指定代理");
			using var api = new BiliClient(settings.UserId, session, verbose, settings.UseSystemProxy, settings.ProxyEndpoint);
			await api.CheckLoginAsync(token);
			List<FavoriteFolder> folders = await api.FoldersAsync(settings.UserId, token);
			verbose($"已取得 {folders.Count} 个收藏夹");
			foreach (FavoriteFolder folder in folders) {
				if (index.Config.Ignore.Contains(folder.Id)) {
					verbose($"跳过已忽略收藏夹 · {folder.Name}（{folder.Id}）");
					continue;
				}
				log($"读取收藏夹 · {folder.Name}");
				LibraryFiles.EnsureDirectory(LibraryFiles.FolderPath(root, folder.Name));
				List<string> ids = await api.VideosAsync(folder.Id, token);
				verbose($"{folder.Name} · 共 {ids.Count} 个视频");
				foreach (string bv in ids) {
					token.ThrowIfCancellationRequested();
					bool changed = false;
					try {
						LibraryFiles.CheckBvid(bv);
						VideoDetail detail = await api.DetailAsync(bv, token);
						changed = await SyncVideoAsync(index, folder.Name, bv, detail, token);
					}
					catch (Exception exception) when (!token.IsCancellationRequested) {
						failures++;
						log($"{bv} · {exception.Message}");
					}
					if (changed) {
						AppStore.Save(Path.Combine(root, "data.json"), index);
						verbose($"索引已保存 · {bv}");
					}
					await Task.Delay(1000, token);
				}
				await Task.Delay(500, token);
			}
			if (failures == 0) {
				index.Config.LastSync = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
				AppStore.Save(Path.Combine(root, "data.json"), index);
			}
		}
		if (failures > 0)
			throw new IOException($"本轮有 {failures} 项失败，详情见日志。");
	}

	private async Task<bool> SyncVideoAsync(LibraryIndex index, string folder, string bv, VideoDetail detail, CancellationToken token) {
		LibraryVideo incoming = detail.Video;
		if (incoming.Parts <= 0)
			throw new InvalidDataException("视频已失效。");
		index.Videos.TryGetValue(bv, out LibraryVideo? existing);
		int previous = existing?.Parts ?? 0;
		verbose($"{bv} · 本地 {previous} P，线上 {incoming.Parts} P");
		if (existing is not null && previous >= incoming.Parts && existing.Folders.Contains(folder)) {
			verbose($"已归档，跳过 · {folder} / {incoming.Title}");
			return false;
		}
		string root = settings.StorageRoot;
		string pool = Path.Combine(root, ".pool");
		if (incoming.Parts > previous) {
			log($"下载 {bv} · {incoming.Title} · P{previous + 1}–P{incoming.Parts}");
			await Downloader.RunAsync(session, pool, bv, previous, incoming.Parts, token, verbose, settings.UseSystemProxy, settings.ProxyEndpoint, preview);
		}
		string description = Path.Combine(pool, bv, "desc.txt");
		if (!File.Exists(description))
			File.WriteAllText(description, detail.Description);
		LibraryVideo record = existing ?? incoming;
		int parts = Math.Max(previous, incoming.Parts);
		IEnumerable<string> destinations = incoming.Parts > previous ? record.Folders.Append(folder).Distinct() : [folder];
		foreach (string destination in destinations)
			LibraryFiles.LinkVideo(root, bv, record.Title, destination, parts, token, verbose);
		if (!record.Folders.Contains(folder))
			record.Folders.Add(folder);
		record.Parts = parts;
		index.Videos[bv] = record;
		log($"已同步 · {folder} / {record.Title}");
		return true;
	}
}
