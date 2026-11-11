param(
	[string]$InstallerCompiler,
	[string]$PackageSource = 'https://api.nuget.org/v3/index.json'
)
$ErrorActionPreference = 'Stop'
$project_root = Split-Path $PSScriptRoot
$project = Join-Path $project_root 'BiliFavSyncSharp.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$local_path = Join-Path $project_root '.local'
$release_path = Join-Path $project_root ("artifacts\v$version-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$payload = Join-Path $release_path 'app'
[void][IO.Directory]::CreateDirectory($release_path)
if (Test-Path -LiteralPath $payload) { throw '发布目录已有文件，请重新运行。' }
$build_env = @{
	DOTNET_CLI_HOME = $local_path
	DOTNET_CLI_TELEMETRY_OPTOUT = '1'
	DOTNET_NOLOGO = '1'
	DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
	DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
	DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
	NUGET_PACKAGES = Join-Path $local_path 'packages'
	NUGET_HTTP_CACHE_PATH = Join-Path $local_path 'http-cache'
	NUGET_PLUGINS_CACHE_PATH = Join-Path $local_path 'plugin-cache'
	NUGET_SCRATCH = Join-Path $local_path 'scratch'
	TEMP = Join-Path $local_path 'temp'
	TMP = Join-Path $local_path 'temp'
	MSBUILDDISABLENODEREUSE = '1'
}
[void][IO.Directory]::CreateDirectory($build_env.TEMP)
$previous = @{}
try {
	foreach ($name in $build_env.Keys) {
		$previous[$name] = [Environment]::GetEnvironmentVariable($name)
		[Environment]::SetEnvironmentVariable($name, $build_env[$name])
	}
	& dotnet restore $project -r win-x64 --configfile (Join-Path $PSScriptRoot 'Offline.config') --source $PackageSource --disable-build-servers -p:SelfContained=true -p:NuGetAudit=false
	if ($LASTEXITCODE -ne 0) { throw '自包含运行时还原失败。' }
	& dotnet publish $project --no-restore -c Release -r win-x64 --self-contained true -o $payload --disable-build-servers -p:DebugType=None -p:DebugSymbols=false
	if ($LASTEXITCODE -ne 0) { throw '自包含发布失败。' }
	foreach ($name in @('README.md', 'LICENSE')) {
		Copy-Item -LiteralPath (Join-Path $project_root $name) -Destination $payload
	}
	$licenses = Join-Path $payload 'licenses'
	[void][IO.Directory]::CreateDirectory($licenses)
	Copy-Item -LiteralPath (Join-Path $project_root 'installer\HardLinkAutoBackup-LICENSE.txt') -Destination $licenses
	$config = Get-Content -Raw -LiteralPath (Join-Path $payload 'BiliFavSyncSharp.runtimeconfig.json') | ConvertFrom-Json
	if (!$config.runtimeOptions.includedFrameworks) { throw '运行时配置未包含自包含框架。' }
	foreach ($framework in $config.runtimeOptions.includedFrameworks) {
		$package_id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
		$package = Join-Path $env:NUGET_PACKAGES ($package_id + '\' + $framework.version)
		$destination = Join-Path $licenses $framework.name
		[void][IO.Directory]::CreateDirectory($destination)
		$notices = @(Get-ChildItem -LiteralPath $package -File | Where-Object Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$')
		if (!$notices) { throw "运行时许可缺失：$package_id" }
		foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $destination }
	}
	$this_app = Join-Path $payload 'BiliFavSyncSharp.exe'
	$check = Start-Process -FilePath $this_app -ArgumentList '--version' -WindowStyle Hidden -PassThru -Wait
	if ($check.ExitCode -ne 0) { throw '自包含启动检查失败。' }
	if ($InstallerCompiler) {
		& $InstallerCompiler "/DAppVersion=$version" "/DSourceDir=$payload" "/DReleaseDir=$release_path" (Join-Path $project_root 'installer\BiliFavSyncSharp.iss')
		if ($LASTEXITCODE -ne 0) { throw '编译安装程序失败。' }
		$setup = Join-Path $release_path "BiliFavSyncSharp-$version-Setup-x64.exe"
		(Get-FileHash -LiteralPath $setup).Hash + '  ' + (Split-Path -Leaf $setup) | Set-Content -Encoding ascii -LiteralPath (Join-Path $release_path 'SHA256SUMS.txt')
		Write-Output $setup
	}
	else { Write-Output $payload }
}
finally {
	foreach ($name in $previous.Keys) {
		[Environment]::SetEnvironmentVariable($name, $previous[$name])
	}
}
