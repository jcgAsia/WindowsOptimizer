param(
    # 배포할 버전 번호. 예: 1.0.5
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

# ==== 경로 설정 (환경에 맞게 한 번만 확인/수정) ==========================
$solutionRoot = "E:\dev\alba\WindowsOptimizer"
$appProj      = Join-Path $solutionRoot "WindowsOptimizer.csproj"
$publishDir   = Join-Path $solutionRoot "publish"
$releasesDir  = Join-Path $solutionRoot "Releases"
$iconPath     = Join-Path $solutionRoot "Assets\app.ico"

# Clowd.Squirrel Squirrel.exe 경로 (버전은 설치된 거 확인해서 맞추기)
$squirrelExe  = Join-Path $env:USERPROFILE ".nuget\packages\clowd.squirrel\2.11.1\tools\Squirrel.exe"

# Git 브랜치/리모트 설정
$gitBranch = "main"
$gitRemote = "origin"
# =======================================================================

Write-Host "==== WindowsOptimizer 빌드 & 릴리즈 시작 (버전 $Version) ====" -ForegroundColor Cyan

# 1) dotnet publish
Write-Host "[1/3] dotnet publish 실행 중..." -ForegroundColor Cyan

dotnet publish $appProj `
    -c Release `
    -r win-x64 `
    -o $publishDir `
    --self-contained false

Write-Host "    -> publish 완료: $publishDir" -ForegroundColor Green

# 2) Squirrel pack
Write-Host "[2/3] Squirrel pack 실행 중..." -ForegroundColor Cyan

if (-not (Test-Path $squirrelExe)) {
    throw "Squirrel.exe를 찾을 수 없습니다: $squirrelExe`nClowd.Squirrel NuGet 패키지 버전/경로를 확인하세요."
}

# Releases 폴더가 없다면 생성
if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir | Out-Null
}

& $squirrelExe pack `
    --packId "WindowsOptimizer" `
    --packVersion $Version `
    --packAuthors "Jcg" `
    --packDirectory $publishDir `
    --releaseDir $releasesDir `
    --icon $iconPath

if ($LASTEXITCODE -ne 0) {
    throw "Squirrel pack 실패 (exit code $LASTEXITCODE)"
}

Write-Host "    -> Releases 생성/갱신 완료: $releasesDir" -ForegroundColor Green

# 3) Git 커밋 & 푸시 (Releases 폴더가 git repo라고 가정)
Write-Host "[3/3] Git 커밋 & 푸시..." -ForegroundColor Cyan

Push-Location $releasesDir

# 변경사항 있는지 체크
git status --porcelain | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Releases 폴더가 git 저장소가 아닙니다: $releasesDir"
}

$changes = git status --porcelain
if ([string]::IsNullOrWhiteSpace($changes)) {
    Write-Host "    -> 변경된 파일이 없습니다. 커밋/푸시는 건너뜁니다." -ForegroundColor Yellow
}
else {
    git add .

    git commit -m "Release $Version" | Out-Null

    git push $gitRemote $gitBranch

    Write-Host "    -> Git push 완료: $gitRemote/$gitBranch" -ForegroundColor Green
}

Pop-Location

Write-Host "==== 릴리즈 스크립트 완료 (버전 $Version) ====" -ForegroundColor Green
