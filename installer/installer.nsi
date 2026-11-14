; Acer Battery Limiter — installer / uninstaller (NSIS 3, builds on macOS/Linux with makensis)
; Build: ./build.sh   (or: makensis -DVERSION=1.0.0 installer/installer.nsi)

Unicode true
SetCompressor /SOLID lzma

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!define APPNAME "Acer Battery Limiter"
!define EXENAME "AcerBatteryLimiter.exe"
!define SVCNAME "AcerBatteryLimiter"
!define REGUNINST "Software\Microsoft\Windows\CurrentVersion\Uninstall\AcerBatteryLimiter"
!define REGRUN "Software\Microsoft\Windows\CurrentVersion\Run"
!define BUILDDIR "..\src\AcerBatteryLimiter\bin\Release\net48"

Name "${APPNAME}"
OutFile "..\dist\AcerBatteryLimiter-Setup-${VERSION}.exe"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKLM "${REGUNINST}" "InstallLocation"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "MIT License"

!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

!define MUI_ICON "..\assets\app.ico"
!define MUI_UNICON "..\assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "This will install ${APPNAME} ${VERSION}.$\r$\n$\r$\nIt keeps the battery of your Acer laptop at the level you choose (Acer's built-in 80% stop, or any level from 0 to 100), and can discharge it while the charger stays connected.$\r$\n$\r$\nA small background service applies the setting, and a tray icon shows the battery status.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Open ${APPNAME}"
!define MUI_FINISHPAGE_RUN_FUNCTION OpenAppAsUser

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APPNAME} needs 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

; Start the app as the logged-in user (not elevated), via Explorer
Function OpenAppAsUser
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXENAME}"'
FunctionEnd

!macro StopEverything
  ; tray / window of every user, then the service (the service switches discharge off when it stops)
  nsExec::ExecToLog 'taskkill /F /IM ${EXENAME}'
  nsExec::ExecToLog 'sc stop ${SVCNAME}'
  Sleep 3000
  nsExec::ExecToLog 'taskkill /F /IM ${EXENAME}'
!macroend

Section "Install"
  SetShellVarContext all
  !insertmacro StopEverything

  ; replace the older helper tasks (BatteryLimit80 / BatteryDischarge) if present
  nsExec::ExecToLog 'schtasks /Delete /TN "BatteryDischarge" /F'
  nsExec::ExecToLog 'schtasks /Delete /TN "BatteryLimit80" /F'
  RMDir /r "$APPDATA\BatteryLimit80"

  SetOutPath "$INSTDIR"
  File "${BUILDDIR}\${EXENAME}"
  File "${BUILDDIR}\${EXENAME}.config"
  File "..\LICENSE"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; settings folder: the window (normal user) writes config.json, the service reads it
  CreateDirectory "$APPDATA\AcerBatteryLimiter"
  nsExec::ExecToLog 'icacls "$APPDATA\AcerBatteryLimiter" /grant *S-1-5-32-545:(OI)(CI)M /T /Q'

  ; Windows service (LocalSystem, automatic start, restart on failure)
  nsExec::ExecToStack 'sc query ${SVCNAME}'
  Pop $0
  Pop $1
  ${If} $0 == 0
    nsExec::ExecToLog 'sc config ${SVCNAME} binPath= "\"$INSTDIR\${EXENAME}\" --service" start= auto DisplayName= "${APPNAME}"'
  ${Else}
    nsExec::ExecToLog 'sc create ${SVCNAME} binPath= "\"$INSTDIR\${EXENAME}\" --service" start= auto DisplayName= "${APPNAME}"'
  ${EndIf}
  nsExec::ExecToLog 'sc description ${SVCNAME} "Keeps the Acer laptop battery at the level set in ${APPNAME}."'
  nsExec::ExecToLog 'sc failure ${SVCNAME} reset= 86400 actions= restart/5000/restart/5000/restart/30000'
  nsExec::ExecToLog 'sc start ${SVCNAME}'

  ; tray icon for every user at sign-in
  WriteRegStr HKLM "${REGRUN}" "AcerBatteryLimiterTray" '"$INSTDIR\${EXENAME}" --tray'

  ; shortcuts (all users)
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0
  CreateShortCut "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

  ; Apps & features entry
  WriteRegStr HKLM "${REGUNINST}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${REGUNINST}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${REGUNINST}" "Publisher" "${APPNAME}"
  WriteRegStr HKLM "${REGUNINST}" "DisplayIcon" "$INSTDIR\${EXENAME},0"
  WriteRegStr HKLM "${REGUNINST}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${REGUNINST}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${REGUNINST}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${REGUNINST}" "NoModify" 1
  WriteRegDWORD HKLM "${REGUNINST}" "NoRepair" 1
  WriteRegDWORD HKLM "${REGUNINST}" "EstimatedSize" 200
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  !insertmacro StopEverything
  ; make sure the battery is not left discharging, then remove the service
  nsExec::ExecToLog '"$INSTDIR\${EXENAME}" --stop-discharge'
  nsExec::ExecToLog 'sc delete ${SVCNAME}'

  DeleteRegValue HKLM "${REGRUN}" "AcerBatteryLimiterTray"
  DeleteRegKey HKLM "${REGUNINST}"

  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  RMDir /r "$APPDATA\AcerBatteryLimiter"

  Delete "$INSTDIR\${EXENAME}"
  Delete "$INSTDIR\${EXENAME}.config"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
