; Installeur Windows de SonNuage (NSIS), construit sous Linux par makensis depuis build/package.sh :
;   makensis -DVERSION=1.2.3 -DNUMERIC=1.2.3.0 -DSOURCE=<dossier publié> -DOUTFILE=<setup.exe> installer.nsi
; Installation pour l'utilisateur courant, sans droits d'administration, dans %LOCALAPPDATA%\Programs\SonNuage.
; Mises à jour lancées par l'appli : « setup.exe /S /relaunch » attend qu'elle se ferme, remplace les fichiers et la
; relance. Les données (%APPDATA%\SonNuage : comptes, réglages) ne sont jamais touchées.

Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define APP "SonNuage"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\SonNuage"

Name "${APP}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP}"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails nevershow

VIProductVersion "${NUMERIC}"
VIAddVersionKey /LANG=0 "ProductName" "SonNuage"
VIAddVersionKey /LANG=0 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "FileDescription" "Installeur de SonNuage"
VIAddVersionKey /LANG=0 "LegalCopyright" "AGPL-3.0-or-later"

!define MUI_ICON "${SOURCE}/wwwroot/icons/sonnuage.ico"
!define MUI_UNICON "${SOURCE}/wwwroot/icons/sonnuage.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\SonNuage.exe"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "English"

LangString CloseApp ${LANG_FRENCH} "SonNuage est encore ouvert. Quittez-le (icône de la barre système → Quitter), puis Réessayer."
LangString CloseApp ${LANG_ENGLISH} "SonNuage is still running. Quit it (tray icon → Quit), then Retry."

; L'appli tient %APPDATA%\SonNuage\instance.lock ouvert sans partage tant qu'elle tourne (instance unique) :
; on attend qu'il se libère (20 s), puis on demande de la fermer (en silencieux : abandon).
!macro WaitForApp
  StrCpy $R1 0
  ${Do}
    ${IfNot} ${FileExists} "$APPDATA\SonNuage\instance.lock"
      ${ExitDo}
    ${EndIf}
    ClearErrors
    FileOpen $R0 "$APPDATA\SonNuage\instance.lock" a
    ${IfNot} ${Errors}
      FileClose $R0
      ${ExitDo}
    ${EndIf}
    IntOp $R1 $R1 + 1
    ${If} $R1 >= 40
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(CloseApp)" /SD IDCANCEL IDRETRY +2
      Abort
      StrCpy $R1 0
    ${EndIf}
    Sleep 500
  ${Loop}
!macroend

Section "SonNuage"
  !insertmacro WaitForApp
  SetOutPath "$INSTDIR"
  ; Pages de la version précédente retirées : un fichier disparu ne doit pas rester.
  RMDir /r "$INSTDIR\wwwroot"
  File /r "${SOURCE}/*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortCut "$SMPROGRAMS\SonNuage.lnk" "$INSTDIR\SonNuage.exe"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "SonNuage"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "SonNuage"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\SonNuage.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0
SectionEnd

; Mise à jour depuis l'appli : elle s'est arrêtée pour laisser remplacer ses fichiers, on la relance.
Function .onInstSuccess
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/relaunch" $1
  ${IfNot} ${Errors}
    Exec '"$INSTDIR\SonNuage.exe"'
  ${EndIf}
FunctionEnd

Section "Uninstall"
  !insertmacro WaitForApp
  Delete "$SMPROGRAMS\SonNuage.lnk"
  ; Lancement avec la session (réglage de l'appli).
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "SonNuage"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  RMDir /r "$INSTDIR"
SectionEnd
