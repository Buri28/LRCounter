# LRCounter 自動テスト：ビルド → DLL を入れる → fpfc で起動 → 曲を自動で始め、決めた秒数後に集計を確かめて終了。
# 結果は <インスタンス>\UserData\LRCounter\AutoTest\ の status.txt（CHECK / RESULT 行）と shot.png。
# 何も操作しないのでノーツは全部 Miss。曲の途中で終了するので、スコアの提出・片手ベストの保存は起きない。
#
# 例: powershell -ExecutionPolicy Bypass -File scripts\autotest.ps1
#     powershell -ExecutionPolicy Bypass -File scripts\autotest.ps1 -Version 1.40.8 -Song "Magic"
param(
    [string]$Version = "1.44.1",
    [string]$InstancesDir = "D:\BSManager\BSInstances",
    [string]$Song = "Magic",
    [string]$Mapper = "",
    [string]$Diff = "Expert",
    [double]$Duration = 130,
    [double]$FakeStar = 5,
    [double]$StarDelay = 10,
    [double]$AutoplayAt = 0,   # 曲が始まってからこの秒数でオートパイロット（F8）に切り替える。0 なら全部 Miss のまま
    [int]$TimeoutSec = 400,
    # 設定を一時的に変える。終了後に元へ戻す。AccBarMin=100 は範囲外の値を直す処理の確認用（90 になれば OK）
    [string[]]$Set = @("DebugLogging=true", "HideDebugLabel=false", "DropSoundMissEnabled=true",
                       "DropSoundMissStopAfterFail=false", "DropSoundMissLeftVolume=0", "DropSoundMissRightVolume=0",
                       "AccBarMin=100"),
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$inst = Join-Path $InstancesDir $Version
$exe = Join-Path $inst "Beat Saber.exe"
$outDir = Join-Path $inst "UserData\LRCounter\AutoTest"
$status = Join-Path $outDir "status.txt"

if (Get-Process -Name "Beat Saber" -ErrorAction SilentlyContinue) {
    Write-Host "Beat Saber is already running. Close it first."
    exit 2
}

if (-not $NoBuild) {
    dotnet build (Join-Path $repo "LRCounter\LRCounter.csproj") -c Debug -nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Copy-Item (Join-Path $repo "LRCounter\bin\Debug\net48\net48\LRCounter.dll") (Join-Path $inst "Plugins\") -Force

# 設定と片手ベストのファイルを退避し、最後に必ず戻す（途中で止まっても finally で戻す）。
$cfg = Join-Path $inst "UserData\LRCounter.json"
$bests = Join-Path $inst "UserData\LRCounter_HandBests.txt"
$backups = @{ $cfg = "$cfg.autotest-backup"; $bests = "$bests.autotest-backup" }
foreach ($k in $backups.Keys) {
    if (Test-Path $backups[$k]) {
        # 前回が途中で止まって退避ファイルが残っている＝こちらが本当の元のファイル。先に戻す。
        Move-Item $backups[$k] $k -Force
        Write-Host "Restored $k left over from a previous run."
    }
}
$bestsHashBefore = if (Test-Path $bests) { (Get-FileHash $bests).Hash } else { "" }

$result = "TIMEOUT"
try {
    foreach ($k in $backups.Keys) { if (Test-Path $k) { Copy-Item $k $backups[$k] -Force } }

    if ($Set.Count -gt 0) {
        $json = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
        $names = $json.PSObject.Properties.Name
        foreach ($kv in $Set) {
            $k, $v = $kv.Split("=", 2)
            if ($names -notcontains $k) { throw "Unknown setting '$k' (not in LRCounter.json)" }
            if ($v -eq "true") { $v = $true } elseif ($v -eq "false") { $v = $false }
            elseif ($v -match '^-?[0-9]+(\.[0-9]+)?$') { $v = [double]$v }
            $json.$k = $v
            Write-Host "Set: $k = $v"
        }
        [IO.File]::WriteAllText($cfg, ($json | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
    }

    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    New-Item -ItemType Directory -Force $outDir | Out-Null

    # BSManager と同じ起動（--no-yeet fpfc）。Steam 版なので AppId を環境変数で渡す。
    $env:SteamAppId = "620980"
    $env:SteamGameId = "620980"
    $env:SteamOverlayGameId = "620980"
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $argList = @("--no-yeet", "fpfc", "--lrcounter-autotest", "--lrcounter-diff=$Diff",
        "--lrcounter-duration=$($Duration.ToString($inv))",
        "--lrcounter-fake-star=$($FakeStar.ToString($inv))", "--lrcounter-star-delay=$($StarDelay.ToString($inv))")
    if ($AutoplayAt -gt 0) { $argList += "--lrcounter-autoplay-at=$($AutoplayAt.ToString($inv))" }
    if ($Mapper -ne "") { $argList += "`"--lrcounter-mapper=$Mapper`"" }
    if ($Song -ne "") { $argList += "`"--lrcounter-song=$Song`"" }
    Write-Host "Launch: `"$exe`" $($argList -join ' ')"
    $proc = Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory $inst -PassThru

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if (Test-Path $status) {
            $last = (Get-Content $status -Encoding UTF8 | Select-Object -Last 1)
            if ($last -match " DONE$") { $result = "DONE"; break }
            if ($last -match " FAIL ") { $result = "FAIL"; break }
        }
        if ($proc.HasExited) { $result = "EXITED"; break }
    }
}
finally {
    # ゲームの終了を最大30秒待つ（DONE なら自分で終了する）。残っていたら止める。
    $waitUntil = (Get-Date).AddSeconds(30)
    while ((Get-Process -Name "Beat Saber" -ErrorAction SilentlyContinue) -and (Get-Date) -lt $waitUntil) { Start-Sleep -Seconds 1 }
    $bs = Get-Process -Name "Beat Saber" -ErrorAction SilentlyContinue
    if ($bs) { $bs | Stop-Process -Force; Write-Host "Beat Saber was stopped by the script."; Start-Sleep -Seconds 2 }

    $cfgAfter = if (Test-Path $cfg) { Get-Content $cfg -Raw -Encoding UTF8 } else { "" }
    $bestsHashAfter = if (Test-Path $bests) { (Get-FileHash $bests).Hash } else { "" }
    foreach ($k in $backups.Keys) {
        if (Test-Path $backups[$k]) { Move-Item $backups[$k] $k -Force }
    }
    Write-Host "Config and hand bests restored."
}

Write-Host "RESULT: $result"
if (Test-Path $status) { Get-Content $status -Encoding UTF8 }
# 範囲外の値を直す処理は、BSIPA が直した値をファイルにも書き戻す
if ($cfgAfter -match '"AccBarMin":\s*([0-9.]+)') { Write-Host "CONFIG FILE after run: AccBarMin=$($Matches[1])" }
Write-Host ("HANDBESTS unchanged during run: " + ($bestsHashBefore -eq $bestsHashAfter))
Get-ChildItem $outDir -Filter "*.png" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "PNG: $($_.FullName)" }

# LRCounter のログ（ゲーム終了後はロックが外れている）。
$logFile = Join-Path $inst "Logs\_latest.log"
if (Test-Path $logFile) {
    Select-String -Path $logFile -Pattern "LRCounter|ScoreSaberApi" |
        Where-Object { $_.Line -notmatch "\[autotest\]" } |
        ForEach-Object { $_.Line.Substring(0, [Math]::Min(300, $_.Line.Length)) } | Select-Object -First 60
}
if ($result -eq "DONE") { exit 0 } else { exit 1 }
