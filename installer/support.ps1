# Windows PowerShell 5.1 이상. 설치 위치의 파일과 현재 사용자의 차곡 등록만 처리한다.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Configure', 'Uninstall')][string]$Mode,
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [string]$Version = '0.10.6',
    [switch]$RegisterPath,
    [switch]$DesktopShortcut,
    [switch]$RemoveData
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$owner = 'Chagok.UserInstaller.v1'
$marker = '15193BE9-9D6D-4D58-9A90-23910F1544AE'
$registryPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Chagok'
$dataRoot = [Environment]::GetFolderPath('LocalApplicationData')
$dataDirectories = @((Join-Path $dataRoot 'Chagok'), (Join-Path $dataRoot 'FileOrganizer'))
$locks = New-Object 'System.Collections.Generic.List[System.IDisposable]'
$phase = '초기화'
$logFile = $null
$logAvailable = $false

function Write-InstallLog([string]$message) {
    if (-not $logFile) { return }
    try {
        [IO.File]::AppendAllText($logFile, ([DateTime]::UtcNow.ToString('o') + ' ' + $message + "`r`n"), (New-Object Text.UTF8Encoding($false)))
        $script:logAvailable = $true
    } catch { $script:logAvailable = $false }
}
function Set-Phase([string]$value) {
    $script:phase = $value
    Write-InstallLog ('단계: ' + $value)
}

