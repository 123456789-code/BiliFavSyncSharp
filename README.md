# BiliFavSyncSharp

Windows 上的 B 站收藏夹同步工具。使用 .NET 10 和 WinForms，按 BV 号共享视频文件，按收藏夹建立硬链接。源项目的许可证见 LICENSE。

## 使用

1. 在项目目录运行 `.\scripts\Build.ps1`，首次构建会在项目 .local 目录准备下载组件，此后也可在 Visual Studio 中打开 BiliFavSyncSharp.slnx。构建需要 .NET 10 SDK 和 Windows 桌面目标包。
2. 运行 `bin\Release\net10.0-windows\BiliFavSyncSharp.exe`，打开“程序设置”。点击“扫码登录”，用哔哩哔哩手机 App 扫码并确认，再选择视频库目录、保存设置。首次扫码会自动填入登录 UID。已有用户也可手动填写 UID 和 SESSDATA。下载引擎由仓库内的 BBDown 源码编译，使用独立工作进程和随包 FFmpeg。
3. 点“立即同步”执行一轮。勾选“每小时同步”会立即执行一轮，在每轮结束一小时后再次执行。开启“关闭窗口后在托盘继续运行”后，关闭主窗口会隐藏至通知区域。双击托盘图标可返回主窗口，托盘菜单可退出程序。
4. “重建硬链接”只需视频库目录，按 data.json 和 .pool 中的分 P 恢复收藏夹目录。已有同名但内容来源不同的文件会报冲突。详情见窗口日志及%LOCALAPPDATA%\BiliFavSyncSharp 中的 latest.log。

在视频表格中，右键单元格可复制完整文本。用鼠标左键双击“视频标题”列中的视频名称，可打开其所在的收藏夹视频目录；同一视频属于多个收藏夹时，打开第一个仍存在的目录。

## 网络代理

“程序设置 → 使用系统代理”默认勾选，登录、续期、同步及内置 BBDown 使用 .NET 默认代理规则（代理环境变量优先，其次为 Windows 用户代理设置）。取消勾选后直接连接。保存后对下一次任务生效；设置窗口中的扫码使用当前勾选状态。

## 任务日志

主窗口勾选“详细日志”后，当前任务会实时记录接口耗时、归档判断、BBDown 控制台的实时输出（含标准错误、分 P、进度和速度）、硬链接路径和索引写入。下载百分比和速度在主窗口的进度条原位更新；每段下载只在日志中保留最后一次进度；未输出新内容的等待期间不会重复打印计时。取消勾选后继续显示常规进度和错误。开关默认关闭，重启后沿用上次选择。下方列表保留最近 500 条；点“打开日志”可查看整轮记录。每次开始任务会重写 %LOCALAPPDATA%\BiliFavSyncSharp\latest.log。

## 接续 Python 版视频库

先退出 Python 同步程序，在设置中选择含 data.json 和 .pool 的原视频库根目录。原来的 sync_config.Ignore 数字 ID 列表、library、folder 和未识别的 JSON 属性会保留。程序同步用户自己创建的收藏夹；失效视频会记录到日志，已经归档的视频保留在本地。首次使用可以先执行离线重建。

设置保存在 %LOCALAPPDATA%\BiliFavSyncSharp\settings.json。首次启动会读取同目录旧程序旁或旧版默认安装位置的设置。保存新登录时可选择在本机记住或仅本次使用。仅本次使用的凭据会在退出时丢失。扫码账户在运行同步时每天检查续期，B 站要求更新时自动轮换凭据。离线重建无需登录。会话被撤销时需重新扫码；手动填写的 SESSDATA 没有刷新凭据。勾选“记住登录状态”后，Windows 会使用当前用户的 DPAPI 加密凭据，密文保存在 settings.json。取消勾选并保存会移除密文。下载凭据通过工作进程的标准输入管道传递。重建要求视频库所在文件系统支持硬链接（例如 NTFS）。
## 登录自启和无人值守同步

在“程序设置”中勾选“登录 Windows 后自动启动”，保存后程序会注册当前用户专属的计划任务。取消勾选并保存可移除该任务。“核对自启状态”显示实际路径和权限，移动程序后可以点“同步自启任务”再保存。管理员启动可单独选择，申请权限时请使用同一账户。默认以普通权限运行。

如需登录后自动下载，请同时配置有效凭据、勾选“记住登录状态”，并在主窗口开启“每小时同步”。主开关会跨启动保留。选择后台运行后，登录自启时窗口隐藏在托盘。卸载前先关闭自启并从托盘退出。安装器保留你的视频库和本地设置。

## 从 GitHub Actions 获取安装包

将本地 main 分支推送到 GitHub。打开 Actions → Windows installer，待构建和安装、覆盖升级、卸载验证通过后，从 Artifacts 下载 BiliFavSyncSharp-Windows-x64，解压即可得到安装程序和 SHA256SUMS.txt。也可以在 Actions 页面手动运行工作流。

## 本地构建安装包

运行 `./scripts/Publish-Release.ps1 -InstallerCompiler '你的 Inno Setup 6 安装目录\ISCC.exe'`。脚本还原 .NET 10 的 Windows x64 运行时，生成自包含的简体中文安装包和 SHA-256 清单，位于 artifacts 中。安装向 Windows 请求管理员权限，默认安装在系统 Program Files 目录，桌面快捷方式及安装后启动可在向导中勾选。

## 发布到 GitHub Releases

推送与项目版本一致的标签（例如 v1.1.0），或在推送到 main 的最后一条提交说明中加入 [release]。也可在 main 上手动运行 Windows installer 并勾选发布选项。工作流会在安装、升级和卸载验证通过后，直接向 Release 上传安装程序 EXE，附件的 SHA-256 可在 GitHub 上查看。发布后续版本前请更新项目文件中的 Version。


内置下载组件来自 [BBDown 1.6.3](https://github.com/nilaoda/BBDown/releases/tag/1.6.3) 和 [FFmpeg 9.0.2 Essentials](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2)。对应许可证和来源保存在安装目录的 tools 中。旧版如已启用自启，请从旧程序关闭自启并退出，安装后在新版设置中重新启用。
