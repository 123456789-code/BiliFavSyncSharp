# BiliFavSyncSharp

Windows 上的 B 站收藏夹同步工具。使用 .NET 10 和 WinForms，按 BV 号共享视频文件，按收藏夹建立硬链接。源项目的许可证见 LICENSE。

## 使用

1. 在项目目录运行 `.\scripts\Build.ps1`，或在 Visual Studio 中打开 BiliFavSyncSharp.slnx。构建需要 .NET 10 SDK 和 Windows 桌面目标包。
2. 运行 `bin\Release\net10.0-windows\BiliFavSyncSharp.exe`，打开“程序设置”。填写 UID、SESSDATA 的值，选择视频库目录和已有的 BBDown.exe。下载器继续使用原来的工作目录和配套工具（如 ffmpeg、aria2c）。
3. 点“立即同步”执行一轮。勾选“每小时同步”会立即执行一轮，在每轮结束一小时后再次执行。开启“关闭窗口后在托盘继续运行”后，关闭主窗口会隐藏至通知区域。双击托盘图标可返回主窗口，托盘菜单可退出程序。
4. “重建硬链接”只需视频库目录，按 data.json 和 .pool 中的分 P 恢复收藏夹目录。已有同名但内容来源不同的文件会报冲突。详情见窗口日志及可执行文件旁的 latest.log。

## 接续 Python 版视频库

先退出 Python 同步程序，在设置中选择含 data.json 和 .pool 的原视频库根目录。原来的 sync_config.Ignore 数字 ID 列表、library、folder 和未识别的 JSON 属性会保留。程序同步用户自己创建的收藏夹；失效视频会记录到日志，已经归档的视频保留在本地。首次使用可以先执行离线重建。

设置保存在程序旁的 settings.json。默认仅在内存中保存 SESSDATA。勾选“在本机记住 SESSDATA”后，Windows 会使用当前用户的 DPAPI 加密凭据，密文保存在 settings.json。取消勾选并保存会移除密文。下载时按 BBDown 的接口以进程参数传递，请使用你信任的本机下载器。重建要求视频库所在文件系统支持硬链接（例如 NTFS）。
## 登录自启和无人值守同步

在“程序设置”中勾选“登录 Windows 后自动启动”，保存后程序会注册当前用户专属的计划任务。取消勾选并保存可移除该任务。“核对自启状态”显示实际路径和权限，移动程序后可以点“同步自启任务”再保存。管理员启动可单独选择，申请权限时请使用同一账户。默认以普通权限运行。

如需登录后自动下载，请同时配置有效凭据、勾选记住 SESSDATA，并在主窗口开启“每小时同步”。主开关会跨启动保留。选择后台运行后，登录自启时窗口隐藏在托盘。卸载前先关闭自启并从托盘退出。安装器保留你的视频库和本地设置。

## 从 GitHub Actions 获取安装包

将本地 main 分支推送到 GitHub。打开 Actions → Windows installer，待构建和安装、覆盖升级、卸载验证通过后，从 Artifacts 下载 BiliFavSyncSharp-Windows-x64，解压即可得到安装程序和 SHA256SUMS.txt。也可以在 Actions 页面手动运行工作流。

## 本地构建安装包

运行 `./scripts/Publish-Release.ps1 -InstallerCompiler '你的 Inno Setup 6 安装目录\ISCC.exe'`。脚本还原 .NET 10 的 Windows x64 运行时，生成自包含的简体中文安装包和 SHA-256 清单，位于 artifacts 中。安装默认面向当前 Windows 用户，桌面快捷方式及安装后启动可在向导中勾选。下载器仍在程序设置中单独指定。
