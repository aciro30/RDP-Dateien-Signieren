# RDP-Signierer

Dieses Verzeichnis enthält jetzt zwei Varianten:

- `Sign-RdpFiles.ps1`: das ursprüngliche PowerShell-Script
- `RdpSignTool.WinForms`: die Windows-GUI, die als einzelne `exe` veröffentlicht werden kann

## Funktionen der GUI

- Live-Log auf der linken Seite
- Zertifikatsauswahl und Zertifikatserzeugung
- Zertifikat-Export als `.cer`
- Registry-Option zum Setzen/Entfernen von `RedirectionWarningDialogVersion=1` mit UAC-Abfrage bei Bedarf
- Drag-and-Drop für `.rdp`-Dateien
- Dateiauswahl per `Durchsuchen`
- sichtbare Statusanzeige `Datei signiert / nicht signiert`
- Testlauf mit `rdpsign /l`
- Bestätigung vor dem echten Signieren
- eigenes App-Icon und umbenannter Ausgabename `RdpSignierer.exe`

## Windows-EXE erzeugen

Im Projektordner kann die Anwendung als einzelne Windows-Datei veröffentlicht werden:

```bash
dotnet publish RdpSignTool.WinForms/RdpSignTool.WinForms.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:PublishTrimmed=false \
  -o publish/win-x64
```

Danach liegt die ausführbare Datei im Ordner `publish/win-x64`, normalerweise als `RdpSignierer.exe`.
