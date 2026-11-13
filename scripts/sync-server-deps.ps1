# Sao chép phần source mà console server cần từ 2 package Fomoxa vào server-deps\.
#
# Lý do: console server (MyGame\Server~) biên dịch *source* của package chứ không
# tham chiếu DLL. Đường dẫn thật nằm ở MyGame\Library\PackageCache\com.fomoxa.*@<hash>
# - có hash, không commit được, và bị .gitignore. server-deps\ là bản vendor cố định
# để `dotnet build` và Docker build không phụ thuộc máy đang có PackageCache gì.
#
# Chạy lại mỗi khi đổi version package trong MyGame\Packages\manifest.json:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\sync-server-deps.ps1

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$myGame = Join-Path $repoRoot "MyGame"
$target = Join-Path $repoRoot "server-deps"

function Find-Package([string]$name) {
    $embedded = Join-Path $myGame "Packages/$name"
    if (Test-Path $embedded) {
        return $embedded
    }

    $cache = Join-Path $myGame "Library/PackageCache"
    if (-not (Test-Path $cache)) {
        throw "missing $cache - open the Unity project once so packages are resolved"
    }

    $found = Get-ChildItem $cache -Directory |
        Where-Object { $_.Name -eq $name -or $_.Name -like "$name@*" } |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not $found) {
        throw "package $name not found in Packages\ or Library\PackageCache"
    }

    return $found.FullName
}

function Copy-Source([string]$from, [string]$to, [string]$filter) {
    if (-not (Test-Path $from)) {
        throw "missing source directory: $from"
    }

    $files = Get-ChildItem $from -Recurse -File -Filter $filter
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($from.Length).TrimStart('\', '/')
        $destination = Join-Path $to $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item $file.FullName $destination -Force
    }

    return $files.Count
}

if (Test-Path $target) {
    Remove-Item $target -Recurse -Force
}

$networking = Find-Package "com.fomoxa.networking"
$rpc = Find-Package "com.fomoxa.rpc"

$netOut = Join-Path $target "com.fomoxa.networking"
$rpcOut = Join-Path $target "com.fomoxa.rpc"

$count = 0
$count += Copy-Source (Join-Path $networking "Runtime/Core") (Join-Path $netOut "Runtime/Core") "*.cs"
$count += Copy-Source (Join-Path $networking "Standalone~") (Join-Path $netOut "Standalone~") "*.cs"
$count += Copy-Source (Join-Path $networking "Runtime/Plugins") (Join-Path $netOut "Runtime/Plugins") "Fomoxa.*.dll"
$count += Copy-Source (Join-Path $rpc "Runtime/Fomoxa.Rpc") (Join-Path $rpcOut "Runtime/Fomoxa.Rpc") "*.cs"
$count += Copy-Source (Join-Path $rpc "Runtime/Directory") (Join-Path $rpcOut "Runtime/Directory") "*.cs"
$count += Copy-Source (Join-Path $rpc "Runtime/Generators") (Join-Path $rpcOut "Runtime/Generators") "*.dll"

$netVersion = (Get-Content (Join-Path $networking "package.json") -Raw | ConvertFrom-Json).version
$rpcVersion = (Get-Content (Join-Path $rpc "package.json") -Raw | ConvertFrom-Json).version
$netSource = $networking.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
$rpcSource = $rpc.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')

$manifest = Join-Path $target "README.md"
@"
# server-deps

Bản vendor của phần source 2 package Fomoxa mà ``MyGame\Server~`` biên dịch.
**Sinh tự động** bằng ``scripts\sync-server-deps.ps1`` - đừng sửa tay.

| Package | Version | Nguồn |
|---|---|---|
| ``com.fomoxa.networking`` | $netVersion | ``$netSource`` |
| ``com.fomoxa.rpc`` | $rpcVersion | ``$rpcSource`` |

Đã sao chép: ``Runtime/Core``, ``Standalone~``, ``Runtime/Plugins/*.dll`` (networking);
``Runtime/Fomoxa.Rpc``, ``Runtime/Directory``, ``Runtime/Generators/*.dll`` (rpc). Bỏ
``Runtime/Unity``, ``Editor``, ``Tests`` vì console server không dùng UnityEngine.
"@ | Set-Content -Path $manifest -Encoding utf8

Write-Host "server-deps: $count file"
Write-Host "  com.fomoxa.networking $netVersion"
Write-Host "  com.fomoxa.rpc        $rpcVersion"
