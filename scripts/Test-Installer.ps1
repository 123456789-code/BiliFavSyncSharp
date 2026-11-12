param([Parameter(Mandatory)][string]$ReleaseRoot)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
	throw '安装验证仅在 GitHub 托管 Windows 环境中运行。'
}
$setups = @(Get-ChildItem -LiteralPath $ReleaseRoot -Filter '*-Setup-x64.exe' -File)
if ($setups.Count -ne 1) { throw '安装包数量与预期不符。' }
$setup_path = $setups[0].FullName
$hash = (Get-FileHash -LiteralPath $setup_path).Hash
if (((Get-Content -LiteralPath (Join-Path $ReleaseRoot 'SHA256SUMS.txt') -Raw).Trim() -split '\s+')[0] -ne $hash) {
	throw '安装包校验失败。'
}
$destination = Join-Path $env:ProgramFiles 'BiliFavSyncSharp'
$app_path = Join-Path $destination 'BiliFavSyncSharp.exe'
$config_path = Join-Path $env:LOCALAPPDATA 'BiliFavSyncSharp\settings.json'

function Invoke-Checked([string]$binary, [string[]]$arguments) {
	$process = Start-Process -FilePath $binary -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
	if ($process.ExitCode -ne 0) { throw "运行失败 ($($process.ExitCode)) $binary" }
}

$install_args = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
Invoke-Checked $setup_path $install_args
Invoke-Checked $app_path @('--version')
Invoke-Checked (Join-Path $destination 'tools\BBDown.exe') @('--help')
Invoke-Checked (Join-Path $destination 'tools\ffmpeg.exe') @('-version')
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class IconCheck {
	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	public static extern uint ExtractIconEx(string file, int index, IntPtr large, IntPtr small, uint count);
}
"@
foreach ($file in @($app_path, $setup_path)) {
	if ([IconCheck]::ExtractIconEx($file, -1, [IntPtr]::Zero, [IntPtr]::Zero, 0) -lt 1) { throw "EXE 缺少图标：$file" }
}
if (Test-Path -LiteralPath $config_path) { throw '全新安装包含了用户配置。' }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($config_path))
$fixture = @{ Uid = 123; Session = 'test-session'; Csrf = 'test-csrf'; RefreshToken = 'test-refresh' } | ConvertTo-Json -Compress
$scope = [Security.Cryptography.DataProtectionScope]::CurrentUser
$encrypted = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($fixture), $null, $scope)
@{ UserId = 123; AutomaticSync = $false; ProtectedLogin = [Convert]::ToBase64String($encrypted) } | ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath $config_path
$config_hash = (Get-FileHash -LiteralPath $config_path).Hash
Invoke-Checked $setup_path $install_args
if ((Get-FileHash -LiteralPath $config_path).Hash -ne $config_hash) { throw '升级改变了现有配置。' }
$saved = Get-Content -Raw -LiteralPath $config_path | ConvertFrom-Json
$restored = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($saved.ProtectedLogin), $null, $scope)
if ([Text.Encoding]::UTF8.GetString($restored) -ne $fixture) { throw '覆盖安装后登录凭据无法恢复。' }
Invoke-Checked (Join-Path $destination 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
if (Test-Path -LiteralPath $app_path) { throw '卸载未移除程序。' }
if ((Get-FileHash -LiteralPath $config_path).Hash -ne $config_hash) { throw '卸载改变了现有配置。' }
Write-Output '安装、启动、覆盖升级、卸载和配置保留验证通过。'
