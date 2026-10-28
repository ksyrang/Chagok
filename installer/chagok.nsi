Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"
!include "x64.nsh"
!ifndef VERSION
  !error "VERSION이 필요합니다. scripts/package-windows.py를 사용하세요."
!endif
!ifndef PAYLOAD
  !error "PAYLOAD가 필요합니다."
!endif
!ifndef OUTPUT
  !error "OUTPUT이 필요합니다."
!endif

!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Chagok"
!define OWNER "Chagok.UserInstaller.v1"
Name "차곡 ${VERSION}"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\Chagok"
InstallDirRegKey HKCU "${REGKEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
AllowSkipFiles off
BrandingText "Chagok · by ksyrang"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1042 "ProductName" "차곡 설치 프로그램"
VIAddVersionKey /LANG=1042 "FileDescription" "Chagok 현재 사용자 설치"
VIAddVersionKey /LANG=1042 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1042 "LegalCopyright" "ksyrang"
Icon "${PAYLOAD}/chagok.ico"
UninstallIcon "${PAYLOAD}/chagok.ico"

Var AppGuard
Var RegisterPath
Var DesktopShortcut
Var DesktopCheckbox
Var PathCheckbox
Var PreserveData
Var PreserveCheckbox
Var Dialog

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_TEXT "차곡을 설치했습니다.$\r$\n$\r$\n시작 메뉴에서 차곡을 열 수 있습니다. PATH 등록을 선택했다면 터미널 앱을 완전히 종료한 뒤 다시 열어 chagok tui를 실행하세요."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom InstallOptions InstallOptionsLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.DataOptions un.DataOptionsLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "Korean"

!macro AcquireGuard
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\FileOrganizer-15193BE9-9D6D-4D58-9A90-23910F1544AE") p .r0 ?e'
  Pop $1
  StrCpy $AppGuard $0
  ${If} $0 == 0
  ${OrIf} $1 == 183
    MessageBox MB_OK|MB_ICONSTOP "차곡이 실행 중이거나 설치 잠금을 열 수 없습니다. 트레이의 차곡도 완전히 종료한 뒤 다시 실행하세요." /SD IDOK
    SetErrorLevel 3
    Abort
  ${EndIf}
!macroend
!macro ReleaseGuard
  ${If} $AppGuard != 0
    System::Call 'kernel32::CloseHandle(p $AppGuard)'
    StrCpy $AppGuard 0
  ${EndIf}
!macroend

Function .onInit
  ${IfNot} ${IsNativeAMD64}
    MessageBox MB_OK|MB_ICONSTOP "이 설치 프로그램은 x64 Windows용입니다." /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
  !insertmacro AcquireGuard
  StrCpy $RegisterPath 1
  ClearErrors
  ReadRegDWORD $0 HKCU "${REGKEY}" "RegisterPath"
  ${IfNot} ${Errors}
    StrCpy $RegisterPath $0
  ${EndIf}
  StrCpy $DesktopShortcut 1
  ClearErrors
  ReadRegDWORD $0 HKCU "${REGKEY}" "DesktopShortcut"
  ${IfNot} ${Errors}
    StrCpy $DesktopShortcut $0
  ${EndIf}
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/NODESKTOP" $1
  ${IfNot} ${Errors}
    StrCpy $DesktopShortcut 0
  ${EndIf}
  ClearErrors
  ${GetOptions} $0 "/NOPATH" $1
  ${IfNot} ${Errors}
    StrCpy $RegisterPath 0
  ${EndIf}
  InitPluginsDir
  File /oname=$PLUGINSDIR\support.ps1 "support.ps1"
FunctionEnd

