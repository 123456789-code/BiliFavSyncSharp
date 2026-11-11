param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$project_path = Split-Path $PSScriptRoot
$local_path = Join-Path $project_path '.local'
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
	& (Join-Path $PSScriptRoot 'Prepare-Tools.ps1')
	foreach ($name in $build_env.Keys) {
		$previous[$name] = [Environment]::GetEnvironmentVariable($name)
		[Environment]::SetEnvironmentVariable($name, $build_env[$name])
	}
	$project = Join-Path $project_path 'BiliFavSyncSharp.csproj'
	& dotnet restore $project --configfile (Join-Path $PSScriptRoot 'Offline.config') --source 'https://api.nuget.org/v3/index.json' --disable-build-servers
	if ($LASTEXITCODE -ne 0) { throw "还原失败 ($LASTEXITCODE)" }
	& dotnet build $project -c $Configuration --no-restore --disable-build-servers
	if ($LASTEXITCODE -ne 0) { throw "编译失败 ($LASTEXITCODE)" }
}
finally {
	foreach ($name in $previous.Keys) {
		[Environment]::SetEnvironmentVariable($name, $previous[$name])
	}
}
