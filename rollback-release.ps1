# WindowsOptimizer 롤백 스크립트
# 사용법: .\rollback-release.ps1
# 최신 버전을 삭제하고 직전 버전으로 롤백

$ErrorActionPreference = "Stop"

# 경로 설정
$solutionRoot = $PSScriptRoot
$releasesDir  = Join-Path $solutionRoot "Releases"
$iconPath     = Join-Path $solutionRoot "Assets\setup.ico"
$squirrelExe  = Join-Path $env:USERPROFILE ".nuget\packages\clowd.squirrel\2.11.1\tools\Squirrel.exe"

Write-Host "==== WindowsOptimizer 롤백 ====" -ForegroundColor Cyan

# 1) 현재 버전 확인
Write-Host "[1/5] 현재 버전 확인..." -ForegroundColor Yellow

$fullPkgs = Get-ChildItem $releasesDir -Filter "*-full.nupkg" | Sort-Object {
    # 버전 번호로 정렬 (1.1.68 > 1.1.67)
    if ($_.Name -match "(\d+)\.(\d+)\.(\d+)") {
        [int]$matches[1] * 10000 + [int]$matches[2] * 100 + [int]$matches[3]
    } else { 0 }
} -Descending

if ($fullPkgs.Count -lt 2) {
    Write-Host "    오류: 롤백할 버전이 없습니다. (최소 2개 버전 필요)" -ForegroundColor Red
    Write-Host "    현재 버전: $($fullPkgs.Count)개" -ForegroundColor Red
    exit 1
}

$latestPkg = $fullPkgs[0]
$previousPkg = $fullPkgs[1]

# 버전 추출
$latestVersion = if ($latestPkg.Name -match "(\d+\.\d+\.\d+)") { $matches[1] } else { "unknown" }
$previousVersion = if ($previousPkg.Name -match "(\d+\.\d+\.\d+)") { $matches[1] } else { "unknown" }

Write-Host "    현재 최신: $latestVersion" -ForegroundColor White
Write-Host "    롤백 대상: $previousVersion" -ForegroundColor White

# 확인 — 버전 문자열 재입력 게이트 (build-release push 게이트와 동일 방식)
# 피드 재게시 = 전 기기 자동업데이트 확산이므로 y/N 대신 롤백 대상 버전을 정확히 재입력해야 진행
Write-Host "    최종 게이트: 피드 재게시는 기존 설치 전 기기 자동업데이트에 영향을 줍니다." -ForegroundColor Yellow
$reEntry = Read-Host "    롤백 대상 버전 문자열을 정확히 재입력하세요 (기대값: $previousVersion)"
if ($null -eq $reEntry) { $reEntry = "" }
if ($reEntry.Trim() -cne $previousVersion) {
    Write-Host "    버전 불일치('$($reEntry.Trim())' != '$previousVersion') - 롤백 중단" -ForegroundColor Red
    exit 1
}
Write-Host "    버전 재입력 일치 - 롤백 진행" -ForegroundColor Green

# 2) 최신 버전 파일 삭제
Write-Host "[2/5] 최신 버전 삭제..." -ForegroundColor Yellow

$latestPattern = "WindowsOptimizer-$latestVersion"
$filesToDelete = Get-ChildItem $releasesDir -Filter "$latestPattern*"

foreach ($file in $filesToDelete) {
    Remove-Item $file.FullName -Force
    Write-Host "    삭제: $($file.Name)" -ForegroundColor Gray
}

# 3) RELEASES 파일 업데이트
Write-Host "[3/5] RELEASES 파일 업데이트..." -ForegroundColor Yellow

$releasesFile = Join-Path $releasesDir "RELEASES"
$releasesContent = Get-Content $releasesFile

# 실제 존재하는 nupkg 파일만 유지
$existingPkgs = Get-ChildItem $releasesDir -Filter "*.nupkg" | ForEach-Object { $_.Name }
$newReleasesContent = $releasesContent | Where-Object {
    $line = $_
    if ([string]::IsNullOrWhiteSpace($line)) { return $false }
    $parts = $line -split "\s+"
    if ($parts.Count -ge 2) {
        $filename = $parts[1]
        return $existingPkgs -contains $filename
    }
    return $false
}

Set-Content $releasesFile $newReleasesContent -Encoding UTF8
Write-Host "    RELEASES 파일 정리 완료" -ForegroundColor Green

# 4) Setup.exe 재생성
Write-Host "[4/5] Setup.exe 재생성..." -ForegroundColor Yellow

if (-not (Test-Path $squirrelExe)) {
    Write-Host "    경고: Squirrel.exe 없음 - Setup.exe 재생성 생략" -ForegroundColor Yellow
} else {
    & $squirrelExe releasify `
        --package $previousPkg.FullName `
        --releaseDir $releasesDir `
        --icon $iconPath

    if ($LASTEXITCODE -eq 0) {
        Write-Host "    Setup.exe 재생성 완료" -ForegroundColor Green
    } else {
        Write-Host "    경고: Setup.exe 재생성 실패 (기존 Setup.exe 유지)" -ForegroundColor Yellow
    }
}

# 5) Git 커밋 & 푸시
Write-Host "[5/5] Git 커밋 & 푸시..." -ForegroundColor Yellow

Push-Location $releasesDir

$changes = git status --porcelain
if ([string]::IsNullOrWhiteSpace($changes)) {
    Write-Host "    변경사항 없음" -ForegroundColor Gray
} else {
    # 화이트리스트 add — 피드 산출물만 스테이징 (임시/무관 파일 커밋 방지)
    git add RELEASES *.nupkg *Setup*.exe
    git commit -m "Rollback: $latestVersion -> $previousVersion"
    git push

    if ($LASTEXITCODE -eq 0) {
        Write-Host "    Git push 완료" -ForegroundColor Green
    } else {
        Write-Host "    경고: Git push 실패" -ForegroundColor Yellow
    }
}

Pop-Location

Write-Host ""
Write-Host "==== 롤백 완료: $previousVersion ====" -ForegroundColor Green
Write-Host ""
Write-Host "남은 버전:" -ForegroundColor White
Get-ChildItem $releasesDir -Filter "*-full.nupkg" | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor Gray }
