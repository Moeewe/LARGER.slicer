# LARGERslicer Setup (Windows), wird von "Install Windows.bat" gestartet.
# Installiert die Grasshopper-Plugins fuer Rhino 8 und/oder Rhino 7
# und kopiert die Robot-Bibliothek (KR120_R2700_2) in den Ordner, den Robots liest.
#
# Umgang mit vorhandenen Installationen:
#   nicht installiert          -> neueste passende Version installieren
#   gleiche Version            -> nichts tun
#   aeltere Version            -> auf neueste aktualisieren
#   neuere Version (z.B. Beta) -> behalten
#   feste Version (unten)      -> genau diese Version aktivieren
#   alte manuelle .gha-Kopie   -> anbieten, sie in einen Backup-Ordner zu verschieben

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
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
function Ok($m)   { Write-Host "  [OK] $m" -ForegroundColor Green }
function Info($m) { Write-Host "  [i] $m" -ForegroundColor Cyan }
function Warn($m) { Write-Host "  [!] $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  [FEHLER] $m" -ForegroundColor Red }
function Ask($q)  { $a = Read-Host "    $q [J/n]"; return ($a -eq "" -or $a -match "^[JjYy]") }

# Versionsvergleich: 1.10 > 1.9, "-beta"/"+abc" werden ignoriert
function Test-Newer($a, $b) {
  $pa = ($a -split "[-+]")[0].Split("."); $pb = ($b -split "[-+]")[0].Split(".")
  for ($i = 0; $i -lt [Math]::Max($pa.Count, $pb.Count); $i++) {
    $x = 0; $y = 0
    if ($i -lt $pa.Count) { [void][int]::TryParse($pa[$i], [ref]$x) }
    if ($i -lt $pb.Count) { [void][int]::TryParse($pb[$i], [ref]$y) }
    if ($x -gt $y) { return $true }
    if ($x -lt $y) { return $false }
  }
  return $false
}

function Get-InstalledVersion($name, $rh) {
  $m = Join-Path $pkgRoot "$rh.0\$name\manifest.txt"
  if (Test-Path $m) { return (Get-Content $m -TotalCount 1).Trim() }
  return $null
}

# Neueste (oder gewuenschte) Version fuer eine Rhino-Version vom Package-Server
function Get-ServerVersion($name, $rh, $want) {
  try { $versions = Invoke-RestMethod "https://yak.rhino3d.com/versions/$name" } catch { return $null }
  # Rhino 8 kann auch Rhino-7-Pakete laden, Rhino 7 auch Rhino-6-Pakete
  $pattern = if ($rh -eq 8) { "^rh[78]" } else { "^rh[67]" }
  $best = $null
  foreach ($v in $versions) {
    if ($want) { if ($v.version -ne $want) { continue } } elseif ($v.prerelease) { continue }
    $d = $v.distributions | Where-Object { $_.rhino_version -match $pattern } | Select-Object -First 1
    if ($d -and (-not $best -or (Test-Newer $v.version $best.version))) { $best = @{ version = $v.version; url = $d.url } }
  }
  return $best
}

