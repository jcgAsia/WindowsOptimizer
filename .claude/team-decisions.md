# Team Decisions Log

> This file is the persistent memory for Team Agent Mode.
> Every session appends below. Old sessions can be archived when the file gets too long.

---

## 프로젝트
- 경로: D:\dev\WindowsOptimizer
- 유형: C# WPF .NET (MVVM, Squirrel 배포)
- 규칙: CLAUDE.md 참조, 불변성/TDD/작은함수 원칙

## 미션
- 원본 요청: 재설치 시 install 카운팅 중복 방지. pid.txt 체크 방식처럼 이미 설치된 상태에서 재설치하면 LogMainUpdateAsync로 전송하도록 수정. CountingBaseUrl과 BustabccLogUrl 중 안 쓰는 것 정리.
- 작업 2개:
  1. OnAppInstall에서 재설치 감지 → install 대신 update 로그 전송
  2. 사용하지 않는 URL 상수 정리

## 작업 분석
- 작업 유형: 버그 수정 + 리팩토링
- 복잡도: 보통
- 구성 팀원: 탐색가(explorer) → 개발자(general-purpose) → 검토자(critic)
- 핵심 파일: Services/UpdateService.cs, Services/CountingService.cs, Services/BustabccLoggingService.cs, GlobalConfig 관련 파일

## 탐색 결과 핵심
- **OnAppInstall()**: 신규설치 + 재설치 모두 호출됨. 현재 재설치 판단 로직 없음.
- **재설치 판단법**: 레지스트리 `HKCU\SOFTWARE\WindowsOptimizer\pid` 존재 여부로 판단 (방법 A 채택)
  - PID 있음 → 재설치 → LogMainUpdateAsync
  - PID 없음 → 신규설치 → LogMainInstallAsync
- **CountingBaseUrl**: 값이 `"https://your-counting-server.com/api/count"` (플레이스홀더). 실제 동작 안 함. **데드 코드**.
- **BustabccLogUrl**: 값이 `"https://bustabcc.net/PRG/lg_read.php"` (실제 운영 서버). 모든 이벤트 커버.
- **CountingService**: LogUpdateAsync도 없고, URL도 플레이스홀더. **제거 대상**.
- **BustabccLoggingService**: LogMainUpdateAsync() 이미 존재. 바로 사용 가능.

## 설계 결정

### 작업 1: 재설치 시 update 로그 전송
- `OnAppInstall()` 내부에서 레지스트리 PID 존재 여부 확인
- PID 있으면(재설치): `BustabccLoggingService.LogMainUpdateAsync()` 호출
- PID 없으면(신규): `BustabccLoggingService.LogMainInstallAsync()` 호출

### 작업 2: CountingService + CountingBaseUrl 제거
- `GlobalConfig.cs`에서 `CountingBaseUrl` 상수 제거
- `Services/CountingService.cs` 파일 전체 삭제
- 호출 제거: `UpdateService.cs:133` (install), `UpdateService.cs:189` (uninstall), `App.xaml.cs:116` (loading)

## 완료된 작업
1. OnAppInstall()에서 레지스트리 pid 존재 여부로 재설치 판단 로직 추가
   - pid 있음 → LogMainUpdateAsync() (재설치)
   - pid 없음 → LogMainInstallAsync() (신규설치)
2. CountingService.cs 파일 전체 삭제
3. GlobalConfig.cs에서 CountingBaseUrl 상수 제거
4. UpdateService.cs에서 CountingService 호출 2건 제거 (install, uninstall)
5. App.xaml.cs에서 CountingService 호출 1건 제거 (loading)
6. 전체 코드베이스에서 CountingService/CountingBaseUrl 참조 0건 확인

## 변경된 파일
- `Services/UpdateService.cs`: OnAppInstall() 재설치 감지 로직 추가, CountingService 호출 제거
- `Services/GlobalConfig.cs`: CountingBaseUrl 상수 제거
- `Services/CountingService.cs`: 파일 삭제
- `App.xaml.cs`: CountingService.LogLoadingAsync() 호출 제거

## 발견된 이슈
### CRITICAL (수정 대상)
1. LogUninstallAsync()가 TargetUpdater(0)로 전송 → TargetMain(1)이어야 함 (이번 작업 범위, 수정)
2. OnAppUninstall() fire-and-forget → 프로세스 종료 전에 HTTP 완료 안 될 수 있음 (기존 이슈, 범위 밖)

### WARNING (참고)
1. 언인스톨 후 재설치 = pid 없으므로 신규설치로 감지 → 의도된 동작 (덮어쓰기 재설치만 감지 대상)
2. _isChecking 경쟁 조건 - 기존 이슈, 이번 범위 밖
3. OnFirstRun() 데드 코드 - 기존 이슈, 이번 범위 밖
4. OnAppInstall 시점에 GlobalConfig.Pid 기본값 - 기존 이슈, 이번 범위 밖

### 수정 결정
- CRITICAL 1만 수정 (LogUninstallAsync TargetUpdater → TargetMain)
- 나머지는 기존 이슈로 별도 작업 필요

## 추가 완료
- BustabccLoggingService.cs: LogUninstallAsync() TargetUpdater → TargetMain 수정

## 남은 작업
- 없음 (완료)