function Same-Path([string]$a, [string]$b) {
    if (-not $a -or -not $b) { return $false }
    $a = $a.Replace('/', '\')
    $b = $b.Replace('/', '\')
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $a = [IO.Path]::GetFullPath($a)
        $b = [IO.Path]::GetFullPath($b)
    }
    return [string]::Equals($a.TrimEnd('\'), $b.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}
function In-Path([string]$path, [string]$parent) {
    return (Same-Path $path $parent) -or $path.StartsWith($parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoLinks([string]$path) {
    while ($path) {
        $exists = $true
        try { $attributes = [IO.File]::GetAttributes($path) }
        catch {
            $cause = $_.Exception.InnerException
            if ($cause -is [IO.FileNotFoundException] -or $cause -is [IO.DirectoryNotFoundException]) { $exists = $false }
            else { throw }
        }
        if ($exists -and ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "링크나 연결 폴더는 처리하지 않습니다: $path"
        }
        $path = [IO.Path]::GetDirectoryName($path)
    }
}
function Write-Result([string]$message, [bool]$failed) {
    # nsExec이 유니코드를 식별하도록 최종 안내에 UTF-16 BOM을 붙인다.
    $encoding = [Text.Encoding]::Unicode
    $bytes = $encoding.GetPreamble() + $encoding.GetBytes($message + "`r`n")
    $stream = if ($failed) { [Console]::OpenStandardError() } else { [Console]::OpenStandardOutput() }
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
}
function Read-State {
    Assert-NoLinks $stateFile
    if (-not [IO.File]::Exists($stateFile)) { return $null }
    $state = [IO.File]::ReadAllText($stateFile) | ConvertFrom-Json
    if ($state.Owner -ne $owner -or -not (Same-Path $state.Directory $InstallDirectory) -or
        $state.PathOwned -isnot [bool] -or $state.RegisterPath -isnot [bool]) {
        throw '이 폴더의 설치 정보를 확인할 수 없습니다. 기존 파일은 보존했습니다.'
    }
    return $state
}
function Save-State($state) {
    $temporary = $stateFile + '.tmp'
    Assert-NoLinks $temporary
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes(($state | ConvertTo-Json))
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    if ([IO.File]::Exists($stateFile)) { [IO.File]::Replace($temporary, $stateFile, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $stateFile) }
}
function Invoke-Path([string]$action) {
    Set-Phase ('PATH ' + $action)
    if (-not [IO.File]::Exists($executable)) { throw 'Chagok.exe가 없습니다. 설치 프로그램을 다시 실행해 복구하세요.' }
    Assert-NoLinks $executable
    $output = @(& $executable path $action --json)
    Write-InstallLog ('PATH 종료 코드: ' + $LASTEXITCODE)
    if ($LASTEXITCODE -ne 0) { throw "PATH $action 처리에 실패했습니다. 등록 상태를 확인한 뒤 다시 실행하세요." }
    return ($output -join "`n" | ConvertFrom-Json)
}
function Acquire-DataLocks {
    foreach ($directory in $dataDirectories) {
        Assert-NoLinks $directory
        if (-not [IO.Directory]::Exists($directory)) { continue }
        $path = Join-Path $directory 'session.lock'
        Assert-NoLinks $path
        try { $locks.Add([IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)) }
        catch { throw '다른 차곡이 데이터를 사용 중이거나 데이터 잠금을 열 수 없습니다. 다른 로그인 세션의 차곡도 종료한 뒤 다시 실행하세요.' }
    }
}
function Prepare-DataRemoval {
    $files = New-Object 'System.Collections.Generic.List[string]'
    foreach ($directory in $dataDirectories) {
        if (-not [IO.Directory]::Exists($directory)) { continue }
        foreach ($name in @('settings.json', 'settings.json.backup', 'settings.json.tmp', 'history.jsonl', 'legacy-import.json')) {
            $path = Join-Path $directory $name
            Assert-NoLinks $path
            if ([IO.Directory]::Exists($path)) { throw "데이터 파일 위치에 폴더가 있어 삭제하지 않았습니다: $path" }
            if ([IO.File]::Exists($path)) {
                $locks.Add([IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Delete))
                $files.Add($path)
            }
        }
    }
    return $files.ToArray()
}
function Remove-ShellRegistration {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\ksyrang\Chagok\Shell')
    if ($key) {
        try { $owned = $key.GetValue('ChagokOwner') -eq $marker -and (Same-Path ([string]$key.GetValue('ExecutablePath', '')) $executable) }
        finally { $key.Dispose() }
        if ($owned) {
            Get-AppxPackage -Name 'ksyrang.Chagok.ShellIntegration' | Where-Object { $_.Publisher -eq 'CN=ksyrang' } |
                ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree('Software\ksyrang\Chagok\Shell', $false)
        }
    }
    $menu = 'Software\Classes\Directory\shell\Chagok.AddRule'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($menu)
    if ($key) {
        $command = $key.OpenSubKey('command')
        try {
            $expected = '"' + $executable + '" --add-rule "%1"'
            $owned = $command -and $key.GetValue('ChagokOwner') -eq $marker -and
                [string]::Equals([string]$command.GetValue(''), $expected, [StringComparison]::OrdinalIgnoreCase)
        } finally { if ($command) { $command.Dispose() }; $key.Dispose() }
        if ($owned) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($menu, $false) }
    }
}
function Update-Shortcut([bool]$remove, [bool]$desktop = $false) {
    $directory = if ($desktop) { [Environment]::GetFolderPath('DesktopDirectory') }
                 else { Join-Path ([Environment]::GetFolderPath('Programs')) 'Chagok' }
    if (-not $directory) { throw '바로가기 폴더를 찾을 수 없습니다.' }
    $name = if ($desktop) { '차곡.lnk' } else { 'Chagok.lnk' }
    $path = Join-Path $directory $name
    Assert-NoLinks $path
    if ($remove -and -not [IO.File]::Exists($path)) { return }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($path)
        if ([IO.File]::Exists($path) -and -not (Same-Path $shortcut.TargetPath $executable)) {
            if ($remove) { return }
            throw "다른 프로그램의 바로가기가 있어 덮어쓰지 않았습니다: $path"
        }
        if ($remove) {
            [IO.File]::Delete($path)
            if (-not $desktop -and -not [IO.Directory]::EnumerateFileSystemEntries($directory).GetEnumerator().MoveNext()) { [IO.Directory]::Delete($directory) }
        } else {
            [void][IO.Directory]::CreateDirectory($directory)
            $shortcut.TargetPath = $executable
            $shortcut.WorkingDirectory = $InstallDirectory
            $shortcut.Description = '차곡 · Chagok'
            $shortcut.Save()
        }
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
}

try {
    try {
        $directory = Join-Path ([IO.Path]::GetTempPath()) 'Chagok-Installer-Logs'
        [void][IO.Directory]::CreateDirectory($directory)
        $logFile = Join-Path $directory ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $PID + '-' + $Mode + '.log')
        Write-InstallLog ('시작: mode=' + $Mode + '; version=' + $Version + '; powershell=' + $PSVersionTable.PSVersion + '; installDirectory=' + $InstallDirectory)
    } catch { $logFile = $null }
    Set-Phase '설치 경로 검사'
    if ($InstallDirectory -notmatch '^[A-Za-z]:[\\/]' -or $InstallDirectory.IndexOfAny([char[]]';"') -ge 0 -or
        $InstallDirectory.Contains("`r") -or $InstallDirectory.Contains("`n")) { throw '로컬 드라이브의 전체 설치 경로를 지정하세요.' }
    $InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
    if ($InstallDirectory.Length -lt 4 -or $InstallDirectory.Length -gt 180) { throw '드라이브 바로 아래가 아닌 180자 이내의 전용 설치 폴더를 선택하세요.' }
    Assert-NoLinks $InstallDirectory
    foreach ($directory in $dataDirectories) {
        if ((In-Path $InstallDirectory $directory) -or (In-Path $directory $InstallDirectory)) {
            throw '설치 폴더와 새/구버전 데이터 폴더는 서로 겹칠 수 없습니다.'
        }
    }
    $stateFile = Join-Path $InstallDirectory 'chagok-install.json'
    $executable = Join-Path $InstallDirectory 'Chagok.exe'
    foreach ($name in @('Chagok.exe', 'support.ps1', 'uninstall.exe', 'LICENSE', 'README.md', 'terminal-guide.md', 'ShellExtension-LICENSES.txt', 'chagok-install.json.tmp')) {
        $path = Join-Path $InstallDirectory $name
        Assert-NoLinks $path
        if ([IO.Directory]::Exists($path)) { throw "설치 파일 위치에 폴더가 있습니다: $path" }
    }
    $state = Read-State
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    if ($key) {
        try {
            if ($key.GetValue('ChagokOwner') -ne $owner -or -not (Same-Path ([string]$key.GetValue('InstallLocation', '')) $InstallDirectory)) {
                throw '다른 설치 위치 또는 등록 정보가 있습니다. 기존 차곡 설치를 먼저 제거하세요. 설정과 기록은 보존할 수 있습니다.'
            }
        } finally { $key.Dispose() }
    }
    switch ($Mode) {
        'Prepare' {
            Set-Phase '설치 정보 준비'
            if ($null -eq $state) {
                if ([IO.File]::Exists($InstallDirectory) -or
                    ([IO.Directory]::Exists($InstallDirectory) -and @([IO.Directory]::EnumerateFileSystemEntries($InstallDirectory)).Count -gt 0)) {
                    throw '다른 파일을 보존하기 위해 설치를 중단했습니다. 빈 폴더를 선택하세요.'
                }
                [void][IO.Directory]::CreateDirectory($InstallDirectory)
                $state = [pscustomobject]@{ Owner = $owner; Directory = $InstallDirectory; Version = $Version; PathOwned = $false; RegisterPath = $false; Pending = $true }
            }
            $state.Pending = $true
            Save-State $state
        }
        'Configure' {
            Set-Phase '설치 정보 확인'
            if ($null -eq $state) { throw '설치 준비 정보가 없습니다. 설치 프로그램을 다시 실행하세요.' }
            if ($RegisterPath) {
                $status = Invoke-Path 'status'
                if (-not $status.registered) { $state.PathOwned = $true; Save-State $state }
                $null = Invoke-Path 'add'
            } elseif ($state.PathOwned) {
                $null = Invoke-Path 'remove'
                $state.PathOwned = $false
            }
            Set-Phase '시작 메뉴 등록'
            Update-Shortcut $false
            Set-Phase '바탕화면 바로가기 설정'
            Update-Shortcut (-not [bool]$DesktopShortcut) $true
            Set-Phase '설치 정보 저장'
            $state.Version = $Version
            $state.RegisterPath = [bool]$RegisterPath
            $state.Pending = $false
            Save-State $state
        }
        'Uninstall' {
            Set-Phase '제거 준비 및 데이터 잠금'
            if ($null -eq $state) { throw '설치 정보를 확인할 수 없어 파일과 데이터를 보존했습니다.' }
            Acquire-DataLocks
            $filesToRemove = @()
            if ($RemoveData) {
                # 삭제 전에 모든 대상의 링크·형식·쓰기 잠금을 확인한다.
                $filesToRemove = @(Prepare-DataRemoval)
            }
            Set-Phase '탐색기 등록 해제'
            Remove-ShellRegistration
            if ($state.PathOwned) {
                $null = Invoke-Path 'remove'
                $state.PathOwned = $false
                Save-State $state
            }
            Set-Phase '시작 메뉴 해제'
            Update-Shortcut $true
            Set-Phase '바탕화면 바로가기 해제'
            Update-Shortcut $true $true
            if ($RemoveData) {
                Set-Phase '선택한 설정과 기록 삭제'
                # 구버전 원본도 지워 재설치 시 삭제한 설정을 다시 가져오지 않는다. 알 수 없는 파일은 남긴다.
                foreach ($path in $filesToRemove) {
                    Assert-NoLinks $path
                    [IO.File]::Delete($path)
                }
            }
        }
    }
    Write-InstallLog ('완료: ' + $Mode)
    Write-Result ('완료: ' + $Mode) $false
    exit 0
} catch {
    Write-InstallLog ('실패: ' + $_.Exception.ToString() + "`r`n" + $_.ScriptStackTrace)
    $message = '단계: ' + $phase + "`r`n" + $_.Exception.Message
    if ($logAvailable) { $message += "`r`n로그: " + $logFile }
    else { $message += "`r`n로그를 저장하지 못했습니다." }
    Write-Result $message $true
    exit 1
} finally {
    foreach ($lease in $locks) { $lease.Dispose() }
}
