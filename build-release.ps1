# WindowsOptimizer 배포 스크립트
# 사용법:
#   Live:   .\build-release.ps1 -Version "1.0.1"
#   Mockup: .\build-release.ps1 -Version "1.0.1" -Mockup

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [switch]$Mockup
)

$ErrorActionPreference = "Stop"

# ==== Code Signing Settings ==========================
$certThumbprint = "f382a7a6ddfd342f44ae9e0010a328bd487cede5"
$timestampUrl   = "http://ts.ssl.com"

# Find signtool.exe from Windows SDK, fallback to PATH
$signtoolExe = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($signtoolExe) {
    $signtoolPath = $signtoolExe.FullName
    Write-Host "    Found signtool: $signtoolPath" -ForegroundColor Gray
} else {
    $signtoolPath = "signtool"  # Fallback: rely on PATH
    Write-Host "    signtool not found in Windows SDK, using PATH fallback" -ForegroundColor Yellow
}

# Build signParams for Squirrel --signParams option
$signParams = "/tr $timestampUrl /td sha256 /fd sha256 /sha1 $certThumbprint"

# Helper function: sign a single file with error handling (non-fatal)
function Invoke-CodeSign {
    param([string]$FilePath)

    if (-not (Test-Path $FilePath)) {
        Write-Host "    [Sign] File not found, skipping: $FilePath" -ForegroundColor Yellow
        return $false
    }

    Write-Host "    [Sign] Signing: $(Split-Path $FilePath -Leaf)" -ForegroundColor Gray
    try {
        $signOutput = & $signtoolPath sign /tr $timestampUrl /td sha256 /fd sha256 /sha1 $certThumbprint $FilePath 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "    [Sign] WARNING: Signing failed for $(Split-Path $FilePath -Leaf) (exit code $LASTEXITCODE)" -ForegroundColor Yellow
            Write-Host "    [Sign] signtool output: $signOutput" -ForegroundColor Yellow
            Write-Host "    [Sign] Build will continue without signing. Ensure SafeNet token is connected for production builds." -ForegroundColor Yellow
            return $false
        }
        Write-Host "    [Sign] Signed successfully: $(Split-Path $FilePath -Leaf)" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "    [Sign] WARNING: Signing failed - $_" -ForegroundColor Yellow
        Write-Host "    [Sign] Build will continue without signing. Ensure SafeNet token is connected for production builds." -ForegroundColor Yellow
        return $false
    }
}

# Test signtool availability (non-fatal check)
if ($signtoolPath -ne "signtool") {
    $codeSigningAvailable = Test-Path $signtoolPath
} else {
    $codeSigningAvailable = $null -ne (Get-Command signtool -ErrorAction SilentlyContinue)
}

if ($codeSigningAvailable) {
    Write-Host "    Code signing: ENABLED (signtool available)" -ForegroundColor Green
} else {
    Write-Host "    Code signing: DISABLED (signtool not found)" -ForegroundColor Yellow
}
# ==========================================

# Git 설정
$gitRemote = "origin"
$gitBranch = "main"

# 경로 설정
$solutionRoot = $PSScriptRoot
$appProj      = Join-Path $solutionRoot "WindowsOptimizer.csproj"
$publishDir   = Join-Path $solutionRoot "publish"
$releasesDir  = Join-Path $solutionRoot "Releases"
$iconPath     = Join-Path $solutionRoot "Assets\setup.ico"

# Squirrel.exe 경로
$squirrelExe  = Join-Path $env:USERPROFILE ".nuget\packages\clowd.squirrel\2.11.1\tools\Squirrel.exe"

# PID 설정
$pid_value = if ($Mockup) { "pb000" } else { "pb001" }
$build_type = if ($Mockup) { "Mockup" } else { "Live" }

Write-Host "==== WindowsOptimizer 빌드 (버전 $Version, $build_type, PID: $pid_value) ====" -ForegroundColor Cyan

