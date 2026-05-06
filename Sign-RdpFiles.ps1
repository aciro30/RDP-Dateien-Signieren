#requires -version 5.1
<#
    RDP-Dateien mit einem lokalen selbstsignierten Code-Signing-Zertifikat signieren

    Was das Skript macht:
    1. Sucht ein vorhandenes Zertifikat aus der Konfiguration oder erzeugt ein neues Code-Signing-Zertifikat
    2. Stellt Vertrauen für den aktuellen Benutzer her:
       - Cert:\CurrentUser\TrustedPublisher
       - Cert:\CurrentUser\Root
    3. Lässt .rdp-Dateien auswählen
    4. Führt erst einen Testlauf mit rdpsign /l aus
    5. Signiert danach die Dateien mit rdpsign (/sha256 bevorzugt, sonst /sha1)
    6. Speichert den Thumbprint in %AppData%\RdpSignTool\config.json

    Hinweise:
    - Das Zertifikat wird standardmäßig im Store Cert:\CurrentUser\My angelegt.
    - Das Skript arbeitet absichtlich nur im CurrentUser-Kontext, damit keine Admin-Rechte nötig sind.
    - Wenn der Zielrechner ein anderes Benutzerkonto verwendet, muss dort ebenfalls Vertrauen in das Zertifikat bestehen.
#>

Add-Type -AssemblyName System.Windows.Forms | Out-Null

$ErrorActionPreference = 'Stop'

# ----------------------------
# Konfiguration / Pfade
# ----------------------------
$AppDir     = Join-Path $env:APPDATA 'RdpSignTool'
$ConfigPath = Join-Path $AppDir 'config.json'
$ExportDir  = Join-Path $AppDir 'certs'

if (-not (Test-Path $AppDir))   { New-Item -ItemType Directory -Path $AppDir   -Force | Out-Null }
if (-not (Test-Path $ExportDir)) { New-Item -ItemType Directory -Path $ExportDir -Force | Out-Null }

# ----------------------------
# Hilfsfunktionen
# ----------------------------

function Write-Info($msg)  { Write-Host "[INFO]  $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "[OK]    $msg" -ForegroundColor Green }
function Write-Warn($msg)  { Write-Host "[WARN]  $msg" -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host "[ERROR] $msg" -ForegroundColor Red }

function Get-RdpSignExe {
    $cmd = Get-Command rdpsign.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path $env:SystemRoot 'System32\rdpsign.exe'),
        (Join-Path $env:SystemRoot 'Sysnative\rdpsign.exe')
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }

    return $null
}

function Load-Config {
    if (Test-Path $ConfigPath) {
        try {
            return Get-Content $ConfigPath -Raw | ConvertFrom-Json
        } catch {
            Write-Warn "Konfigurationsdatei konnte nicht gelesen werden. Sie wird neu erstellt."
        }
    }

    return [PSCustomObject]@{
        Thumbprint = $null
        Subject    = $null
        CreatedAt  = $null
    }
}

function Save-Config([string]$Thumbprint, [string]$Subject) {
    $cfg = [PSCustomObject]@{
        Thumbprint = $Thumbprint
        Subject    = $Subject
        CreatedAt  = (Get-Date).ToString('s')
    }
    $cfg | ConvertTo-Json | Set-Content -Path $ConfigPath -Encoding UTF8
}

function Normalize-Thumbprint([string]$Thumbprint) {
    return ($Thumbprint -replace '\s','').ToUpperInvariant()
}

function Get-CertByThumbprint([string]$Thumbprint) {
    if (-not $Thumbprint) { return $null }
    $tp = Normalize-Thumbprint $Thumbprint

    $stores = @(
        'Cert:\CurrentUser\My',
        'Cert:\LocalMachine\My'
    )

    foreach ($store in $stores) {
        if (Test-Path $store) {
            $cert = Get-ChildItem $store -ErrorAction SilentlyContinue |
                Where-Object { (Normalize-Thumbprint $_.Thumbprint) -eq $tp } |
                Select-Object -First 1
            if ($cert) { return $cert }
        }
    }

    return $null
}

function Test-IsCodeSigningCandidate($cert) {
    if (-not $cert) { return $false }
    if (-not $cert.HasPrivateKey) { return $false }
    if ($cert.NotAfter -le (Get-Date)) { return $false }

    # Enhanced Key Usage prüfen
    foreach ($eku in $cert.EnhancedKeyUsageList) {
        if ($eku.ObjectId -eq '1.3.6.1.5.5.7.3.3' -or $eku.FriendlyName -match 'Code') {
            return $true
        }
    }

    return $false
}

