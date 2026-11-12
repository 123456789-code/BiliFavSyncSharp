# 内置下载引擎

基于 nilaoda/BBDown 1.6.3，提交 45622f79cd766e0fc6f5cbd49fcf4960340f35c3。上游：https://github.com/nilaoda/BBDown/tree/45622f79cd766e0fc6f5cbd49fcf4960340f35c3 。MIT 许可证见 LICENSE.txt。

保留解析、分段下载和封装代码，编译目标与桌面程序同为 .NET 10。由桌面程序启动独立工作进程，直接调用库，按用户设置选择网络路由。移除独立 CLI 解析、登录入口及 HTTP 服务。进度由下载字节计数产生，独立于控制台文本。HTTPS 连接使用平台证书验证。