# 1) csproj 버전 업데이트
Write-Host "[1/4] 버전 업데이트..." -ForegroundColor Yellow
$csprojContent = Get-Content $appProj -Raw
$csprojContent = $csprojContent -replace '<Version>.*?</Version>', "<Version>$Version</Version>"
Set-Content $appProj $csprojContent

# 2) dotnet restore + publish
Write-Host "[2/4] dotnet restore & publish ($build_type)..." -ForegroundColor Yellow
dotnet restore $appProj
if ($LASTEXITCODE -ne 0) { throw "복원 실패" }
dotnet publish $appProj -c Release -o $publishDir --no-restore
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

# pid.txt 주입 (빌드 후 publish 폴더에 생성)
$pid_value | Out-File -FilePath (Join-Path $publishDir "pid.txt") -Encoding UTF8 -NoNewline
Write-Host "    -> PID: $pid_value (pid.txt 생성됨)" -ForegroundColor Gray

# 3) Squirrel pack
Write-Host "[3/4] Squirrel pack..." -ForegroundColor Yellow
if (-not (Test-Path $squirrelExe)) {
    throw "Squirrel.exe 없음: $squirrelExe"
}
if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir | Out-Null
}

$squirrelArgs = @(
    "pack"
    "--packId", "WindowsOptimizer"
    "--packVersion", $Version
    "--packAuthors", "JCG"
    "--packDirectory", $publishDir
    "--releaseDir", $releasesDir
    "--icon", $iconPath
    "--allowUnaware"
)

# Add code signing to Squirrel pack (signs exe/dll inside the package)
if ($codeSigningAvailable) {
    $squirrelArgs += "--signParams", $signParams
    Write-Host "    -> Code signing enabled for Squirrel pack" -ForegroundColor Gray
}

& $squirrelExe @squirrelArgs

if ($LASTEXITCODE -ne 0) { throw "Squirrel pack 실패" }

# Sign Setup.exe separately (Squirrel may not sign the Setup installer)
if ($codeSigningAvailable) {
    Write-Host "    [Post] Signing Setup.exe files..." -ForegroundColor Gray
    $setupFiles = Get-ChildItem $releasesDir -Filter "*Setup*.exe" -ErrorAction SilentlyContinue
    foreach ($setup in $setupFiles) {
        Invoke-CodeSign -FilePath $setup.FullName
    }
}

# 이전 버전 정리 (최근 2개 버전만 유지)
Write-Host "[3.5/4] 이전 버전 정리 (최근 2개 유지)..." -ForegroundColor Yellow

$allFullPkgs = Get-ChildItem $releasesDir -Filter "*-full.nupkg" | Sort-Object Name -Descending
if ($allFullPkgs.Count -gt 2) {
    $versionsToDelete = $allFullPkgs | Select-Object -Skip 2
    foreach ($pkg in $versionsToDelete) {
        $versionPattern = $pkg.Name -replace "-full\.nupkg$", ""
        $deltaFile = Join-Path $releasesDir "$versionPattern-delta.nupkg"

        # full 파일 삭제
        Remove-Item $pkg.FullName -Force
        Write-Host "    삭제: $($pkg.Name)" -ForegroundColor Gray

        # delta 파일 삭제 (있으면)
        if (Test-Path $deltaFile) {
            Remove-Item $deltaFile -Force
            Write-Host "    삭제: $versionPattern-delta.nupkg" -ForegroundColor Gray
        }
    }
    Write-Host "    -> $($versionsToDelete.Count)개 버전 삭제됨" -ForegroundColor Green
} else {
    Write-Host "    -> 정리할 버전 없음" -ForegroundColor Gray
}

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
    # 릴리즈 파일만 add (mapping.xml은 mapping_editor에서만 관리)
    git add RELEASES *.nupkg *Setup*.exe

    git commit -m "Release $Version" | Out-Null

    git push $gitRemote $gitBranch

    Write-Host "    -> Git push 완료: $gitRemote/$gitBranch" -ForegroundColor Green
}

Pop-Location

Write-Host "==== 릴리즈 스크립트 완료 (버전 $Version) ====" -ForegroundColor Green
