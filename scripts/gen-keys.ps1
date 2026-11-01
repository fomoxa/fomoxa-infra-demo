param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$secretRoot = Join-Path $repoRoot "deploy/secrets"
$directorySecrets = Join-Path $secretRoot "directory"

New-Item -ItemType Directory -Force -Path $directorySecrets | Out-Null

function New-Hex {
    param([int]$Bytes)

    $buffer = New-Object byte[] $Bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($buffer)
    }
    finally {
        $rng.Dispose()
    }

    return (($buffer | ForEach-Object { $_.ToString("x2") }) -join "")
}

$keys = [ordered]@{
    "directory/backend" = 24
    "directory/pod"     = 24
    "directory/gateway" = 24
    "directory/admin"   = 24
    "directory/root"    = 32
    "backend-pod"       = 24
}

$created = 0
$skipped = 0

foreach ($relative in $keys.Keys) {
    $path = Join-Path $secretRoot $relative

    if ((Test-Path $path) -and -not $Force) {
        $skipped++
        continue
    }

    $parent = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    [IO.File]::WriteAllText($path, (New-Hex $keys[$relative]))
    $created++
}

"secrets: $created tao moi, $skipped giu nguyen"
"root key: 64 hex (bat buoc), role key: 48 hex (>= 16 byte)"
"thu muc: $secretRoot"
