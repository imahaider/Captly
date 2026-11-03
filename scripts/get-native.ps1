# Puts moonshine.dll and onnxruntime.dll in native\<rid>, which is git-ignored.
#   win-x64    copies the library from the moonshine-voice wheel.
#   win-arm64  builds it from source, because upstream publishes no win-arm64 library.
#              The toolchain is portable, needs no admin rights, and lands in .tools\.
# Needs an ARM64 host for win-arm64, because the llvm-mingw download below is a native ARM64 build.
param([ValidateSet('win-x64', 'win-arm64')][string]$Rid)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$moonshineVersion = '0.1.5'
$ortVersion = '1.23.2'
$llvmMingw = 'llvm-mingw-20261020-ucrt-aarch64'
$cmakeVersion = '4.4.5'
$ninjaVersion = '1.13.2'

$root = Split-Path $PSScriptRoot
if (-not $Rid) { $Rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' } }
$output = Join-Path $root "native\$Rid"
New-Item -ItemType Directory -Force $output | Out-Null

function Get-Archive([string]$Url, [string]$Destination) {
    if (Test-Path $Destination) { return }
    $zip = "$Destination.zip"
    Write-Host "Fetching $Url"
    Invoke-WebRequest -UseBasicParsing $Url -OutFile $zip
    Expand-Archive $zip $Destination -Force
    Remove-Item $zip
}

$tools = Join-Path $root '.tools'
New-Item -ItemType Directory -Force $tools | Out-Null

if ($Rid -eq 'win-x64') {
    $wheel = Join-Path $tools "moonshine-wheel-$moonshineVersion"
    $url = ((Invoke-RestMethod "https://pypi.org/pypi/moonshine-voice/$moonshineVersion/json").urls |
        Where-Object { $_.filename -match 'win_amd64' }).url
    Get-Archive $url $wheel
    Copy-Item "$wheel\moonshine_voice\moonshine.dll", "$wheel\moonshine_voice\onnxruntime.dll" $output -Force
    Get-ChildItem $output | Select-Object Name, Length
    return
}

$github = 'https://github.com'
Get-Archive "$github/mstorsjo/llvm-mingw/releases/download/20261020/$llvmMingw.zip" "$tools\llvm"
Get-Archive "$github/Kitware/CMake/releases/download/v$cmakeVersion/cmake-$cmakeVersion-windows-arm64.zip" "$tools\cmake"
Get-Archive "$github/ninja-build/ninja/releases/download/v$ninjaVersion/ninja-winarm64.zip" "$tools\ninja"
Get-Archive "https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime/$ortVersion/microsoft.ml.onnxruntime.$ortVersion.nupkg" "$tools\ort"
Get-Archive "https://codeload.github.com/moonshine-ai/moonshine/zip/refs/tags/v$moonshineVersion" "$tools\moonshine"

$source = Join-Path $tools "moonshine\moonshine-$moonshineVersion"
$env:PATH = "$tools\llvm\$llvmMingw\bin;$tools\cmake\cmake-$cmakeVersion-windows-arm64\bin;$tools\ninja;$env:PATH"

$core = Join-Path $source 'core'
$ortDir = Join-Path $core 'third-party\onnxruntime'
$ortArm = Join-Path $ortDir 'lib\windows\arm64'
New-Item -ItemType Directory -Force $ortArm | Out-Null
Copy-Item "$tools\ort\runtimes\win-arm64\native\onnxruntime.dll", "$tools\ort\runtimes\win-arm64\native\onnxruntime.lib" $ortArm -Force

# Upstream hard-codes x86_64 for WIN32, and passes the MSVC-only /w to any WIN32 compiler.
$find = Join-Path $ortDir 'find-ort-library-path.cmake'
(Get-Content $find -Raw).Replace('lib/windows/x86_64/onnxruntime.lib', 'lib/windows/arm64/onnxruntime.lib') | Set-Content $find -NoNewline
$lists = Join-Path $core 'CMakeLists.txt'
(Get-Content $lists -Raw).Replace("if (WIN32)`n    set_source_files_properties(`${CPP_ANNOTE_SRC_FILES}", "if (MSVC)`n    set_source_files_properties(`${CPP_ANNOTE_SRC_FILES}") | Set-Content $lists -NoNewline

$build = Join-Path $source 'build-arm64'
cmake -S $core -B $build -G Ninja `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ `
    -DMOONSHINE_BUILD_SHARED=ON `
    '-DCMAKE_CXX_FLAGS=-include cstdlib' `
    '-DCMAKE_SHARED_LINKER_FLAGS=-static -static-libgcc -static-libstdc++ -Wl,--export-all-symbols'
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed' }
cmake --build $build --target moonshine -- -k 0
if ($LASTEXITCODE -ne 0) { throw 'moonshine build failed' }

$dll = Get-ChildItem $build -Recurse -Include 'moonshine.dll', 'libmoonshine.dll' | Select-Object -First 1
Copy-Item $dll.FullName (Join-Path $output 'moonshine.dll') -Force
Copy-Item "$ortArm\onnxruntime.dll" $output -Force
Get-ChildItem $output | Select-Object Name, Length