function Find-RhinoSystem($major) {
  $dir = "C:\Program Files\Rhino $major\System"
  if (-not (Test-Path (Join-Path $dir "Rhino.exe"))) {
    $reg = Get-ItemProperty "HKLM:\SOFTWARE\McNeel\Rhinoceros\$major.0\Install" -ErrorAction SilentlyContinue
    if ($reg -and $reg.Path) { $dir = $reg.Path.TrimEnd("\") }
  }
  if (Test-Path (Join-Path $dir "Rhino.exe")) { return $dir }
  return $null
}

# Rhino 8: ueber Yak.exe. Rhino 7: Paket direkt laden und so ablegen, wie es der
# Package Manager tut (alte Versionsordner bleiben, manifest.txt bestimmt die aktive).
function Invoke-Install($rh, $name, $hit) {
  if ($rh -eq 8) {
    $out = & $script:yak8 install $name $hit.version 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Host $out; return $false }
    return $true
  }
  $dest = Join-Path $pkgRoot "7.0\$name"
  $verDir = Join-Path $dest $hit.version
  try {
    if (-not (Test-Path (Join-Path $verDir "manifest.yml"))) {
      $tmp = Join-Path $env:TEMP ("larger_" + [guid]::NewGuid())
      New-Item -ItemType Directory -Force -Path $tmp | Out-Null
      Invoke-WebRequest $hit.url -OutFile (Join-Path $tmp "p.zip") -UseBasicParsing
      if (Test-Path $verDir) { Remove-Item $verDir -Recurse -Force }
      Expand-Archive (Join-Path $tmp "p.zip") -DestinationPath $verDir -Force
      Get-ChildItem $verDir -Recurse -File | Unblock-File
      Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    Set-Content -Path (Join-Path $dest "manifest.txt") -Value $hit.version -Encoding ASCII
    return $true
  } catch { Write-Host "      $_"; return $false }
}

function Install-Package($rh, $name, $pin) {
  $have = Get-InstalledVersion $name $rh
  $hit = Get-ServerVersion $name $rh $pin
  if (-not $hit) {
    if ($have) { Warn "$name $have ist installiert (Package-Server nicht erreichbar, nicht geprueft)"; return $true }
    Fail "${name}: $(if ($pin) { "Version $pin " })fuer Rhino $rh nicht gefunden oder Package-Server nicht erreichbar"; return $false
  }
  if ($have -eq $hit.version) { Ok "$name $have ist aktuell"; return $true }
  if ($have -and -not $pin -and (Test-Newer $have $hit.version)) {
    Info "$name $have ist neuer als die veroeffentlichte $($hit.version) - wird behalten"; return $true
  }
  if (Invoke-Install $rh $name $hit) {
    if (-not $have) { Ok "$name $($hit.version) installiert" }
    elseif ($pin) { Ok "$name $have -> $($hit.version) (feste Version)" }
    else { Ok "$name $have -> $($hit.version) aktualisiert" }
    return $true
  }
  Fail "$name $($hit.version) konnte nicht installiert werden"; return $false
}

# Alte manuelle Installationen (.gha direkt im Grasshopper-Libraries-Ordner) werden
# zusaetzlich zum Paket geladen und verursachen doppelte Komponenten.
function Test-ManualCopies {
  $lib = Join-Path $env:APPDATA "Grasshopper\Libraries"
  if (-not (Test-Path $lib)) { return }
  $found = @(Get-ChildItem $lib -Recurse -Depth 2 -File -Include "LARGERslicer*.gha", "Robots.gha", "Pufferfish*.gha" -ErrorAction SilentlyContinue)
  if ($found.Count -eq 0) { return }
  Warn "Alte manuelle Plugin-Kopien gefunden (werden doppelt geladen):"
  $found | ForEach-Object { Write-Host "      $($_.FullName.Substring($lib.Length + 1))" }
  if (Ask "In einen Backup-Ordner verschieben?") {
    $backup = Join-Path (Split-Path $lib) "Libraries-Backup-$stamp"
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    $found | ForEach-Object { Move-Item $_.FullName $backup -Force; Ok "verschoben: $($_.Name)" }
    Info "Backup: $backup"
  } else { Warn "Nicht verschoben - bei Problemen diese Dateien manuell entfernen" }
}

# Robot-Bibliothek kopieren. Abweichende vorhandene Dateien werden als *.bak gesichert
# (nicht *.xml, damit Robots sie nicht als zweite Bibliothek laedt).
function Copy-Library($lib) {
  New-Item -ItemType Directory -Force -Path $lib | Out-Null
  $okAll = $true
  $mine = Get-ChildItem (Join-Path $here "RobotLibrary\*") -Include *.xml, *.3dm -File
  foreach ($f in $mine) {
    $target = Join-Path $lib $f.Name
    if ((Test-Path $target) -and ((Get-FileHash $target).Hash -eq (Get-FileHash $f.FullName).Hash)) { Ok "$($f.Name) ist aktuell ($lib)"; continue }
    try {
      if (Test-Path $target) { Move-Item $target "$target.bak-$stamp" -Force; Info "alte $($f.Name) gesichert als $($f.Name).bak-$stamp" }
      Copy-Item $f.FullName $lib -Force -ErrorAction Stop; Ok "$($f.Name) -> $lib"
    } catch { Fail "Kopieren fehlgeschlagen: $($f.Name)"; $okAll = $false }
  }
  # Gleicher Robotername in einer anderen Datei? Dann ist unklar, welche Robots nimmt.
  Get-ChildItem $lib -Filter *.xml -File | Where-Object { $mine.Name -notcontains $_.Name } | ForEach-Object {
    if (Select-String -Path $_.FullName -Pattern 'name="KR120_R2700_2"' -Quiet) { Warn "KR120_R2700_2 ist auch in $($_.Name) definiert - bitte dort entfernen" }
  }
  return $okAll
}

Write-Host "`n=== LARGERslicer Setup ===`n"

$r8 = Find-RhinoSystem 8
$r7 = Find-RhinoSystem 7
if (-not $r8 -and -not $r7) { Fail "Weder Rhino 8 noch Rhino 7 gefunden. Bitte zuerst Rhino installieren."; exit 1 }
if ($r8) { Ok "Rhino 8 gefunden"; $script:yak8 = Join-Path $r8 "Yak.exe" }
if ($r7) { Ok "Rhino 7 gefunden" }

if (Get-Process -Name "Rhino" -ErrorAction SilentlyContinue) {
  Read-Host "`n  Rhino laeuft gerade. Bitte Rhino beenden und Enter druecken"
}

$err = $false
foreach ($rh in @(8, 7)) {
  if (($rh -eq 8 -and -not $r8) -or ($rh -eq 7 -and -not $r7)) { continue }
  Write-Host "`nPlugins fuer Rhino ${rh}:"
  foreach ($entry in $packages) {
    $parts = $entry -split " "
    $name = $parts[0]
    # Feste Versionen gelten nur fuer Rhino 8, Rhino 7 bekommt die neueste passende
    $pin = if ($rh -eq 8 -and $parts.Count -gt 1) { $parts[1] } else { $null }
    # Pufferfish gibt es fuer Rhino 7 auch als aelteres "KSAPufferfish" - nicht doppelt installieren
    $ksa = Get-InstalledVersion "KSAPufferfish" $rh
    if ($name -eq "Pufferfish" -and $ksa) { Ok "Pufferfish ist schon installiert (KSAPufferfish $ksa)"; continue }
    if (-not (Install-Package $rh $name $pin)) { $err = $true }
  }
}
# Rhino 7 und 8 teilen sich unter Windows denselben Libraries-Ordner
Test-ManualCopies

Write-Host "`nRobot-Bibliothek:"
# Robots (1.x und 2.x) liest standardmaessig <Dokumente>\Robots (auch wenn Dokumente in OneDrive liegt)
$lib = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Robots"
$settings = Join-Path $pkgRoot "8.0\Robots\settings.json"
if ($r8 -and (Test-Path $settings)) {
  # Falls in Robots ein eigener lokaler Ordner gesetzt wurde, diesen benutzen
  try { $custom = (Get-Content $settings -Raw | ConvertFrom-Json).localLibraryPath; if ($custom) { $lib = $custom } } catch {}
}
if (-not (Copy-Library $lib)) { $err = $true }

Write-Host ""
if (-not $err) {
  Write-Host "Fertig! Rhino starten und `"$gh`" aus diesem SETUP-Ordner oeffnen."
  $ghPath = Join-Path $here $gh
  if (Test-Path $ghPath) { Start-Process explorer.exe "/select,`"$ghPath`"" }
  Write-Host "Robot System: KR120_R2700_2"
} else {
  Write-Host "Es gab Fehler (siehe oben). Internetverbindung pruefen und erneut ausfuehren."
  exit 1
}
