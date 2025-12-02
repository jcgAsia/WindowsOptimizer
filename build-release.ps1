# WindowsOptimizer 배포 스크립트
# 사용법: .\build-release.ps1 -Version "1.0.1"

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

# Squirrel.exe 경로 (버전에 따라 조정)
$squirrelExe  = Join-Path $env:USERPROFILE ".nuget\packages\clowd.squirrel\2.11.1\tools\Squirrel.exe"

Write-Host "==== WindowsOptimizer 빌드 (버전 $Version) ====" -ForegroundColor Cyan

# 1) csproj 버전 업데이트
Write-Host "[1/4] 버전 업데이트..." -ForegroundColor Yellow
$csprojContent = Get-Content $appProj -Raw
$csprojContent = $csprojContent -replace '<Version>.*?</Version>', "<Version>$Version</Version>"
Set-Content $appProj $csprojContent

# 2) dotnet publish
Write-Host "[2/4] dotnet publish..." -ForegroundColor Yellow
dotnet publish $appProj -c Release -r win-x64 -o $publishDir --self-contained false
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

# 3) Squirrel pack
Write-Host "[3/4] Squirrel pack..." -ForegroundColor Yellow
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

# 4) 결과 출력
Write-Host "[4/4] 완료!" -ForegroundColor Green
Write-Host ""
Write-Host "생성된 파일:" -ForegroundColor White
Get-ChildItem $releasesDir | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor Gray }
Write-Host ""
Write-Host "다음 단계:" -ForegroundColor Yellow

# 5) Git 커밋 & 푸시 (Releases 폴더가 git repo라고 가정)
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
