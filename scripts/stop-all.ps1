$ErrorActionPreference = "Continue"

$names = @("MyGame", "fomoxa-directory", "fomoxa-gateway", "MyGame.Backend")

foreach ($name in $names) {
    $running = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "dung $name ($($running.Count) tien trinh)"
        $running | Stop-Process -Force
    }
}

Start-Sleep -Milliseconds 500

$ports = 9500, 9501, 9600, 9601, 9602, 7777, 7778
$stillOpen = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in $ports }
if ($stillOpen) {
    Write-Host "con mo: $($stillOpen.LocalPort -join ', ')"
}
else {
    Write-Host "da dung het, port sach"
}
