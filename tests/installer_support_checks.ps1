# Windows 등록은 실행하지 않고 공통 파일·잠금·보존 로직을 임시 폴더에서 검증한다.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../installer/support.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($definition in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    Invoke-Expression $definition.Extent.Text
}
$checks = 0
function Check([bool]$value, [string]$message) {
    if (-not $value) { throw $message }
    $script:checks++
}
function Must-Fail([scriptblock]$action) {
    $failed = $false
    try { & $action } catch { $failed = $true }
    Check $failed '실패해야 하는 작업이 성공했습니다.'
}
$temporaryBase = [IO.Path]::GetTempPath()
if ([IO.Directory]::Exists('/private/tmp')) { $temporaryBase = '/private/tmp' }
$testRoot = Join-Path $temporaryBase ('chagok-installer-check-' + [guid]::NewGuid().ToString('N'))
$InstallDirectory = Join-Path $testRoot 'install'
$stateFile = Join-Path $InstallDirectory 'chagok-install.json'
$owner = 'Chagok.UserInstaller.v1'
$dataDirectories = @((Join-Path $testRoot 'Chagok'), (Join-Path $testRoot 'FileOrganizer'))
$locks = New-Object 'System.Collections.Generic.List[System.IDisposable]'
try {
    $directoryParameter = @($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'InstallDirectory' })[0]
    Check ($null -eq $directoryParameter.DefaultValue -and $directoryParameter.Extent.Text.Contains('Mandatory = $true')) '설치 경로는 암묵적 스크립트 위치 대신 명시적으로 받아야 함'
    $installerSource = [IO.File]::ReadAllLines((Join-Path $PSScriptRoot '../installer/chagok.nsi'))
    foreach ($mode in @('Prepare', 'Configure', 'Uninstall')) {
        $calls = @($installerSource | Where-Object { $_.Contains('nsExec::ExecToStack') -and $_.Contains('-Mode ' + $mode) })
        Check ($calls.Count -eq 1 -and $calls[0].Contains('-InstallDirectory "$INSTDIR"')) ('설치 단계의 경로 전달 누락: ' + $mode)
    }
    [void][IO.Directory]::CreateDirectory($InstallDirectory)
    Check (Same-Path 'C:\Tools\Chagok' 'c:\tools\chagok\') '경로의 대소문자·끝 구분자'
    Check (Same-Path 'C:/Tools/Chagok' 'c:\tools\chagok') '경로 구분자 차이'
    Check (In-Path 'C:\Tools\Chagok\nested' 'C:\Tools\Chagok') '하위 경로'
    Check (-not (In-Path 'C:\Tools\Chagok-other' 'C:\Tools\Chagok')) '비슷한 접두사 경로를 혼동함'
    Check ($null -eq (Read-State)) '빈 설치 상태'
    $state = [pscustomobject]@{ Owner = $owner; Directory = $InstallDirectory; Version = '0.10.0'; PathOwned = $false; RegisterPath = $false; Pending = $true }
    Save-State $state
    $read = Read-State
    Check (-not $read.PathOwned -and -not $read.RegisterPath -and $read.Pending) '새 상태는 PATH를 소유하지 않아야 함'
    $state.PathOwned = $true
    $state.RegisterPath = $true
    $state.Pending = $false
    Save-State $state
    $read = Read-State
    Check ($read.PathOwned -and $read.RegisterPath -and -not $read.Pending) '설치 상태 원자적 갱신'
    Check (-not [IO.File]::Exists($stateFile + '.tmp')) '설치 상태 임시 파일 정리'
    $state.Owner = 'another-app'
    Save-State $state
    Must-Fail { Read-State }
    $state.Owner = $owner
    $state.Directory = Join-Path $testRoot 'other'
    Save-State $state
    Must-Fail { Read-State }
    foreach ($directory in $dataDirectories) {
        [void][IO.Directory]::CreateDirectory($directory)
        [IO.File]::WriteAllText((Join-Path $directory 'settings.json'), '{"Version":1}')
        [IO.File]::WriteAllText((Join-Path $directory 'history.jsonl'), '기록 원문')
        [IO.File]::WriteAllText((Join-Path $directory 'unknown.txt'), '사용자 파일')
    }
    $busy = [IO.File]::Open((Join-Path $dataDirectories[0] 'session.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try { Must-Fail { Acquire-DataLocks } } finally { $busy.Dispose() }
    Acquire-DataLocks
    Check ($locks.Count -eq 2) '새/구버전의 세션을 함께 잠가야 함'
    $planned = @(Prepare-DataRemoval)
    Check ($planned.Count -eq 4) '새/구버전 설정·기록만 선택'
    Check (@($planned | Where-Object { $_.EndsWith('unknown.txt') }).Count -eq 0) '알 수 없는 파일은 삭제 대상이 아님'
    foreach ($path in $planned) { [IO.File]::Delete($path) }
    foreach ($lease in $locks) { $lease.Dispose() }
    $locks.Clear()
    foreach ($directory in $dataDirectories) {
        Check (-not [IO.File]::Exists((Join-Path $directory 'settings.json'))) '선택한 설정 삭제'
        Check ([IO.File]::ReadAllText((Join-Path $directory 'unknown.txt')) -eq '사용자 파일') '다른 파일 보존'
    }
    [void][IO.Directory]::CreateDirectory((Join-Path $dataDirectories[0] 'settings.json'))
    Must-Fail { Prepare-DataRemoval }
    [IO.Directory]::Delete((Join-Path $dataDirectories[0] 'settings.json'))
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $original = Join-Path $testRoot 'original.txt'
        [IO.File]::WriteAllText($original, '보존')
        [void][IO.File]::CreateSymbolicLink((Join-Path $dataDirectories[0] 'settings.json'), $original)
        Must-Fail { Prepare-DataRemoval }
        Check ([IO.File]::ReadAllText($original) -eq '보존') '링크 대상의 원본 보존'
        $dangling = Join-Path $testRoot 'dangling'
        [void][IO.File]::CreateSymbolicLink($dangling, (Join-Path $testRoot 'missing'))
        Must-Fail { Assert-NoLinks $dangling }
    }
    [pscustomobject]@{ checksPassed = $checks; syntaxParsed = $true; source = $source; registryExecuted = $false } | ConvertTo-Json -Compress
} finally {
    foreach ($lease in $locks) { $lease.Dispose() }
    if ([IO.Directory]::Exists($testRoot)) { [IO.Directory]::Delete($testRoot, $true) }
}
