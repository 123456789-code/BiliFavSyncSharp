param()
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot
$cache = Join-Path $project '.local/tool-downloads'
$destination = Join-Path $project '.local/bundled-media'
[void][IO.Directory]::CreateDirectory($cache)
[void][IO.Directory]::CreateDirectory($destination)

function Get-Archive([string]$url, [string]$digest) {
	$archive = Join-Path $cache ($url.Split('/')[-1])
	if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $digest) {
		$temp_path = "$archive.$([guid]::NewGuid()).tmp"
		try {
			Invoke-WebRequest -Uri $url -OutFile $temp_path
			if ((Get-FileHash -LiteralPath $temp_path).Hash -ne $digest) { throw "下载校验失败：$url" }
			[IO.File]::Copy($temp_path, $archive, $true)
		}
		finally { if ([IO.File]::Exists($temp_path)) { [IO.File]::Delete($temp_path) } }
	}
	return [IO.Compression.ZipFile]::OpenRead($archive)
}

function Copy-Entry($archive, [string]$pattern, [string]$name) {
	$entries = @($archive.Entries | Where-Object FullName -match $pattern)
	if ($entries.Count -ne 1) { throw "压缩包中文件数量异常：$pattern" }
	[IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], (Join-Path $destination $name), $true)
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$ffmpeg = Get-Archive 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip' '60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA'
try {
	Copy-Entry $ffmpeg '(^|/)bin/ffmpeg\.exe$' 'ffmpeg.exe'
	Copy-Entry $ffmpeg '(^|/)LICENSE(\.txt)?$' 'FFmpeg-LICENSE.txt'
}
finally { $ffmpeg.Dispose() }
Copy-Item -LiteralPath (Join-Path $project 'installer/BBDown-LICENSE.txt') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $project 'installer/Tool-Sources.txt') -Destination $destination -Force
