param(
    [string]$Base = "http://127.0.0.1:7350",
    [string]$Room = "arena:1",
    [string]$HttpKey = "defaulthttpkey",
    [string]$ServerKey = "defaultkey",
    [string]$UserA = "fomoxa-test-ana",
    [string]$UserB = "fomoxa-test-bo"
)

$ErrorActionPreference = "Stop"

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

    # PowerShell 5.1 lam hong dau ngoac kep khi truyen -d truc tiep, nen ghi body ra file.
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

$roomBody = @{ room = $Room } | ConvertTo-Json -Compress

Write-Host "1) dang nhap device A/B"
$ana = Auth-Device $UserA
$bo = Auth-Device $UserB
"   ana = $($ana.UserId)"
"   bo  = $($bo.UserId)"

Write-Host "2) reset queue $Room"
$null = Rpc-Key "reset_match" $roomBody

Write-Host "3) ana find_match"
$a = Rpc-User "find_match" $ana.Token $roomBody
"   -> $($a | ConvertTo-Json -Compress)"

if ($a.status -ne "waiting") { throw "ana phai o trang thai waiting" }

Write-Host "4) bo find_match"
$b = Rpc-User "find_match" $bo.Token $roomBody
"   -> $($b | ConvertTo-Json -Compress)"

if ($b.status -ne "matched") { throw "bo phai duoc matched" }

Write-Host "5) ana poll_match"
$poll = Rpc-User "poll_match" $ana.Token $roomBody
"   -> $($poll | ConvertTo-Json -Compress)"

if ($poll.status -ne "matched") { throw "ana phai thay ket qua matched" }
if ($poll.ticket -ne $b.ticket) { throw "hai client phai nhan cung ticket" }

Write-Host "6) backend verify_ticket"
$verify = Rpc-Key "verify_ticket" (@{ ticket = $poll.ticket; room = $Room; user_id = $ana.UserId } | ConvertTo-Json -Compress)
"   -> ok=$($verify.ok) $($verify.body | ConvertTo-Json -Compress)"
if (-not $verify.ok) { throw "verify_ticket phai thanh cong" }

Write-Host "7) verify voi user khac (phai bi tu choi)"
$stranger = Auth-Device "fomoxa-test-stranger"
$bad = Rpc-Key "verify_ticket" (@{ ticket = $poll.ticket; room = $Room; user_id = $stranger.UserId } | ConvertTo-Json -Compress)
"   -> ok=$($bad.ok) $($bad.body)"
if ($bad.ok) { throw "user ngoai phai bi tu choi" }

Write-Host "8) verify ticket bia (phai bi tu choi)"
$fake = Rpc-Key "verify_ticket" (@{ ticket = "00000000-0000-0000-0000-000000000000"; room = $Room; user_id = $ana.UserId } | ConvertTo-Json -Compress)
"   -> ok=$($fake.ok)"
if ($fake.ok) { throw "ticket bia phai bi tu choi" }

Write-Host ""
Write-Host "OK: 2 client ghep dung room, ticket dung, user ngoai va ticket bia bi chan"