Function InstallOptions
  !insertmacro MUI_HEADER_TEXT "설치 옵션" "바로가기 추가와 터미널 명령 등록 여부를 선택하세요."
  nsDialogs::Create 1018
  Pop $Dialog
  ${NSD_CreateCheckbox} 0 8u 100% 20u "바탕화면에 차곡 바로가기 추가(&D)"
  Pop $DesktopCheckbox
  ${NSD_SetState} $DesktopCheckbox $DesktopShortcut
  ${NSD_CreateCheckbox} 0 38u 100% 28u "터미널에서 chagok 명령 사용 (사용자 PATH 등록)"
  Pop $PathCheckbox
  ${NSD_SetState} $PathCheckbox $RegisterPath
  ${NSD_CreateLabel} 0 74u 100% 64u "등록하면 어느 폴더에서든 chagok tui를 실행할 수 있습니다.$\r$\n변경은 터미널 앱을 완전히 종료한 뒤 다시 열면 적용됩니다.$\r$\n시스템 PATH와 다른 프로그램의 항목은 변경하지 않습니다.$\r$\n해제하면 설치 프로그램이 추가했던 항목만 제거합니다."
  Pop $0
  nsDialogs::Show
FunctionEnd
Function InstallOptionsLeave
  ${NSD_GetState} $DesktopCheckbox $DesktopShortcut
  ${NSD_GetState} $PathCheckbox $RegisterPath
FunctionEnd

Section "차곡 설치"
  nsExec::ExecToStack '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\support.ps1" -Mode Prepare -InstallDirectory "$INSTDIR" -Version "${VERSION}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "설치 준비 실패 (종료 코드: $0)"
    DetailPrint "$1"
    MessageBox MB_OK|MB_ICONSTOP "설치를 준비하지 못했습니다.$\r$\n$1" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  SetOutPath "$INSTDIR"
  File "support.ps1"
  ClearErrors
  WriteUninstaller "$INSTDIR\uninstall.exe"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "제거 프로그램을 만들지 못했습니다. 쓰기 권한을 확인한 뒤 같은 위치에 다시 설치하세요." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  File "${PAYLOAD}/Chagok.exe"
  File "${PAYLOAD}/LICENSE"
  File "${PAYLOAD}/README.md"
  File "${PAYLOAD}/terminal-guide.md"
  File "${PAYLOAD}/ShellExtension-LICENSES.txt"
  StrCpy $2 ""
  ${If} $RegisterPath == 1
    StrCpy $2 "-RegisterPath"
  ${EndIf}
  ${If} $DesktopShortcut == 1
    StrCpy $2 "$2 -DesktopShortcut"
  ${EndIf}
  nsExec::ExecToStack '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\support.ps1" -Mode Configure -InstallDirectory "$INSTDIR" -Version "${VERSION}" $2'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "설치 설정 실패 (종료 코드: $0)"
    DetailPrint "$1"
    MessageBox MB_OK|MB_ICONSTOP "설정하지 못했습니다. 같은 위치에 다시 설치해 복구할 수 있습니다.$\r$\n$1" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  ClearErrors
  WriteRegStr HKCU "${REGKEY}" "ChagokOwner" "${OWNER}"
  WriteRegStr HKCU "${REGKEY}" "DisplayName" "차곡 (Chagok)"
  WriteRegStr HKCU "${REGKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${REGKEY}" "Publisher" "ksyrang"
  WriteRegStr HKCU "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REGKEY}" "DisplayIcon" "$INSTDIR\Chagok.exe"
  WriteRegStr HKCU "${REGKEY}" "UninstallString" '$\"$INSTDIR\uninstall.exe$\"'
  WriteRegStr HKCU "${REGKEY}" "QuietUninstallString" '$\"$INSTDIR\uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${REGKEY}" "RegisterPath" $RegisterPath
  WriteRegDWORD HKCU "${REGKEY}" "DesktopShortcut" $DesktopShortcut
  WriteRegDWORD HKCU "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REGKEY}" "NoRepair" 1
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "Windows 앱 목록에 등록하지 못했습니다. 권한을 확인한 뒤 같은 위치에 다시 설치하세요." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  SetErrorLevel 0
SectionEnd

Function .onInstSuccess
  !insertmacro ReleaseGuard
FunctionEnd
Function .onGUIEnd
  !insertmacro ReleaseGuard
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext current
  !insertmacro AcquireGuard
  StrCpy $PreserveData 1
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/REMOVEDATA" $1
  ${IfNot} ${Errors}
    StrCpy $PreserveData 0
  ${EndIf}
