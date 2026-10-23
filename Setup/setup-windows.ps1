# LARGERslicer Setup (Windows), wird von "Install Windows.bat" gestartet.
# Installiert die Grasshopper-Plugins ueber den Rhino Package Manager (yak)
# und kopiert die Robot-Bibliothek (KR120_R2700_2) in den Ordner, den Robots liest.

# "Paket" = neueste Version, "Paket 1.2.3" = feste Version
$packages = @(
  "Robots",
  "LARGERslicer",
  "Pufferfish"
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$gh = Join-Path $here "..\EXAMPLE FILES\00 - WEBER : GINGER [Robotic] 3D Printing\LARGERslicer Weber Robot and Ginger.gh"
function Ok($m)   { Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { Write-Host "  [FEHLER] $m" -ForegroundColor Red }

Write-Host "`n=== LARGERslicer Setup ===`n"

# Rhino 8 + Yak.exe finden (Standardpfad, sonst Registry)
$yak = "C:\Program Files\Rhino 8\System\Yak.exe"
if (-not (Test-Path $yak)) {
  $reg = Get-ItemProperty "HKLM:\SOFTWARE\McNeel\Rhinoceros\8.0\Install" -ErrorAction SilentlyContinue
  if ($reg -and $reg.Path) { $yak = Join-Path $reg.Path "Yak.exe" }
}
if (-not (Test-Path $yak)) {
  Fail "Rhino 8 wurde nicht gefunden. Bitte zuerst Rhino 8 installieren."
  exit 1
}
Ok "Rhino 8 gefunden"

if (Get-Process -Name "Rhino" -ErrorAction SilentlyContinue) {
  Read-Host "`n  Rhino laeuft gerade. Bitte Rhino beenden und Enter druecken"
}

Write-Host "`nPlugins:"
$installed = & $yak list 2>$null
$err = $false
foreach ($entry in $packages) {
  $parts = $entry -split " "
  $name = $parts[0]
  if ($parts.Count -gt 1 -and ($installed -contains "$name ($($parts[1]))")) { Ok "$entry ist schon installiert"; continue }
  $out = & $yak install @parts 2>&1 | Out-String
  if ($LASTEXITCODE -eq 0) {
    $v = [regex]::Matches($out, [regex]::Escape($name) + " \([^)]*\)") | Select-Object -Last 1
    Ok "$(if ($v) { $v.Value } else { $entry }) installiert"
  } else {
    Fail "$name konnte nicht installiert werden:"; Write-Host $out; $err = $true
  }
}

Write-Host "`nRobot-Bibliothek:"
# Robots 2.x liest standardmaessig <Dokumente>\Robots (auch wenn Dokumente in OneDrive liegt)
$lib = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Robots"
foreach ($base in @($env:APPDATA, $env:LOCALAPPDATA)) {
  $settings = Join-Path $base "McNeel\Rhinoceros\packages\8.0\Robots\settings.json"
  if (Test-Path $settings) {
    # Falls in Robots ein eigener lokaler Ordner gesetzt wurde, diesen benutzen
    try { $custom = (Get-Content $settings -Raw | ConvertFrom-Json).localLibraryPath; if ($custom) { $lib = $custom } } catch {}
  }
}
New-Item -ItemType Directory -Force -Path $lib | Out-Null
Get-ChildItem (Join-Path $here "RobotLibrary\*") -Include *.xml, *.3dm -File | ForEach-Object {
  try { Copy-Item $_.FullName $lib -Force -ErrorAction Stop; Ok "$($_.Name) -> $lib" }
  catch { Fail "Kopieren fehlgeschlagen: $($_.Name)"; $err = $true }
}

Write-Host ""
if (-not $err) {
  Write-Host "Fertig! Rhino 8 starten und `"$(Split-Path $gh -Leaf)`" oeffnen."
  Write-Host "Robot System: KR120_R2700_2"
} else {
  Write-Host "Es gab Fehler (siehe oben). Internetverbindung pruefen und erneut ausfuehren."
  exit 1
}
