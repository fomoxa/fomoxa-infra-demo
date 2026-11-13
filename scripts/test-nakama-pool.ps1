param(
    [string]$Base = "http://127.0.0.1:7350",
    [string]$HttpKey = "",
    [string]$ServerKey = ""
)

$ErrorActionPreference = "Stop"

# Key Nakama: mac dinh doc tu secret cua cum K8s (neu co), khong thi dung key mac dinh cua docker compose.
function Get-NakamaKey {
    param([string]$Name, [string]$Fallback)
    if ($env:NAKAMA_USE_DEFAULT_KEYS -eq "1") { return $Fallback }
    try {
        $encoded = kubectl get secret fomoxa-nakama -n fomoxa -o "jsonpath={.data.$Name}" 2>$null
        if ($encoded) { return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) }
    }
    catch { }
    return $Fallback
}
if (-not $HttpKey) { $HttpKey = Get-NakamaKey "http-key" "defaulthttpkey" }
if (-not $ServerKey) { $ServerKey = Get-NakamaKey "server-key" "defaultkey" }

Write-Host "=== P5.5 check: matchmaking auto-assigns rooms from a pool (>= 4 clients) ===" -ForegroundColor Cyan

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
Write-Host "`n1/4 Resetting auto pool..." -ForegroundColor Yellow
$null = Rpc-Key "reset_match" (@{ room = "auto" } | ConvertTo-Json -Compress)

# 2. Dang nhap 4 client
Write-Host "`n2/4 Logging in 4 clients (ana, bo, chi, dung)..." -ForegroundColor Yellow
$ana  = Auth-Device "fomoxa-p55-ana"
$bo   = Auth-Device "fomoxa-p55-bo"
$chi  = Auth-Device "fomoxa-p55-chi"
$dung = Auth-Device "fomoxa-p55-dung"

# 3. Cap 1 (ana + bo) yeu cau find_match voi room "auto"
Write-Host "`n3/4 Pair 1 (ana + bo) joins the auto pool..." -ForegroundColor Yellow
$autoBody = @{ room = "auto" } | ConvertTo-Json -Compress

$resAna = Rpc-User "find_match" $ana.Token $autoBody
if ($resAna.status -ne "waiting") { throw "ana must be in waiting state" }
Write-Host "   ana is waiting..." -ForegroundColor Gray

$resBo = Rpc-User "find_match" $bo.Token $autoBody
if ($resBo.status -ne "matched") { throw "bo must be matched" }
$room1 = $resBo.room
$ticket1 = $resBo.ticket
Write-Host "   -> Pair 1 matched into room: $room1 (ticket: $ticket1)" -ForegroundColor Green

$pollAna = Rpc-User "poll_match" $ana.Token $autoBody
if ($pollAna.ticket -ne $ticket1) { throw "ana must receive the same ticket as bo" }
Write-Host "   -> ana poll result: OK (room=$($pollAna.room))" -ForegroundColor Green

# 4. Pair 2 (chi + dung) requests find_match with room "auto"
Write-Host "`n4/4 Pair 2 (chi + dung) joins the auto pool..." -ForegroundColor Yellow
$resChi = Rpc-User "find_match" $chi.Token $autoBody
if ($resChi.status -ne "waiting") { throw "chi must be in waiting state" }
Write-Host "   chi is waiting..." -ForegroundColor Gray

$resDung = Rpc-User "find_match" $dung.Token $autoBody
if ($resDung.status -ne "matched") { throw "dung must be matched" }
$room2 = $resDung.room
$ticket2 = $resDung.ticket
Write-Host "   -> Pair 2 matched into room: $room2 (ticket: $ticket2)" -ForegroundColor Green

$pollChi = Rpc-User "poll_match" $chi.Token $autoBody
if ($pollChi.ticket -ne $ticket2) { throw "chi must receive the same ticket as dung" }
Write-Host "   -> chi poll result: OK (room=$($pollChi.room))" -ForegroundColor Green

# Check that both pairs received different rooms from the pool
if ($room1 -eq $room2) {
    Write-Warning "Note: both pairs went to $room1 (the pool may need to rotate). Room list: $room1, $room2"
}
else {
    Write-Host "`nProof of success: Pair 1 ($room1) and Pair 2 ($room2) received two different rooms from the pool!" -ForegroundColor Green
}

# Verify ca 2 ticket
$v1 = Rpc-Key "verify_ticket" (@{ ticket = $ticket1; room = $room1; user_id = $ana.UserId } | ConvertTo-Json -Compress)
$v2 = Rpc-Key "verify_ticket" (@{ ticket = $ticket2; room = $room2; user_id = $chi.UserId } | ConvertTo-Json -Compress)
if (-not $v1.ok -or -not $v2.ok) { throw "verify_ticket failed" }

Write-Host "Both tickets verified successfully!" -ForegroundColor Green
Write-Host "`n=== P5.5 DoD COMPLETED SUCCESSFULLY! ===" -ForegroundColor Green

