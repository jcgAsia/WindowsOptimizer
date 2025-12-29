# Chrome 프로세스 종료 확인
$chrome = Get-Process -Name chrome -ErrorAction SilentlyContinue
if ($chrome) {
    Write-Host "Chrome이 실행 중입니다. 먼저 Chrome을 완전히 종료해주세요." -ForegroundColor Red
    exit
}

$chromeDir = [Environment]::GetFolderPath('LocalApplicationData') + '\Google\Chrome\User Data'

# 모든 프로필 검사 및 수정
@('Default', 'Profile 1', 'Profile 2') | ForEach-Object {
    $prefFile = Join-Path $chromeDir "$_\Preferences"
    if (Test-Path $prefFile) {
        $content = Get-Content $prefFile -Raw -Encoding UTF8

        # window_placement에서 left 값 찾기
        if ($content -match '"window_placement":\s*\{[^}]*"left"\s*:\s*(-?\d+)') {
            $left = [int]$Matches[1]
            Write-Host "=== $_ ===" -ForegroundColor Cyan
            Write-Host "  현재 left 값: $left"

            # 화면 밖이면 수정 (left가 1920보다 크거나 음수이면)
            if ($left -gt 1920 -or $left -lt -100) {
                Write-Host "  창이 화면 밖에 있습니다! 수정 중..." -ForegroundColor Yellow

                # left를 100으로, top을 100으로 변경
                $content = $content -replace '"left"\s*:\s*(-?\d+)', '"left":100'
                $content = $content -replace '"top"\s*:\s*(-?\d+)', '"top":100'
                $content = $content -replace '"right"\s*:\s*(-?\d+)', '"right":1200'
                $content = $content -replace '"bottom"\s*:\s*(-?\d+)', '"bottom":800'

                # 백업 생성
                Copy-Item $prefFile "$prefFile.bak" -Force

                # 수정된 내용 저장
                [System.IO.File]::WriteAllText($prefFile, $content, [System.Text.Encoding]::UTF8)

                Write-Host "  수정 완료! (백업: $prefFile.bak)" -ForegroundColor Green
            } else {
                Write-Host "  정상 범위입니다." -ForegroundColor Green
            }
        }
    }
}

Write-Host ""
Write-Host "작업 완료. Chrome을 다시 실행해보세요." -ForegroundColor Cyan
