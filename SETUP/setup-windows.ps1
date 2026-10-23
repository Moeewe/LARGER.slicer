# LARGERslicer Setup (Windows), wird von "Install Windows.bat" gestartet.
# Installiert die Grasshopper-Plugins fuer Rhino 8 und/oder Rhino 7
# und kopiert die Robot-Bibliothek (KR120_R2700_2) in den Ordner, den Robots liest.

# "Paket" = neueste passende Version, "Paket 1.2.3" = feste Version (nur Rhino 8)
$packages = @(
  "Robots",
  "LARGERslicer",
  "Pufferfish"
)

$ErrorActionPreference = "Continue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$gh = "LARGERslicer Weber Robot and Ginger.gh"
$pkgRoot = Join-Path $env:APPDATA "McNeel\Rhinoceros\packages"
function Ok($m)   { Write-Host "  [OK] $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  [!] $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  [FEHLER] $m" -ForegroundColor Red }

function Find-RhinoSystem($major) {
  $dir = "C:\Program Files\Rhino $major\System"
  if (-not (Test-Path (Join-Path $dir "Rhino.exe"))) {
    $reg = Get-ItemProperty "HKLM:\SOFTWARE\McNeel\Rhinoceros\$major.0\Install" -ErrorAction SilentlyContinue
    if ($reg -and $reg.Path) { $dir = $reg.Path.TrimEnd("\") }
  }
  if (Test-Path (Join-Path $dir "Rhino.exe")) { return $dir }
  return $null
}

# Rhino 8: ueber Yak.exe (waehlt automatisch die passende Version)
function Install-R8($yak, $entry) {
  $parts = $entry -split " "
  $name = $parts[0]
  if ($parts.Count -gt 1 -and ((& $yak list 2>$null) -contains "$name ($($parts[1]))")) { Ok "$entry ist schon installiert"; return $true }
  $out = & $yak install @parts 2>&1 | Out-String
  if ($LASTEXITCODE -eq 0) {
    $v = [regex]::Matches($out, [regex]::Escape($name) + " \([^)]*\)") | Select-Object -Last 1
    Ok "$(if ($v) { $v.Value } else { $entry }) installiert"; return $true
  }
  Fail "$name konnte nicht installiert werden:"; Write-Host $out; return $false
}

# Rhino 7: neueste Rhino-7-Version direkt vom Package-Server laden
# und so ablegen, wie es der Package Manager tut.
function Install-R7($name) {
  try { $versions = Invoke-RestMethod "https://yak.rhino3d.com/versions/$name" }
  catch { Fail "${name}: Package-Server nicht erreichbar"; return $false }
  $hit = $null
  foreach ($v in $versions) {
    if ($v.prerelease) { continue }
    $d = $v.distributions | Where-Object { $_.rhino_version -match "^rh[67]" } | Select-Object -First 1
    if ($d) { $hit = @{ version = $v.version; url = $d.url }; break }
  }
  if (-not $hit) { Fail "${name}: keine Version fuer Rhino 7 gefunden"; return $false }

  $dest = Join-Path $pkgRoot "7.0\$name"
  if (Test-Path (Join-Path $dest "$($hit.version)\manifest.yml")) { Ok "$name ($($hit.version)) ist schon installiert"; return $true }
  $tmp = Join-Path $env:TEMP ("larger_" + [guid]::NewGuid())
  try {
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    Invoke-WebRequest $hit.url -OutFile (Join-Path $tmp "p.zip") -UseBasicParsing
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Expand-Archive (Join-Path $tmp "p.zip") -DestinationPath (Join-Path $dest $hit.version) -Force
    Set-Content -Path (Join-Path $dest "manifest.txt") -Value $hit.version -Encoding ASCII
    Get-ChildItem $dest -Recurse -File | Unblock-File
    Ok "$name ($($hit.version)) installiert"; return $true
  } catch {
    Fail "$name ($($hit.version)) konnte nicht geladen werden: $_"; return $false
  } finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Write-Host "`n=== LARGERslicer Setup ===`n"

$r8 = Find-RhinoSystem 8
$r7 = Find-RhinoSystem 7
if (-not $r8 -and -not $r7) { Fail "Weder Rhino 8 noch Rhino 7 gefunden. Bitte zuerst Rhino installieren."; exit 1 }
if ($r8) { Ok "Rhino 8 gefunden" }
if ($r7) { Ok "Rhino 7 gefunden" }

if (Get-Process -Name "Rhino" -ErrorAction SilentlyContinue) {
  Read-Host "`n  Rhino laeuft gerade. Bitte Rhino beenden und Enter druecken"
}

$err = $false
if ($r8) {
  Write-Host "`nPlugins fuer Rhino 8:"
  foreach ($entry in $packages) { if (-not (Install-R8 (Join-Path $r8 "Yak.exe") $entry)) { $err = $true } }
}
if ($r7) {
  Write-Host "`nPlugins fuer Rhino 7:"
  foreach ($entry in $packages) {
    $name = ($entry -split " ")[0]
    # Pufferfish gibt es fuer Rhino 7 auch als aelteres "KSAPufferfish" - nicht doppelt installieren
    if ($name -eq "Pufferfish" -and (Test-Path (Join-Path $pkgRoot "7.0\KSAPufferfish"))) { Ok "Pufferfish ist schon installiert (KSAPufferfish)"; continue }
    if (-not (Install-R7 $name)) { $err = $true }
  }
}

Write-Host "`nRobot-Bibliothek:"
# Robots (1.x und 2.x) liest standardmaessig <Dokumente>\Robots (auch wenn Dokumente in OneDrive liegt)
$lib = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Robots"
$settings = Join-Path $pkgRoot "8.0\Robots\settings.json"
if ($r8 -and (Test-Path $settings)) {
  # Falls in Robots ein eigener lokaler Ordner gesetzt wurde, diesen benutzen
  try { $custom = (Get-Content $settings -Raw | ConvertFrom-Json).localLibraryPath; if ($custom) { $lib = $custom } } catch {}
}
New-Item -ItemType Directory -Force -Path $lib | Out-Null
Get-ChildItem (Join-Path $here "RobotLibrary\*") -Include *.xml, *.3dm -File | ForEach-Object {
  try { Copy-Item $_.FullName $lib -Force -ErrorAction Stop; Ok "$($_.Name) -> $lib" }
  catch { Fail "Kopieren fehlgeschlagen: $($_.Name)"; $err = $true }
}

Write-Host ""
if (-not $err) {
  Write-Host "Fertig! Rhino starten und `"$gh`" oeffnen."
  Write-Host "Robot System: KR120_R2700_2"
  if ($r7) { Warn "Rhino 7: Das Skript enthaelt eine Python-3-Komponente, die es nur in Rhino 8 gibt." }
} else {
  Write-Host "Es gab Fehler (siehe oben). Internetverbindung pruefen und erneut ausfuehren."
  exit 1
}
