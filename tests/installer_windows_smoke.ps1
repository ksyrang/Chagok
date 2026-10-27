# 전용 Windows 테스트 계정 또는 Windows Sandbox에서만 명시적으로 실행한다.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Installer, [switch]$AllowProfileChanges)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $AllowProfileChanges -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows 전용 테스트 프로필에서 -AllowProfileChanges를 명시하세요. 현재 사용자의 PATH와 임시 데이터 폴더를 변경합니다.'
}
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$expectedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($Installer).FileVersion
$registry = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Chagok'
$dataRoot = [Environment]::GetFolderPath('LocalApplicationData')
$dataDirectories = @((Join-Path $dataRoot 'Chagok'), (Join-Path $dataRoot 'FileOrganizer'))
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Chagok'
foreach ($path in @($dataDirectories) + @($menu)) {
    if (Test-Path -LiteralPath $path) { throw "기존 데이터를 보존하기 위해 검사를 중단했습니다: $path" }
}
$existing = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registry)
if ($existing) { $existing.Dispose(); throw '기존 차곡 설치가 없는 전용 테스트 계정이 필요합니다.' }
$environment = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
$before = $environment.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
$beforeKind = if ($null -ne $before) { $environment.GetValueKind('Path') } else { [Microsoft.Win32.RegistryValueKind]::ExpandString }
$beforeProcess = $env:PATH
$baseline = '%SystemRoot%\System32;C:\Chagok-test-sentinel'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Chagok 설치 검증 ' + [guid]::NewGuid().ToString('N'))
$checks = 0
function Check([bool]$value, [string]$message) {
    if (-not $value) { throw $message }
    $script:checks++
}
function User-Path { return $environment.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
function Setup([string]$directory, [string]$options = '', [int]$code = 0) {
    # NSIS의 /D는 마지막에 두고 경로 전체에 따옴표를 붙이지 않는다.
    $process = Start-Process -FilePath $Installer -ArgumentList "/S $options /D=$directory" -Wait -PassThru
    Check ($process.ExitCode -eq $code) "설치 종료 코드: $($process.ExitCode), 기대: $code"
}
function Uninstall([string]$directory, [bool]$remove = $false) {
    $options = if ($remove) { '/S /REMOVEDATA' } else { '/S' }
    $null = Start-Process -FilePath (Join-Path $directory 'uninstall.exe') -ArgumentList $options -Wait -PassThru
    Check (-not [IO.File]::Exists((Join-Path $directory 'Chagok.exe'))) '프로그램 제거 실패'
    Check (-not [IO.File]::Exists((Join-Path $directory 'chagok-install.json'))) '설치 정보 제거 실패'
}
try {
    $environment.SetValue('Path', $baseline, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    $blocked = Join-Path $testRoot '실행 중 차단'
    $mutex = New-Object System.Threading.Mutex($false, 'Local\FileOrganizer-15193BE9-9D6D-4D58-9A90-23910F1544AE')
    try { Setup $blocked '' 3; Check (-not [IO.Directory]::Exists($blocked)) '앱 실행 중 설치가 파일을 생성함' }
    finally { $mutex.Dispose() }
    $directory = Join-Path $testRoot 'PATH 기본 등록'
    Setup $directory
    $state = Get-Content -Raw -LiteralPath (Join-Path $directory 'chagok-install.json') | ConvertFrom-Json
    Check ($state.PathOwned -and $state.RegisterPath) '기본 PATH 등록'
    Check ((User-Path) -eq ($baseline + ';' + $directory)) '다른 PATH 항목과 환경 변수 보존'
    Check ($environment.GetValueKind('Path') -eq [Microsoft.Win32.RegistryValueKind]::ExpandString) 'PATH 값 형식 보존'
    $env:PATH = [Environment]::ExpandEnvironmentVariables([Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + (User-Path))
    Push-Location $testRoot
    try {
        $command = Get-Command chagok -CommandType Application
        Check ($command.Path -eq (Join-Path $directory 'Chagok.exe')) '다른 현재 폴더에서 명령 검색'
        $version = @(& $command.Path version --json) -join "`n" | ConvertFrom-Json
        Check ($version.version -eq $expectedVersion) '설치 EXE 버전'
    } finally { Pop-Location }
    Setup $directory '/NOPATH'
    Check ((User-Path) -eq $baseline) '같은 위치 재설치에서 PATH 선택 해제'
    Setup $directory
    Check ((User-Path) -eq $baseline) '이전 PATH 선택 유지'
    foreach ($data in $dataDirectories) {
        [void][IO.Directory]::CreateDirectory($data)
        [IO.File]::WriteAllText((Join-Path $data 'settings.json'), '{"Version":1}')
        [IO.File]::WriteAllText((Join-Path $data 'history.jsonl'), '기록 보존')
        [IO.File]::WriteAllText((Join-Path $data 'unknown.txt'), '다른 파일')
    }
    [IO.File]::WriteAllText((Join-Path $directory 'unknown.txt'), '설치 폴더 사용자 파일')
    Uninstall $directory
    Check ([IO.File]::ReadAllText((Join-Path $directory 'unknown.txt')) -eq '설치 폴더 사용자 파일') '설치 폴더 다른 파일 보존'
    foreach ($data in $dataDirectories) {
        Check ([IO.File]::ReadAllText((Join-Path $data 'history.jsonl')) -eq '기록 보존') '기본 제거는 데이터 보존'
    }
    $noPath = Join-Path $testRoot 'PATH 등록 안 함'
    Setup $noPath '/NOPATH'
    Check ((User-Path) -eq $baseline) '최초 설치 PATH 해제'
    Uninstall $noPath $true
    foreach ($data in $dataDirectories) {
        Check (-not [IO.File]::Exists((Join-Path $data 'settings.json'))) '명시한 새/구 설정 삭제'
        Check (-not [IO.File]::Exists((Join-Path $data 'history.jsonl'))) '명시한 새/구 기록 삭제'
        Check ([IO.File]::ReadAllText((Join-Path $data 'unknown.txt')) -eq '다른 파일') '기타 데이터 파일 보존'
    }
    $manual = Join-Path $testRoot '사용자가 등록한 PATH'
    $manualValue = $baseline + ';' + $manual
    $environment.SetValue('Path', $manualValue, [Microsoft.Win32.RegistryValueKind]::String)
    Setup $manual
    $state = Get-Content -Raw -LiteralPath (Join-Path $manual 'chagok-install.json') | ConvertFrom-Json
    Check (-not $state.PathOwned -and $state.RegisterPath) '사용자 PATH 소유권을 가져오면 안 됨'
    Uninstall $manual
    Check ((User-Path) -eq $manualValue) '수동 등록 PATH는 제거 후 보존'
    Check ($environment.GetValueKind('Path') -eq [Microsoft.Win32.RegistryValueKind]::String) 'REG_SZ 원문 형식 보존'
    [pscustomobject]@{ checksPassed = $checks; pathChoices = $true; dataChoices = $true; unicodeDirectory = $true; unrelatedFilesPreserved = $true } | ConvertTo-Json -Compress
} finally {
    if ($null -eq $before) { $environment.DeleteValue('Path', $false) }
    else { $environment.SetValue('Path', $before, $beforeKind) }
    $environment.Dispose()
    $env:PATH = $beforeProcess
    # 기존 데이터가 없음을 확인한 테스트 프로필의 생성물만 정리한다.
    foreach ($path in @($dataDirectories) + @($menu, $testRoot)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registry, $false)
}
