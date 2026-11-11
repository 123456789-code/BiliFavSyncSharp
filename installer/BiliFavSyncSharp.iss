#ifndef AppVersion
#error AppVersion is required
#endif
#ifndef SourceDir
#error SourceDir is required
#endif
#ifndef ReleaseDir
#error ReleaseDir is required
#endif

[Setup]
AppId={{D9C9A665-21AB-499B-B7A4-E7DFD9485463}
AppName=B站收藏夹同步
AppVersion={#AppVersion}
AppPublisher=123456789-code

DefaultDirName={autopf}\BiliFavSyncSharp
DefaultGroupName=B站收藏夹同步
PrivilegesRequired=admin
AppMutex=Global\BiliFavSyncSharp.Running
SetupIconFile=..\Assets\App.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
WizardStyle=modern

UninstallDisplayIcon={app}\BiliFavSyncSharp.exe
LicenseFile=..\LICENSE
OutputDir={#ReleaseDir}
OutputBaseFilename=BiliFavSyncSharp-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\B站收藏夹同步"; Filename: "{app}\BiliFavSyncSharp.exe"
Name: "{commondesktop}\B站收藏夹同步"; Filename: "{app}\BiliFavSyncSharp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BiliFavSyncSharp.exe"; Description: "打开B站收藏夹同步"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function GetTempFileNameW(directory, prefix: String; unique: Cardinal; file_name: String): Cardinal;
	external 'GetTempFileNameW@kernel32.dll stdcall';

function WriteAccessError(const path: String): String;
begin
	Log('BFS_WRITE_CHECK_FAILED: ' + path);
	Result := '安装程序无法写入以下位置：' + #13#10 + path + #13#10#13#10;
	Result := Result + '请从托盘退出旧程序，并检查上述文件是否只读或被占用。检查通过前不会开始复制程序文件；请勿跳过失败文件。';
end;

function CheckInstallWriteAccess: String;
var
	directory, parent_directory, probe, file_path: String;
	stream: TFileStream;
	found: TFindRec;
begin
	Result := '';
	directory := ExpandConstant('{app}');
	{ 目标尚不存在时检查最近的已有父目录，不留下新建目录。 }
	while not DirExists(directory) do begin
		parent_directory := ExtractFileDir(directory);
		if FileExists(directory) or (parent_directory = directory) or (parent_directory = '') then begin
			Result := WriteAccessError(directory);
			exit;
		end;
		directory := parent_directory;
	end;
	probe := StringOfChar(#0, 260);
	if GetTempFileNameW(directory, 'bfs', 0, probe) = 0 then begin
		Result := WriteAccessError(directory);
		exit;
	end;
	probe := Copy(probe, 1, Pos(#0, probe) - 1);
	if not DeleteFile(probe) then begin
		Result := WriteAccessError(probe);
		exit;
	end;
	{ 创建新文件成功不代表可以覆盖已有卸载器；只打开检查，不改动其内容。 }
	if FindFirst(ExpandConstant('{app}\unins*.*'), found) then begin
		try
			repeat
				if (CompareText(ExtractFileExt(found.Name), '.exe') = 0) or
					(CompareText(ExtractFileExt(found.Name), '.dat') = 0) then begin
					file_path := ExpandConstant('{app}\') + found.Name;
					try
						stream := TFileStream.Create(file_path, fmOpenReadWrite or fmShareDenyNone);
						stream.Free;
					except
						Result := WriteAccessError(file_path);
						exit;
					end;
				end;
			until not FindNext(found);
		finally
			FindClose(found);
		end;
	end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
	Result := CheckInstallWriteAccess;
end;

function InitializeUninstall: Boolean;
var
	service, tasks, task, xml, commands: Variant;
	i, j, count: Integer;
	task_name, command_path: String;
	loaded: Boolean;
begin
	Result := False;
	try
		service := CreateOleObject('Schedule.Service');
		service.Connect();
		tasks := service.GetFolder('\').GetTasks(1);
		for i := 1 to tasks.Count do begin
			task := tasks.Item(i);
			task_name := task.Name;
			if Pos('BiliFavSyncSharp-', task_name) = 1 then begin
				xml := CreateOleObject('MSXML2.DOMDocument.6.0');
				xml.async := False;
				loaded := xml.loadXML(task.Xml);
				if not loaded then
					raiseException('无法读取自启任务定义。');
				commands := xml.selectNodes('//*[local-name()="Exec"]/*[local-name()="Command"]');
				count := commands.length;
				for j := 0 to count - 1 do begin
					command_path := commands.item(j).text;
					if CompareText(command_path, ExpandConstant('{app}\BiliFavSyncSharp.exe')) = 0 then begin
						MsgBox('请先在程序设置中关闭该安装位置的登录自启，然后重试卸载。', mbInformation, MB_OK);
						exit;
					end;
				end;
			end;
		end;
		Result := True;
	except
		MsgBox('无法核对登录自启任务：' + GetExceptionMessage, mbError, MB_OK);
	end;
end;
