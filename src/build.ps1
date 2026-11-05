# Baut AkkuAnzeige.exe (Icon erzeugen + kompilieren)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$out = Split-Path $src -Parent
Add-Type -AssemblyName System.Drawing

# ---- Icon (PNG-basiertes ICO mit mehreren Groessen) ----
function New-BatteryPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $u = $s / 32.0
    $body = New-Object System.Drawing.RectangleF((2*$u), (8*$u), (25*$u), (16*$u))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([math]::Max(1.0, 2.2*$u))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40,40,40))), $body)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60,190,90))), (4.5*$u), (10.5*$u), (15*$u), (11*$u))
    $g.DrawRectangle($pen, $body.X, $body.Y, $body.Width, $body.Height)
    $g.FillRectangle([System.Drawing.Brushes]::White, (27*$u), (13*$u), (3*$u), (6*$u))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return ,$ms.ToArray()
}
$sizes = 16, 24, 32, 48, 256
$pngs = foreach ($s in $sizes) { ,(New-BatteryPng $s) }
$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
[System.IO.File]::WriteAllBytes("$src\app.ico", $fs.ToArray())

# Eigenes Symbol des Nutzers verwenden, falls vorhanden
$custom = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'BatterieAkkuSymbol.ico'
if (Test-Path $custom) { Copy-Item $custom "$src\app.ico" -Force; "Icon: $custom" }
# Fenster- und Desktop-Symbol
$winIco = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'Bluetooth_Batterie_Minimal.ico'
if (Test-Path $winIco) { Copy-Item $winIco "$src\win.ico" -Force; Copy-Item $winIco "$out\AkkuAnzeige-Desktop.ico" -Force; "Fenster-Icon: $winIco" }
else { Copy-Item "$src\app.ico" "$src\win.ico" -Force }

# ---- Kompilieren ----
# ---- Pruefung: doppelte Uebersetzungen lassen die App beim Start abstuerzen ----
$langKeys = [regex]::Matches((Get-Content "$src\Lang.cs" -Raw -Encoding UTF8), '\{\s*"((?:[^"\\]|\\.)*)"\s*,\s*"') | ForEach-Object { $_.Groups[1].Value }
$dup = $langKeys | Group-Object | Where-Object Count -gt 1
if ($dup) { throw "Doppelte Uebersetzungen in Lang.cs: " + (($dup | ForEach-Object Name) -join ', ') }

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
# WinRT (Bluetooth-LE-Scanner fuer AirPods): Windows-Metadaten + WinRT-Bruecke des .NET Framework
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$winrt = @(
    "/r:$env:WINDIR\System32\WinMetadata\Windows.Devices.winmd",
    "/r:$env:WINDIR\System32\WinMetadata\Windows.Foundation.winmd",
    "/r:$env:WINDIR\System32\WinMetadata\Windows.Storage.winmd",
    "/r:$fw\System.Runtime.dll", "/r:$fw\System.Runtime.WindowsRuntime.dll", "/r:$fw\System.Runtime.InteropServices.WindowsRuntime.dll",
    "/nowarn:1701")
& $csc /nologo /target:winexe /codepage:65001 /optimize+ /debug:pdbonly /platform:anycpu `
    "/win32icon:$src\app.ico" "/resource:$src\app.ico,AkkuAnzeige.app.ico" "/resource:$src\win.ico,AkkuAnzeige.win.ico" `
    "/resource:$out\CHANGELOG.md,AkkuAnzeige.CHANGELOG.md" "/resource:$out\CHANGELOG.de.md,AkkuAnzeige.CHANGELOG.de.md" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.ServiceProcess.dll `
    "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" `
    "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" `
    @winrt "/win32manifest:$src\app.manifest" `
    "/out:$out\AkkuAnzeige.exe" "$src\AssemblyInfo.cs" "$src\AkkuAnzeige.cs" "$src\Ui.cs" "$src\Lang.cs" "$src\Vendors.cs" "$src\Info.cs" "$src\Update.cs" "$src\BugReport.cs" "$src\Switcher.cs" `
    "$src\Hid.cs" "$src\LogiHidpp.cs" "$src\DirectHid.cs" "$src\AppleBle.cs"
if ($LASTEXITCODE -ne 0) { throw "Build (App) fehlgeschlagen" }
Copy-Item "$src\app.ico" "$out\AkkuAnzeige.ico" -Force

# ---- Signieren (noetig fuer UIAccess) ----
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object Subject -eq 'CN=Akku-Anzeige Code Signing (lokal)' | Select-Object -First 1
if (-not $cert) { throw "Signatur-Zertifikat fehlt" }
$sig = Set-AuthenticodeSignature -FilePath "$out\AkkuAnzeige.exe" -Certificate $cert -HashAlgorithm SHA256
"Signatur App: $($sig.Status)"

# ---- Setup.exe (enthaelt die signierte App, Symbole und das Zertifikat) ----
New-Item -ItemType Directory -Force "$out\Setup" | Out-Null
$ver = [regex]::Match((Get-Content "$src\AssemblyInfo.cs" -Raw), 'AssemblyInformationalVersion\("([^"]+)"\)').Groups[1].Value
if (-not $ver) { $ver = 'dev' }
Get-ChildItem "$out\Setup" -Filter 'AkkuAnzeige-Setup*.exe' | Remove-Item -Force
$setupExe = "$out\Setup\AkkuAnzeige-Setup-$ver.exe"
& $csc /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu `
    "/win32icon:$src\win.ico" "/win32manifest:$src\setup.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.ServiceProcess.dll `
    "/resource:$out\AkkuAnzeige.exe,AkkuAnzeige.exe" "/resource:$out\AkkuAnzeige.ico,AkkuAnzeige.ico" `
    "/resource:$out\AkkuAnzeige-Desktop.ico,AkkuAnzeige-Desktop.ico" "/resource:$out\cert\AkkuAnzeige.cer,AkkuAnzeige.cer" `
    "/out:$setupExe" "$src\AssemblyInfo.cs" "$src\Setup.cs" "$src\Vendors.cs"
if ($LASTEXITCODE -ne 0) { throw "Build (Setup) fehlgeschlagen" }
$sig2 = Set-AuthenticodeSignature -FilePath $setupExe -Certificate $cert -HashAlgorithm SHA256
"Signatur Setup: $($sig2.Status) -> $setupExe"

# ---- Installieren nach Program Files (UAC-Abfrage) ----
if ($args -notcontains '-noinstall') {
    $log = Join-Path $env:TEMP 'akkuanzeige_install.log'
    $p = Start-Process powershell -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
        "& '$src\install_admin.ps1' -Source '$out' -Cert '$out\cert\AkkuAnzeige.cer' *> '$log'")
    if (Test-Path $log) { Get-Content $log }
}
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen" }
"OK: $out\AkkuAnzeige.exe"
