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
$destination = Join-Path $env:RUNNER_TEMP 'BiliFavSyncSharp-install-test'
$app_path = Join-Path $destination 'BiliFavSyncSharp.exe'
$config_path = Join-Path $destination 'settings.json'

function Invoke-Checked([string]$binary, [string[]]$arguments) {
	$process = Start-Process -FilePath $binary -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
	if ($process.ExitCode -ne 0) { throw "运行失败 ($($process.ExitCode)) $binary" }
}

$install_args = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/DIR="' + $destination + '"'))
Invoke-Checked $setup_path $install_args
Invoke-Checked $app_path @('--version')
if (Test-Path -LiteralPath $config_path) { throw '全新安装包含了用户配置。' }
'{"AutomaticSync":false,"StartAtLogon":false,"UserId":123}' | Set-Content -Encoding utf8 -LiteralPath $config_path
$config_hash = (Get-FileHash -LiteralPath $config_path).Hash
Invoke-Checked $setup_path $install_args
if ((Get-FileHash -LiteralPath $config_path).Hash -ne $config_hash) { throw '升级改变了现有配置。' }
Invoke-Checked (Join-Path $destination 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
if (Test-Path -LiteralPath $app_path) { throw '卸载未移除程序。' }
if ((Get-FileHash -LiteralPath $config_path).Hash -ne $config_hash) { throw '卸载改变了现有配置。' }
Write-Output '安装、启动、覆盖升级、卸载和配置保留验证通过。'