function Get-ExistingCodeSigningCerts {
    $stores = @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')
    $all = @()

    foreach ($store in $stores) {
        if (Test-Path $store) {
            $all += Get-ChildItem $store -ErrorAction SilentlyContinue
        }
    }

    return $all |
        Where-Object { Test-IsCodeSigningCandidate $_ } |
        Sort-Object NotAfter -Descending
}

function New-RdpCodeSigningCert {
    param(
        [string]$Subject = 'CN=RDP File Signing'
    )

    Write-Info "Erzeuge neues selbstsigniertes Code-Signing-Zertifikat in Cert:\CurrentUser\My ..."

    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(5)

    return $cert
}

function Ensure-CertificateTrust {
    param(
        [Parameter(Mandatory)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    $cerFile = Join-Path $ExportDir ("{0}.cer" -f (Normalize-Thumbprint $Certificate.Thumbprint))

    Write-Info "Exportiere öffentliches Zertifikat nach: $cerFile"
    Export-Certificate -Cert $Certificate -FilePath $cerFile -Force | Out-Null

    $trustedPublisherStore = 'Cert:\CurrentUser\TrustedPublisher'
    $rootStore             = 'Cert:\CurrentUser\Root'

    $tp = Normalize-Thumbprint $Certificate.Thumbprint

    $alreadyInTP = Get-ChildItem $trustedPublisherStore -ErrorAction SilentlyContinue |
        Where-Object { (Normalize-Thumbprint $_.Thumbprint) -eq $tp } |
        Select-Object -First 1

    if (-not $alreadyInTP) {
        Import-Certificate -FilePath $cerFile -CertStoreLocation $trustedPublisherStore | Out-Null
        Write-Ok "Zertifikat in 'Trusted Publishers' importiert."
    } else {
        Write-Info "Zertifikat ist bereits in 'Trusted Publishers' vorhanden."
    }

    $alreadyInRoot = Get-ChildItem $rootStore -ErrorAction SilentlyContinue |
        Where-Object { (Normalize-Thumbprint $_.Thumbprint) -eq $tp } |
        Select-Object -First 1

    if (-not $alreadyInRoot) {
        Import-Certificate -FilePath $cerFile -CertStoreLocation $rootStore | Out-Null
        Write-Ok "Zertifikat in 'Trusted Root Certification Authorities' importiert."
    } else {
        Write-Info "Zertifikat ist bereits in 'Trusted Root Certification Authorities' vorhanden."
    }
}

function Select-RdpFiles {
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = 'Eine oder mehrere RDP-Dateien auswählen'
    $dialog.Filter = 'RDP-Dateien (*.rdp)|*.rdp|Alle Dateien (*.*)|*.*'
    $dialog.Multiselect = $true

    $result = $dialog.ShowDialog()
    if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
        return @()
    }

    return @($dialog.FileNames)
}

function Read-Choice([string]$Prompt, [string[]]$Allowed) {
    while ($true) {
        $value = Read-Host $Prompt
        if ($Allowed -contains $value) {
            return $value
        }
        Write-Warn "Ungültige Eingabe. Erlaubt: $($Allowed -join ', ')"
    }
}

function Choose-Certificate {
    $config = Load-Config

    if ($config.Thumbprint) {
        $configured = Get-CertByThumbprint $config.Thumbprint
        if ($configured -and (Test-IsCodeSigningCandidate $configured)) {
            Write-Info "Gespeichertes Zertifikat gefunden: $($configured.Subject)"
            Write-Host "  Thumbprint : $($configured.Thumbprint)"
            Write-Host "  Gültig bis : $($configured.NotAfter)"
            $reuse = Read-Choice "Dieses Zertifikat verwenden? (J/N)" @('J','N','j','n')
            if ($reuse -match '^[Jj]$') {
                return $configured
            }
        } else {
            Write-Warn "Gespeichertes Zertifikat ist nicht mehr verwendbar."
        }
    }

    $existing = Get-ExistingCodeSigningCerts

    if ($existing.Count -gt 0) {
        Write-Info "Gefundene verwendbare Code-Signing-Zertifikate:"
        for ($i = 0; $i -lt $existing.Count; $i++) {
            $c = $existing[$i]
            Write-Host ("[{0}] {1}" -f ($i + 1), $c.Subject)
            Write-Host ("     Thumbprint : {0}" -f $c.Thumbprint)
            Write-Host ("     Store      : {0}" -f $c.PSParentPath)
            Write-Host ("     Gültig bis : {0}" -f $c.NotAfter)
        }
        Write-Host "[N] Neues Zertifikat erzeugen"

        while ($true) {
            $answer = Read-Host "Auswahl"
            if ($answer -match '^[Nn]$') {
                break
            }
            if ($answer -as [int]) {
                $index = [int]$answer
                if ($index -ge 1 -and $index -le $existing.Count) {
                    return $existing[$index - 1]
                }
            }
            Write-Warn "Ungültige Auswahl."
        }
    }

    $subjectInput = Read-Host "Betreff für neues Zertifikat (leer = CN=RDP File Signing)"
    if ([string]::IsNullOrWhiteSpace($subjectInput)) {
        $subjectInput = 'CN=RDP File Signing'
    } elseif ($subjectInput -notmatch '^CN=') {
        $subjectInput = "CN=$subjectInput"
    }

    return New-RdpCodeSigningCert -Subject $subjectInput
}

function Get-RdpSignHashMode {
    param(
        [Parameter(Mandatory)]
        [string]$RdpsignPath
    )

    try {
        $help = & $RdpsignPath /? 2>&1 | Out-String
        if ($help -match '/sha256') {
            return 'sha256'
        }
    } catch {
    }

    return 'sha1'
}

function Invoke-RdpSign {
    param(
        [Parameter(Mandatory)][string]$RdpsignPath,
        [Parameter(Mandatory)][string]$Mode,          # sha1 | sha256
        [Parameter(Mandatory)][string]$Thumbprint,
        [Parameter(Mandatory)][string[]]$Files,
        [switch]$TestOnly
    )

    $cleanThumbprint = Normalize-Thumbprint $Thumbprint

    foreach ($file in $Files) {
        if (-not (Test-Path $file)) {
            Write-Warn "Datei nicht gefunden: $file"
            continue
        }

        $args = @()
        if ($Mode -eq 'sha256') {
            $args += '/sha256'
        } else {
            $args += '/sha1'
        }

        $args += $cleanThumbprint

        if ($TestOnly) {
            $args += '/l'
        } else {
            $args += '/v'
        }

        $args += $file

        Write-Info ("rdpsign {0}" -f ($args -join ' '))

        & $RdpsignPath @args
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0) {
            if ($TestOnly) {
                Write-Ok "Test erfolgreich: $file"
            } else {
                Write-Ok "Signiert: $file"
            }
        } else {
            throw "rdpsign fehlgeschlagen für '$file' mit ExitCode $exitCode."
        }
    }
}

