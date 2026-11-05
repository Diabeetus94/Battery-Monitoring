# Wird mit Administratorrechten ausgefuehrt (UAC):
#  1) eigenes Zertifikat der Akku-Anzeige als vertrauenswuerdig eintragen (nur einmal noetig)
#  2) signierte AkkuAnzeige.exe + Symbole nach C:\Program Files\AkkuAnzeige kopieren
param([string]$Source, [string]$Cert, [string]$LogFile)
$ErrorActionPreference = 'Stop'
try {
    $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $Cert
    foreach ($storeName in 'Root', 'TrustedPublisher') {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store $storeName, 'LocalMachine'
        $store.Open('ReadWrite')
        if (-not ($store.Certificates | Where-Object Thumbprint -eq $c.Thumbprint)) { $store.Add($c); "Zertifikat in LocalMachine\$storeName eingetragen" }
        $store.Close()
    }
    $dest = Join-Path $env:ProgramFiles 'AkkuAnzeige'
    New-Item -ItemType Directory -Force $dest | Out-Null
    Get-Process AkkuAnzeige -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 1
    foreach ($f in 'AkkuAnzeige.exe', 'AkkuAnzeige.ico', 'AkkuAnzeige-Desktop.ico') {
        $p = Join-Path $Source $f
        if (Test-Path $p) { Copy-Item $p $dest -Force }
    }
    "OK: installiert nach $dest"
} catch { "FEHLER: $_" }
