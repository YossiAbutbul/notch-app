# Builds the shippable Notch.exe: one self-contained file (no .NET install needed) that installs itself when run.
# Usage: powershell -NoProfile -File tools\publish.ps1
# Output: dist\Notch.exe
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj = Join-Path $root "src\NotchApp\NotchApp.csproj"
$out = Join-Path $root "dist"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

& $dotnet publish $proj -c Release -r win-x64 --self-contained true -o (Join-Path $root "artifacts\publish") `
  -p:Packaged=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:SatelliteResourceLanguages=en -nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $root "artifacts\publish\Notch.exe") (Join-Path $out "Notch.exe") -Force
$f = Get-Item (Join-Path $out "Notch.exe")
"Built {0} ({1:N1} MB, version {2})" -f $f.FullName, ($f.Length / 1MB), $f.VersionInfo.FileVersion
