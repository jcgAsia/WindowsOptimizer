# WindowsOptimizer Squirrel 배포 스크립트
# 사용법: .\build-release.ps1 -Version "1.0.1"

param(
    [Parameter(Mandatory=$false)]
    [string]$Version = "1.0.0",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputDir = ".\Releases",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"

# 설정
$ProjectName = "WindowsOptimizer"
$AppName = "WindowsOptimizer"
$AppTitle = "Windows System Optimizer"
$Publisher = "YourCompany"
$IconPath = "Assets\app.ico"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  $AppTitle 배포 빌드" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# 1. 출력 디렉토리 생성
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
    Write-Host "[1/5] 출력 디렉토리 생성: $OutputDir" -ForegroundColor Green
}

# 2. 프로젝트 빌드
if (!$SkipBuild) {
    Write-Host "[2/5] Release 빌드 중..." -ForegroundColor Yellow
    
    # 버전 업데이트
    $csprojPath = "$ProjectName.csproj"
    if (Test-Path $csprojPath) {
        $content = Get-Content $csprojPath -Raw
        $content = $content -replace '<Version>.*</Version>', "<Version>$Version</Version>"
        Set-Content $csprojPath $content
        Write-Host "  버전 업데이트: $Version" -ForegroundColor Gray
    }
    
    # 빌드
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "빌드 실패!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  빌드 완료" -ForegroundColor Green
} else {
    Write-Host "[2/5] 빌드 스킵" -ForegroundColor Gray
}

# 3. NuGet 패키지 생성 (Squirrel용)
Write-Host "[3/5] NuGet 패키지 생성 중..." -ForegroundColor Yellow

$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>$AppName</id>
    <version>$Version</version>
    <title>$AppTitle</title>
    <authors>$Publisher</authors>
    <owners>$Publisher</owners>
    <description>$AppTitle</description>
    <copyright>Copyright © $(Get-Date -Format yyyy) $Publisher</copyright>
  </metadata>
  <files>
    <file src="bin\Release\net48\**\*.*" target="lib\net48" exclude="**\*.pdb;**\*.xml" />
  </files>
</package>
"@

$nuspecPath = "$AppName.nuspec"
Set-Content $nuspecPath $nuspecContent
Write-Host "  nuspec 생성: $nuspecPath" -ForegroundColor Gray

# NuGet 패키지 생성
nuget pack $nuspecPath -OutputDirectory $OutputDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "NuGet 패키지 생성 실패!" -ForegroundColor Red
    exit 1
}
Write-Host "  NuGet 패키지 생성 완료" -ForegroundColor Green

# 4. Squirrel 패키지 생성
Write-Host "[4/5] Squirrel 패키지 생성 중..." -ForegroundColor Yellow

$nupkgPath = "$OutputDir\$AppName.$Version.nupkg"
$squirrelOutput = "$OutputDir\Squirrel"

# Squirrel 실행
if (!(Test-Path $squirrelOutput)) {
    New-Item -ItemType Directory -Path $squirrelOutput | Out-Null
}

# Clowd.Squirrel 사용
$squirrelExe = (Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\clowd.squirrel" -Recurse -Filter "Squirrel.exe" | Select-Object -First 1).FullName

if ($squirrelExe -and (Test-Path $squirrelExe)) {
    & $squirrelExe pack `
        --packId $AppName `
        --packVersion $Version `
        --packDirectory "bin\Release\net48" `
        --releaseDir $squirrelOutput `
        --icon $IconPath `
        --splashImage $null `
        --setupIcon $IconPath
        
    Write-Host "  Squirrel 패키지 생성 완료" -ForegroundColor Green
} else {
    Write-Host "  Squirrel.exe를 찾을 수 없습니다. 수동으로 설치하세요." -ForegroundColor Yellow
    Write-Host "  명령: dotnet tool install -g Clowd.Squirrel" -ForegroundColor Gray
}

# 5. 배포 파일 정리
Write-Host "[5/5] 배포 파일 정리..." -ForegroundColor Yellow

$releaseFiles = @{
    "Setup.exe" = "$squirrelOutput\${AppName}Setup.exe"
    "RELEASES" = "$squirrelOutput\RELEASES"
    "nupkg" = "$squirrelOutput\$AppName-$Version-full.nupkg"
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  배포 완료!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "생성된 파일:" -ForegroundColor White

if (Test-Path $squirrelOutput) {
    Get-ChildItem $squirrelOutput | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "서버 업로드 파일 (PlanB 8. 서버 파일 구조):" -ForegroundColor Yellow
Write-Host "  /planb/version.xml" -ForegroundColor Gray
Write-Host "  /planb/mapping.xml" -ForegroundColor Gray
Write-Host "  /planb/${AppName}_v${Version}.exe" -ForegroundColor Gray

# version.xml 샘플 생성
$versionXml = @"
<?xml version="1.0" encoding="utf-8"?>
<version>
  <number>$Version</number>
  <url>https://your-server.com/planb/${AppName}_v${Version}.exe</url>
  <checksum>SHA256_HASH_HERE</checksum>
</version>
"@

$versionXmlPath = "$OutputDir\version.xml"
Set-Content $versionXmlPath $versionXml
Write-Host ""
Write-Host "version.xml 샘플 생성: $versionXmlPath" -ForegroundColor Green

Write-Host ""
Write-Host "완료!" -ForegroundColor Green
