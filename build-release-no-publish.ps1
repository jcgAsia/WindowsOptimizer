# WindowsOptimizer 배포 스크립트 (Publish 스킵 - 서명된 exe 사용)
#
# 사용법:
#   .\build-release-no-publish.ps1 -Version "1.1.53"
#
# 사전 준비:
#   1. dotnet publish 실행하여 publish 폴더에 exe 생성
#   2. WindowsOptimizer.exe를 코드 서명
#   3. 서명된 exe를 publish 폴더에 복사
#   4. 이 스크립트 실행

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

# 경로 설정
$solutionRoot = $PSScriptRoot
$appProj      = Join-Path $solutionRoot "WindowsOptimizer.csproj"
$publishDir   = Join-Path $solutionRoot "publish"
$releasesDir  = Join-Path $solutionRoot "Releases"
$iconPath     = Join-Path $solutionRoot "Assets\setup.ico"
$exePath      = Join-Path $publishDir "WindowsOptimizer.exe"

# Squirrel.exe 경로
$squirrelExe  = Join-Path $env:USERPROFILE ".nuget\packages\clowd.squirrel\2.11.1\tools\Squirrel.exe"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  WindowsOptimizer 배포 (Publish 스킵, 버전 $Version)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# 1) 사전 검증
Write-Host "[1/4] 사전 검증..." -ForegroundColor Yellow

if (-not (Test-Path $publishDir)) {
    throw "publish 폴더가 없습니다: $publishDir`n먼저 'dotnet publish' 실행 필요"
}

if (-not (Test-Path $exePath)) {
    throw "WindowsOptimizer.exe가 없습니다: $exePath`n먼저 'dotnet publish' 실행 필요"
}

# exe 서명 확인 (선택적)
$sig = Get-AuthenticodeSignature $exePath
if ($sig.Status -eq "Valid") {
    Write-Host "    -> WindowsOptimizer.exe 서명 확인: OK ($($sig.SignerCertificate.Subject))" -ForegroundColor Green
} elseif ($sig.Status -eq "NotSigned") {
    Write-Host "    -> 경고: WindowsOptimizer.exe가 서명되지 않았습니다!" -ForegroundColor Red
    $continue = Read-Host "    계속하시겠습니까? (Y/N)"
    if ($continue -ne "Y" -and $continue -ne "y") {
        throw "사용자가 취소했습니다."
    }
} else {
    Write-Host "    -> 경고: 서명 상태: $($sig.Status)" -ForegroundColor Yellow
}

Write-Host "    -> publish 폴더 확인: OK" -ForegroundColor Green

# 2) csproj 버전 업데이트
Write-Host "[2/4] 버전 업데이트..." -ForegroundColor Yellow
$csprojContent = Get-Content $appProj -Raw
$csprojContent = $csprojContent -replace '<Version>.*?</Version>', "<Version>$Version</Version>"
Set-Content $appProj $csprojContent
Write-Host "    -> csproj 버전: $Version" -ForegroundColor Green

# 3) Squirrel pack (dotnet publish 스킵)
Write-Host "[3/4] Squirrel pack (서명된 exe 사용)..." -ForegroundColor Yellow

if (-not (Test-Path $squirrelExe)) {
    throw "Squirrel.exe 없음: $squirrelExe"
}

if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir | Out-Null
}

& $squirrelExe pack `
    --packId "WindowsOptimizer" `
    --packVersion $Version `
    --packAuthors "JCG" `
    --packDirectory $publishDir `
    --releaseDir $releasesDir `
    --icon $iconPath `
    --allowUnaware

if ($LASTEXITCODE -ne 0) { throw "Squirrel pack 실패" }

Write-Host "    -> Squirrel pack 완료" -ForegroundColor Green

# 4) 결과 출력
Write-Host "[4/4] 완료!" -ForegroundColor Green
Write-Host ""
Write-Host "생성된 파일:" -ForegroundColor White
Get-ChildItem $releasesDir | ForEach-Object {
    $fileSize = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  - $($_.Name) ($fileSize MB)" -ForegroundColor Gray
}

# Setup.exe 서명 확인
$setupExe = Join-Path $releasesDir "WindowsOptimizerSetup.exe"
if (Test-Path $setupExe) {
    $setupSig = Get-AuthenticodeSignature $setupExe
    Write-Host ""
    if ($setupSig.Status -eq "NotSigned") {
        Write-Host "주의: WindowsOptimizerSetup.exe도 서명이 필요합니다!" -ForegroundColor Yellow
        Write-Host "  signtool sign /sha1 [THUMBPRINT] /tr http://timestamp.digicert.com /td sha256 `"$setupExe`"" -ForegroundColor Gray
    }
}

# 5) Git 커밋 & 푸시
Write-Host ""
Write-Host "[5/5] Git 커밋 & 푸시..." -ForegroundColor Yellow

Push-Location $releasesDir

git status --porcelain | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "    -> Releases 폴더가 git 저장소가 아닙니다. 수동 업로드 필요" -ForegroundColor Yellow
    Pop-Location
} else {
    $changes = git status --porcelain
    if ([string]::IsNullOrWhiteSpace($changes)) {
        Write-Host "    -> 변경된 파일이 없습니다." -ForegroundColor Yellow
    } else {
        git add .
        git commit -m "Release $Version"
        git push
        Write-Host "    -> Git push 완료" -ForegroundColor Green
    }
    Pop-Location
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  릴리즈 완료! (버전 $Version)" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