FunctionEnd
Function un.DataOptions
  !insertmacro MUI_HEADER_TEXT "설정과 실행 기록" "프로그램을 제거한 뒤 데이터를 남길지 선택하세요."
  nsDialogs::Create 1018
  Pop $Dialog
  ${NSD_CreateCheckbox} 0 12u 100% 25u "설정과 실행 기록 보존 (권장)"
  Pop $PreserveCheckbox
  ${NSD_SetState} $PreserveCheckbox $PreserveData
  ${NSD_CreateLabel} 0 45u 100% 85u "해제하면 Chagok과 구버전 FileOrganizer의 설정·기록·백업을 삭제합니다.$\r$\n$\r$\n정리했던 파일, 별도 --data-dir의 데이터, 알 수 없는 파일은 삭제하지 않습니다. 다른 프로그램이 등록한 PATH 항목도 보존합니다."
  Pop $0
  nsDialogs::Show
FunctionEnd
Function un.DataOptionsLeave
  ${NSD_GetState} $PreserveCheckbox $PreserveData
  ${If} $PreserveData == 0
    MessageBox MB_YESNO|MB_ICONEXCLAMATION|MB_DEFBUTTON2 "새/구버전의 설정·실행 기록·백업을 삭제할까요? 삭제한 데이터는 복구할 수 없습니다." IDYES confirmed
    Abort
    confirmed:
  ${EndIf}
FunctionEnd
Section "Uninstall"
  StrCpy $2 ""
  ${If} $PreserveData == 0
    StrCpy $2 "-RemoveData"
  ${EndIf}
  nsExec::ExecToStack '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\support.ps1" -Mode Uninstall -InstallDirectory "$INSTDIR" $2'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "제거 준비 실패 (종료 코드: $0)"
    DetailPrint "$1"
    MessageBox MB_OK|MB_ICONSTOP "제거를 중단했습니다. 문제를 해결한 뒤 다시 실행하세요.$\r$\n$1" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  ClearErrors
  Delete "$INSTDIR\Chagok.exe"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "실행 파일을 지울 수 없습니다. 차곡을 완전히 종료한 뒤 다시 제거하세요." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
  ClearErrors
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\terminal-guide.md"
  Delete "$INSTDIR\ShellExtension-LICENSES.txt"
  Delete "$INSTDIR\chagok-install.json.tmp"
  ${If} ${Errors}
    Goto uninstall_failed
  ${EndIf}
  Delete "$INSTDIR\support.ps1"
  ${If} ${Errors}
    Goto uninstall_failed
  ${EndIf}
  Delete "$INSTDIR\uninstall.exe"
  ${If} ${Errors}
    Goto uninstall_failed
  ${EndIf}
  Delete "$INSTDIR\chagok-install.json"
  ${If} ${Errors}
    Goto uninstall_failed
  ${EndIf}
  ClearErrors
  ReadRegStr $0 HKCU "${REGKEY}" "ChagokOwner"
  ${IfNot} ${Errors}
  ${AndIf} $0 == "${OWNER}"
    DeleteRegKey HKCU "${REGKEY}"
    ${If} ${Errors}
      Goto uninstall_failed
    ${EndIf}
  ${EndIf}
  ; 알 수 없는 사용자 파일이 있으면 폴더를 남긴다. 재귀 삭제하지 않는다.
  RMDir "$INSTDIR"
  SetErrorLevel 0
  Goto uninstall_done
  uninstall_failed:
    MessageBox MB_OK|MB_ICONSTOP "일부 설치 파일이나 등록 정보를 지우지 못했습니다. 파일 잠금·권한을 확인하세요. 필요하면 같은 설치 프로그램으로 복구한 뒤 다시 제거하세요." /SD IDOK
    SetErrorLevel 1
    Abort
  uninstall_done:
SectionEnd
Function un.onUninstSuccess
  !insertmacro ReleaseGuard
FunctionEnd
Function un.onGUIEnd
  !insertmacro ReleaseGuard
FunctionEnd
