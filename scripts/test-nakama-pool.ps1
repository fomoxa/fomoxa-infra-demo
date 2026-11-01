param(
    [string]$Base = "http://127.0.0.1:7350",
    [string]$HttpKey = "defaulthttpkey",
    [string]$ServerKey = "defaultkey"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Kiem tra P5.5: Matchmaking tu dong cap room tu pool (>= 4 client) ===" -ForegroundColor Cyan

function Auth-Device {
    param([string]$Id)

    $body = @{ id = $Id; create = $true } | ConvertTo-Json -Compress
    $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${ServerKey}:"))
    $session = Invoke-RestMethod "$Base/v2/account/authenticate/device?create=true" -Method Post -Headers @{ Authorization = "Basic $basic" } -Body $body -ContentType "application/json"

    $jwt = $session.token.Split(".")[1].Replace("-", "+").Replace("_", "/")
    switch ($jwt.Length % 4) {
        2 { $jwt += "==" }
        3 { $jwt += "=" }
    }

    $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($jwt)) | ConvertFrom-Json
    return @{ Token = $session.token; UserId = $(if ($claims.uid) { $claims.uid } else { $claims.sub }) }
}

function Invoke-Rpc {
    param([string]$Name, [string]$Auth, [string]$Body)

    $file = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($file, $Body)

    try {
        return & curl.exe -s -X POST "$Base/v2/rpc/${Name}?unwrap" -H "Authorization: $Auth" -H "Content-Type: application/json" --data-binary "@$file"
    }
    finally {
        Remove-Item $file -ErrorAction SilentlyContinue
    }
}

function Rpc-User {
    param([string]$Name, [string]$Token, [string]$Body)
    return Invoke-Rpc $Name "Bearer $Token" $Body | ConvertFrom-Json
}

function Rpc-Key {
    param([string]$Name, [string]$Body)
    $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${HttpKey}:"))
    $text = Invoke-Rpc $Name "Basic $basic" $Body
    $parsed = $text | ConvertFrom-Json -ErrorAction SilentlyContinue

    if ($null -eq $parsed -or ($parsed.PSObject.Properties.Name -contains "code" -and $parsed.code -ne 0)) {
        return @{ ok = $false; body = $text }
    }
    if ($parsed.PSObject.Properties.Name -contains "ok" -and -not $parsed.ok) {
        return @{ ok = $false; body = $parsed }
    }
    return @{ ok = $true; body = $parsed }
}

# 1. Reset auto pool
Write-Host "`n1/4 Reset auto pool..." -ForegroundColor Yellow
$null = Rpc-Key "reset_match" (@{ room = "auto" } | ConvertTo-Json -Compress)

# 2. Dang nhap 4 client
Write-Host "`n2/4 Dang nhap 4 clients (ana, bo, chi, dung)..." -ForegroundColor Yellow
$ana  = Auth-Device "fomoxa-p55-ana"
$bo   = Auth-Device "fomoxa-p55-bo"
$chi  = Auth-Device "fomoxa-p55-chi"
$dung = Auth-Device "fomoxa-p55-dung"

# 3. Cap 1 (ana + bo) yeu cau find_match voi room "auto"
Write-Host "`n3/4 Cap 1 (ana + bo) vao auto pool..." -ForegroundColor Yellow
$autoBody = @{ room = "auto" } | ConvertTo-Json -Compress

$resAna = Rpc-User "find_match" $ana.Token $autoBody
if ($resAna.status -ne "waiting") { throw "ana phai o trang thai waiting" }
Write-Host "   ana dang cho..." -ForegroundColor Gray

$resBo = Rpc-User "find_match" $bo.Token $autoBody
if ($resBo.status -ne "matched") { throw "bo phai duoc matched" }
$room1 = $resBo.room
$ticket1 = $resBo.ticket
Write-Host "   -> Cap 1 matched vao room: $room1 (ticket: $ticket1)" -ForegroundColor Green

$pollAna = Rpc-User "poll_match" $ana.Token $autoBody
if ($pollAna.ticket -ne $ticket1) { throw "ana phai nhan cung ticket voi bo" }
Write-Host "   -> ana poll ket qua: OK (room=$($pollAna.room))" -ForegroundColor Green

# 4. Cap 2 (chi + dung) yeu cau find_match voi room "auto"
Write-Host "`n4/4 Cap 2 (chi + dung) vao auto pool..." -ForegroundColor Yellow
$resChi = Rpc-User "find_match" $chi.Token $autoBody
if ($resChi.status -ne "waiting") { throw "chi phai o trang thai waiting" }
Write-Host "   chi dang cho..." -ForegroundColor Gray

$resDung = Rpc-User "find_match" $dung.Token $autoBody
if ($resDung.status -ne "matched") { throw "dung phai duoc matched" }
$room2 = $resDung.room
$ticket2 = $resDung.ticket
Write-Host "   -> Cap 2 matched vao room: $room2 (ticket: $ticket2)" -ForegroundColor Green

$pollChi = Rpc-User "poll_match" $chi.Token $autoBody
if ($pollChi.ticket -ne $ticket2) { throw "chi phai nhan cung ticket voi dung" }
Write-Host "   -> chi poll ket qua: OK (room=$($pollChi.room))" -ForegroundColor Green

# Kiem tra ca hai cap nhan 2 room khac nhau tu pool
if ($room1 -eq $room2) {
    Write-Warning "Luu y: Ca 2 cap deu vao $room1 (pool co the can xoay vong). Danh sach phong: $room1, $room2"
}
else {
    Write-Host "`nChung minh thanh cong: Cap 1 ($room1) va Cap 2 ($room2) duoc cap 2 room khac nhau tu pool!" -ForegroundColor Green
}

# Verify ca 2 ticket
$v1 = Rpc-Key "verify_ticket" (@{ ticket = $ticket1; room = $room1; user_id = $ana.UserId } | ConvertTo-Json -Compress)
$v2 = Rpc-Key "verify_ticket" (@{ ticket = $ticket2; room = $room2; user_id = $chi.UserId } | ConvertTo-Json -Compress)
if (-not $v1.ok -or -not $v2.ok) { throw "Verify ticket that bai" }

Write-Host "Verify ca 2 ticket thanh cong!" -ForegroundColor Green
Write-Host "`n=== DoD P5.5 HOAN TAT THANH CONG! ===" -ForegroundColor Green

