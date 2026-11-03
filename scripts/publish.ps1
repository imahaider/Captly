# Builds the portable download: dist\Captly-<version>-<rid>.zip, holding one self-contained
# Captly.exe, a short read-me and the licences, with a .sha256 file beside it.
# Speech models are not bundled. First-run setup downloads the one that suits the PC.
# Each runtime needs its native libraries from scripts\get-native.ps1 first; without them the
# exe could not caption, so that runtime is refused rather than packaged.
param([ValidateSet('win-x64', 'win-arm64')][string[]]$Rid = @('win-x64', 'win-arm64'))
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$dotnet = if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { 'dotnet' }
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

$readme = @"
Captly $version
Live captions that stick to your video.

Run Captly.exe. Setup downloads a speech model once, then Captly lives in the taskbar tray.
Nothing is installed: settings are kept in %APPDATA%\Captly and models in %LOCALAPPDATA%\Captly.
To remove Captly, quit it from the tray, then delete this folder and those two.

Captly is open source under the MIT licence (LICENSE.txt). The libraries it uses
and their licences are listed in THIRD_PARTY_NOTICES.md.

Source, help and updates: https://github.com/imahaider/Captly
"@

foreach ($r in $Rid) {
    $native = Join-Path $root "native\$r"
    if (-not (Test-Path "$native\moonshine.dll") -or -not (Test-Path "$native\onnxruntime.dll")) {
        Write-Warning "Skipping $r`: run scripts\get-native.ps1 -Rid $r first."
        continue
    }

    # A library for the wrong processor would package fine but never load (D51).
    $expected = @{ 'win-x64' = 0x8664; 'win-arm64' = 0xAA64 }[$r]
    foreach ($dll in 'moonshine.dll', 'onnxruntime.dll') {
        $bytes = [IO.File]::ReadAllBytes("$native\$dll")
        $machine = [BitConverter]::ToUInt16($bytes, [BitConverter]::ToInt32($bytes, 0x3C) + 4)
        if ($machine -ne $expected) { throw ("native\$r\$dll is built for machine 0x{0:X4}, not {1}" -f $machine, $r) }
    }

    $name = "Captly-$version-$r"
    $out = Join-Path $dist $name
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    & $dotnet publish (Join-Path $root 'src\Captly.App\Captly.App.csproj') -c Release -r $r -o $out -nologo -v q -p:DebugType=none -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $r" }
    Set-Content -Path (Join-Path $out 'Read me.txt') -Value $readme -Encoding UTF8
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $out 'LICENSE.txt')
    Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') (Join-Path $out 'THIRD_PARTY_NOTICES.md')

    $zip = Join-Path $dist "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path "$out\*" -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zip.sha256" -Value "$hash  $name.zip" -Encoding ASCII

    $exe = Get-Item (Join-Path $out 'Captly.exe')
    "{0}: Captly.exe {1:N1} MB, zip {2:N1} MB, SHA-256 {3}" -f $r, ($exe.Length / 1MB), ((Get-Item $zip).Length / 1MB), $hash
}