# ----------------------------
# Hauptablauf
# ----------------------------

Write-Host ""
Write-Host "=== RDP Sign Tool ===" -ForegroundColor Magenta
Write-Host ""

$rdpsign = Get-RdpSignExe
if (-not $rdpsign) {
    Write-Err "rdpsign.exe wurde nicht gefunden."
    Write-Host "Prüfe, ob Remotedesktop-Client/Windows-Komponenten korrekt installiert sind."
    exit 1
}

Write-Ok "rdpsign.exe gefunden: $rdpsign"

$hashMode = Get-RdpSignHashMode -RdpsignPath $rdpsign
Write-Info "Verwendeter Hash-Parameter: /$hashMode"

$cert = Choose-Certificate
if (-not $cert) {
    Write-Err "Kein Zertifikat verfügbar."
    exit 1
}

if (-not (Test-IsCodeSigningCandidate $cert)) {
    Write-Err "Das ausgewählte Zertifikat ist kein verwendbarer Code-Signing-Kandidat."
    exit 1
}

Write-Host ""
Write-Host "Ausgewähltes Zertifikat:" -ForegroundColor Yellow
Write-Host "  Subject    : $($cert.Subject)"
Write-Host "  Thumbprint : $($cert.Thumbprint)"
Write-Host "  NotAfter   : $($cert.NotAfter)"
Write-Host "  Store      : $($cert.PSParentPath)"
Write-Host ""

Ensure-CertificateTrust -Certificate $cert
Save-Config -Thumbprint $cert.Thumbprint -Subject $cert.Subject
Write-Ok "Konfiguration gespeichert: $ConfigPath"

$files = Select-RdpFiles
if (-not $files -or $files.Count -eq 0) {
    Write-Warn "Keine .rdp-Dateien ausgewählt."
    exit 0
}

Write-Host ""
Write-Info "Starte Testlauf mit rdpsign /l ..."
Invoke-RdpSign -RdpsignPath $rdpsign -Mode $hashMode -Thumbprint $cert.Thumbprint -Files $files -TestOnly

Write-Host ""
$confirm = Read-Choice "Test erfolgreich. Dateien jetzt wirklich signieren? (J/N)" @('J','N','j','n')
if ($confirm -notmatch '^[Jj]$') {
    Write-Warn "Abgebrochen. Es wurden keine Dateien verändert."
    exit 0
}

Write-Host ""
Write-Info "Signiere Dateien ..."
Invoke-RdpSign -RdpsignPath $rdpsign -Mode $hashMode -Thumbprint $cert.Thumbprint -Files $files

Write-Host ""
Write-Ok "Fertig."
Write-Host "Hinweis: rdpsign überschreibt die ausgewählten .rdp-Dateien direkt."
Write-Host ""