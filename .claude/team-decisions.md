# Team Decisions Log

> This file is the persistent memory for Team Agent Mode.

---

## 세션: 2026-05-24 런처/OP 통계 로깅 분리 & 중복 설치 방지

## 프로젝트
- 경로(현재): D:\Dev\JcgAsia\WindowsOptimizer (윈옵티마이저 OP, C# WPF .NET Framework 4.8, Squirrel 배포)
- 경로(연관): D:\Dev\JcgAsia\PlanBLauncher (런처, exe만 내려주는 프로그램)
- 규칙(OP CLAUDE.md/메모리 요약):
  - net48 호환 패키지만 허용 (System.Text.Json 사용 금지)
  - MonitorLogService는 수동 JSON 생성
  - config_reload는 설정 변경 시에만 전송
  - 모니터링 서버: wo-collect.centras.ai (최신), 집계 대시보드는 wo-monitor.vercel.app
  - app_start/load는 분당 최대 1건

## 미션
- 원본 요청 요약:
  1. 통계 데이터 보면 런처 로딩이 Application Loading으로 잡혀서 통계가 섞여 보임 (pb001 파트너: Launcher Install 49 / Launcher Loading 3333 / Application Install 0 / Application Loading 76414)
  2. 런처(Launcher)와 옵티마이저(Application/OP)는 독립 프로그램이므로 로깅도 독립적으로 분리되어야 함
  3. 로그 액션(install/loading/update/uninstall) × 어플타입(launcher/application) 정의 재정립 필요
  4. 현재 재실행 시 loading만 카운트되는데, 이미 설치된 상태에서 재설치 트리거되면 "update"도 같이 카운트되어 재설치(중복) 사실을 알 수 있어야 함
  5. 진짜 버전업 케이스와 중복 케이스 구분 모호 → 그래서 **중복 설치 방지 기능**을 양쪽 모두에 추가
- 합의된 방향(채팅 기록):
  - 중복 설치 방지 = exe 설치 시 설치 경로에 동일 exe가 이미 있으면 설치 자체를 스킵 (레지스트리 기반 X, exe 존재 여부 기반 O)
  - 런처와 OP 둘 다 적용
  - 1차로 **런처 먼저 적용**, 검증 후 OP 적용 권장
  - OP 자동업데이트는 기존 유지

## 작업 분석
- 작업 유형: 조사/분석 + 버그 수정 + 신규 기능 (중복 설치 방지)
- 복잡도: 보통 (2개 프로젝트 동시 관여, 통신 서버 통계 정의도 검토 필요)
- 구성 팀원: 탐색가(병렬 2명, OP와 Launcher 동시) → 설계자 1명 → (사용자 승인 후) 개발자 → 검토자
- **현재 단계는 advisory** — 사용자가 "체크 한번 해 주세요"라고 했으므로, 우선 코드 분석 + 설계안 제시까지가 본 턴 산출물. 구현 착수는 사용자 명시 동의 후.

## 탐색 결과

### A. OP (WindowsOptimizer)

**모니터링 전송 경로 — 이중**
- `BustabccLoggingService` (레거시): `bustabcc.net/PRG/lg_read.php` ← **대시보드 통계의 진짜 소스**
- `MonitorLogService` (신규): `wo-collect.centras.ai/api/logs`
- OP의 모든 로그는 두 서버에 동시 전송됨

**OP 액션 매트릭스**
| action | target | 발생 시점 | 콜사이트 |
|---|---|---|---|
| install | (programId) | Squirrel onInitialInstall (신규 판정) | UpdateService.cs:144 |
| update | (programId) | Squirrel onAppUpdate / onInitialInstall(재설치 판정) | UpdateService.cs:140,176 |
| load | TargetMain=1 | 앱 정상 기동 매번 | App.xaml.cs:130 → BustabccLoggingService.cs:43 |
| app_start | - | 앱 기동 매번 (60s rate limit, wo-collect 전용) | App.xaml.cs:67 |
| uninstall | (programId) | Squirrel onAppUninstall | UpdateService.cs:201 |

**OP payload (wo-collect)**
```json
{"pid":"pb001","action":"load","mac_address":"...","version":"2.7.7.0","success":true,"detail":""}
```
→ **app_type/source 필드 없음.** 발신자(OP) 식별 불가.

**파트너ID(pid)**
- GlobalConfig.cs:11, 기본값 "pb001"
- 우선순위: pid.txt(exe 옆) > 레지스트리 SOFTWARE\WindowsOptimizer\pid > 기본값

**중복 설치 방지**
- 없음. Mutex(Global\WindowsOptimizerMutex)는 실행 중복 방지만 함.

---

### B. Launcher (PlanBLauncher)

**스택**: C# .NET Framework 4.8, WinExe (콘솔)
**핵심 파일**:
- `Program.cs` — 진입점
- `Services\BustabccLoggingService.cs` — bustabcc.net 로그
- `Services\DeploymentService.cs` — OP 다운로드/설치/실행
- `Services\LauncherConfig.cs` — ClientId(DEBUG="pb000"/Release="pb001")

**런처가 실제 보내는 로그 (단 2가지)**
1. 시작 시: `action=load, target=0 (TargetLauncher)` → Launcher Loading 카운트
2. OP **신규 설치 완료** 시: `action=install, target=programId` → Program/Application Install 카운트

**LogProgramLoadAsync()는 정의돼 있지만 호출하는 곳이 없음** → 런처는 절대 "application loading"을 보내지 않음.

**OP 배포 흐름 (DeploymentService.cs)**
1. 설정 조회 → 버전/파일 체크 (레지스트리 HKCU\SOFTWARE\PlanBLauncher\Program_{id}_Version + checkfile 존재)
2. **AlreadyUpToDate면 ExecuteProgram만 — 로그 0개**
3. 신규/업데이트만: 다운로드 → 압축해제/복사 → 버전 레지스트리 저장 → 실행 → **install 로그 1회**
4. `PeriodicCheckAsync()` — 주기적으로 OP 상태 체크. OP가 죽어 있으면 다시 ExecuteProgram

**중복 설치 방지**: `IsCheckFilePresent()`로 파일 존재 확인은 하지만, **이건 "스킵" 판단용일 뿐 명확한 exe-존재-기반 가드는 없음**. `IsProcessRunning()`은 실행 중복 방지용.

---

### C. ★ 통계 이상의 진짜 원인 (가설 검증 결과)

원래 가설 ("런처가 application loading을 잘못 보낸다") = **틀림**.

**진짜 원인 3가지**:

1. **Application Loading 76414의 정체** — 런처가 OP를 다운받지 않고 그냥 실행만 시키는 경우(AlreadyUpToDate), OP가 매 실행마다 자기 자신의 load 로그를 bustabcc.net에 보냄. PeriodicCheck가 죽은 OP를 살리는 시나리오에서 폭증 가능. **즉 Application Loading은 정상 동작이지만 너무 많이 찍힘.**

2. **Application Install=0의 정체** — 런처는 OP를 처음 깔 때만 install을 보냄. 런처 배포 이전부터 OP가 깔려 있던 PC에서는 영원히 install이 안 찍힘. **즉 OP 자체의 Squirrel 설치 이벤트도 누락된 것**(런처가 OP exe를 그냥 복사/실행해서 --squirrel-install 핸들러가 안 돌았기 때문 추정).

3. **재설치(중복) 시 update 카운트가 안 오르는 이유**
   - OP Squirrel OnAppUninstall이 레지스트리 pid 키 전체 삭제 (UpdateService.cs:208)
   - 따라서 다음 설치 시 신규/재설치 판정 불가, 항상 install로만 집계
   - 그리고 위 #2처럼 런처는 AlreadyUpToDate면 install/update 둘 다 안 보냄

---

### D. 두 프로젝트가 공유하는 서버 스키마의 문제

- bustabcc.net `lg_read.php`는 `action` + `target`(숫자/문자) 조합으로 분류:
  - target=0 → Launcher
  - target=1 → Application/Main (OP)
  - target=programId(문자) → Program(설치 대상 식별)
- payload에 명시적 `app_type` 필드 없음. 서버가 target 값으로 분류.
- 결국 **"app_type을 추가하지 않으면 서버에서 launcher/application 구분이 target에 묶여 있음"** — 깨끗한 구분을 위해서는 양 프로젝트 + 서버 스키마 합의가 필요.


## 설계 결정

### 액션 정의 (합의안)
| 액션 | 런처 | OP |
|---|---|---|
| install | exe 미존재 상태에서 설치 완료 | OnAppInstall + .installed 마커 없음 |
| update | exe 있고 버전 다름 | OnAppUpdate, 또는 OnAppInstall + 마커 있음(재설치) |
| load | 일 1회 (LastLogDate) | 일 1회 (레지스트리 loading 키 재사용) |
| uninstall | 해당없음 | OnAppUninstall (마커도 같이 삭제) |

### app_type 필드 (서버 무수정 호환방식)
- 클라이언트 쿼리 끝에 `&app_type=launcher` / `&app_type=application` 추가
- 서버는 무시 OK, target 값으로 이미 분류됨 → 호환 안전

### 중복 설치 방지 (★ 구체화 — 사용자 핵심 강조)

**런처 (1차 핵심 작업)**
- 판단 기준: **레지스트리/checkfile 폐기 → exe 실제 존재로만**
- 스캔 경로: `ProgramConfig.ExeFolder`(예: `%LOCALAPPDATA%\WindowsOptimizer`) 하위 `app-*\{ExeFilename}` glob, Version.TryParse 내림차순 정렬 최신 우선
- 폴백: ExeFolder 미지정 시 기존 `Execute.Path` 고정 경로 File.Exists
- DeployResult enum: `AlreadyUpToDate` → **`AlreadyInstalled`** 로 교체
- DeployAllProgramsAsync에 **`isInitialDeploy: bool` 파라미터** 추가
  - Main에서 호출: `true`
  - PeriodicCheckAsync에서 호출: `false`
- 로그 정책:
  - `Installed` → install 로그 (기존)
  - `AlreadyInstalled` + `isInitialDeploy=true` → **update 로그 1회** (재설치 시도 감지)
  - `AlreadyInstalled` + `isInitialDeploy=false` → 로그 없음 (PeriodicCheck 폭증 방지)
- load 로그는 OP 자체에 위임. 런처 `LogProgramLoadAsync` 비활성 유지
- 레지스트리 버전 비교/저장 로직 제거, `IsCheckFilePresent` 메서드 삭제

**OP (2차, 별도 빌드)**
- `.installed` 마커 파일 (OnAppInstall 생성, OnAppUninstall 삭제). 레지스트리 삭제 문제 우회

### 케이스 매트릭스 (런처)
| # | exe | 가동 | 동작 | 로그 |
|---|---|---|---|---|
| 1 | 없음 | 초기 | 다운로드/설치 | install |
| 2 | 없음 | Periodic | 다운로드/설치 | install |
| 3 | 있음 | 초기 | 스킵+실행 | update + (OP의)load |
| 4 | 있음 | Periodic | 스킵, IsProcRunning이면 실행도 스킵 | 없음 |
| 5 | 있음(버전다름) | 초기 | 스킵+실행 (OP Squirrel이 자체 업데이트) | update + (OP의)load |
| 6 | 있음(버전다름) | Periodic | 스킵 | 없음 |
| 7 | 곰→재설치→런처 재기동 | 초기 | 케이스3과 동일 | update |

### load+app_start 이중전송
- 두 채널 모두 유지 (대시보드 단절 방지)
- bustabcc load만 일 1회 rate-limit 추가 → Application Loading 폭증 해결

### 변경 파일 (1차 = 런처)
- PlanBLauncher/Services/DeploymentService.cs — DeployResult.Updated, CheckSquirrelExeExists()
- PlanBLauncher/Services/BustabccLoggingService.cs — app_type 쿼리, LogProgramUpdateAsync
- PlanBLauncher/Program.cs — case Updated 분기
- PlanBLauncher/Services/LauncherConfig.cs — AppType 상수

### 변경 파일 (2차 = OP)
- WindowsOptimizer/Services/UpdateService.cs — .installed 마커 처리
- WindowsOptimizer/Services/BustabccLoggingService.cs — app_type=application
- WindowsOptimizer/App.xaml.cs — LogMainLoadAsync 일 1회 분기

### 예상 통계 변화 (pb001)
- Application Loading 76414 → 일 1회 기준으로 대폭 감소
- Application Update 0 → 재설치 감지되어 분리 집계
- Application Install 0 → 신규 PC 누적 (기존 PC는 영영 0 그대로 — 의도된 행동)


## 완료된 작업
- [4-1a] DeploymentConfig.cs: ProgramConfig에 ExeFolder/ExeFilename 프로퍼티 추가, Parse에서 exefolder/exefilename XML 속성 읽기 (기본값 "")
- [4-1b] BustabccLoggingService.cs: LogProgramUpdateAsync(programId) 메서드 신규 추가 (LogProgramInstallAsync와 동일 패턴, ActionUpdate 사용)
- [4-1c] LauncherConfig.cs: ActionUpdate 상수 이미 존재(line 28) → 추가 작업 없음
- [4-2] DeploymentService.cs: enum AlreadyUpToDate→AlreadyInstalled rename, FindExePath(program) 신규 추가(Squirrel app-* Version 내림차순 스캔 + Execute.Path 폴백), DeployProgramAsync 재작성(레지스트리 버전 비교/SetProgramVersion/IsCheckFilePresent 제거), IsCheckFilePresent 메서드 삭제
- [4-3] Program.cs: DeployAllProgramsAsync(config, bool isInitialDeploy) 시그니처 변경(기본값 없음 강제), switch case AlreadyUpToDate→AlreadyInstalled + isInitialDeploy시 LogProgramUpdateAsync, Main:true / PeriodicCheckAsync:false 명시 호출
- [6] StatusReportService.cs:156 GetProgramVersion → program.Version 교체 (레지스트리 의존 제거 일관성 확보, 통계 오염 해결)

## 변경된 파일
- D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher\Models\DeploymentConfig.cs (line 57-58 프로퍼티 추가, Parse 객체초기화 블록에 2줄 추가)
- D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher\Services\BustabccLoggingService.cs (line 37-40 신규 메서드)
- D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher\Services\DeploymentService.cs (enum rename, FindExePath 신규, DeployProgramAsync 재작성, IsCheckFilePresent 삭제)
- D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher\Program.cs (DeployAllProgramsAsync 시그니처+분기, 호출사이트 2곳 갱신)
- D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher\Services\StatusReportService.cs (line 156, GetProgramVersion → program.Version)

## 발견된 이슈
### 1차 검토 (Phase 5)
- CRITICAL: 없음
- WARNING: StatusReportService 버전 항상 0 → Phase 6에서 program.Version 사용으로 수정함
- SUGGESTION: app-* 정렬 length 체크 의도 모호, ExeFolder/ExeFilename 편측 설정 무음 처리 (미반영)

### 2차 검토 (Phase 6 후)
- CRITICAL: 없음
- WARNING: StatusReportService.ProgramVersions가 "설치 버전"→"서버 선언 버전"으로 의미 변화. 설치 실패 PC에서 배포 완료로 오인 가능성 — 추가 개선은 별도 작업 범위(FileVersionInfo로 실제 exe 버전 읽기). 현재 상태는 즉각적인 통계 오염(0 반환)은 해결됨
- SUGGESTION: ProgramVersions 필드 주석을 "서버 선언 버전"으로 수정 권장

## 남은 작업
(진행하면서 채움)

---

## 세션 추가: 2026-05-25 런처 빌드 + 테스트

### 빌드 환경
- 솔루션: D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher.sln (PlanBLauncher 프로젝트 1개만 포함)
- 빌드 대상: D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher\PlanBLauncher.csproj
- TargetFramework: net48, OutputType: WinExe
- NuGet 패키지: 없음 (restore 불필요)
- 빌드 명령: `dotnet build "D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher.sln" -c Release`
- 산출물: D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher\bin\Release\net48\PlanBLauncher.exe

### 테스트 전략
- 프레임워크: xUnit 2.9.x + xunit.runner.visualstudio 2.8.x + Microsoft.NET.Test.Sdk 17.x
- 테스트 프로젝트: D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher.Tests (신규)
- InternalsVisibleTo("PlanBLauncher.Tests") 추가 (csproj ItemGroup AssemblyAttribute 방식)
- FindExePath: private → **internal** 노출 (테스트 가능화)
- 네트워크 호출 메서드 제외: LogProgramUpdateAsync, DeployProgramAsync 다운로드 분기 — 운영 서버 오염 방지

### 테스트 시나리오 (14개)
**단위테스트 8개 (U-1 ~ U-8)**: ProgramConfig.Parse(3), DeploymentConfig.Parse(3), LauncherConfig.ResolvePath(2)
**통합테스트 6개 (I-1 ~ I-6)**: FindExePath 4가지 케이스, DeployProgramAsync AlreadyInstalled, StatusReportService.CollectCurrentStatus

### 테스트 인프라
- 생성: D:\Dev\JcgAsia\PlanBLauncher\PlanBLauncher.Tests\ (csproj + UnitTests.cs + IntegrationTests.cs)
- 메인 csproj에 InternalsVisibleTo("PlanBLauncher.Tests") 추가
- DeploymentService.FindExePath: private → internal 노출
- sln 등록 완료

### 테스트 결과 (2026-05-25 02:xx KST)
**총 15개 / 통과 15 / 실패 0 / 5.0초**

| 카테고리 | 통과 |
|---|---|
| 단위테스트 (U-1 ~ U-8, U-5는 Theory 2건) | 9 |
| 통합테스트 (I-1 ~ I-6) | 6 |

**핵심 회귀테스트 통과**:
- I-6: app-2.10.0 vs app-2.9.0 → Version.TryParse로 2.10.0 우선 (문자열 정렬 버그 회피 확인)
- U-7: %LOCALAPPDATA% 환경변수 치환 정상
- U-8: 슬래시→백슬래시 변환 정상

### 추가 발견 (통합테스트 작성 중)
- **잠재 이슈**: FindExePath 폴백 로직이 설계 의도와 다름. ExeFolder가 있고 app-* 스캔이 실패하면 **Execute.Path 폴백이 발동**한다. 이는 ExeFolder 신뢰 설정 시 의도치 않은 폴백을 유발할 수 있음.
  - 의도: ExeFolder 지정 시 결과만 신뢰, 못 찾으면 null
  - 실제: ExeFolder 못 찾으면 Execute.Path까지 시도
  - 영향 평가: 서버 config XML이 exefolder만 정확히 설정하면 큰 문제 없음. 다만 Execute.Path가 구버전 고정 경로면 잘못된 매칭 가능.
  - 권고: 의도대로 동작시키려면 ExeFolder 설정 시 폴백을 타지 않도록 FindExePath의 폴백 조건을 명시화 (`if (string.IsNullOrEmpty(ExeFolder))`로 폴백 진입 가드)
  - **[2026-05-25 수정 완료]** FindExePath 첫 if 블록(ExeFolder+ExeFilename 분기) 끝에 `return null;` 추가 → ExeFolder 지정 시 app-* 스캔 결과만 신뢰, Execute.Path 폴백 차단. 폴백은 ExeFolder 미지정 시에만 동작.
  - 테스트 영향: I-2를 I-2a(ExeFolder 지정+app-*없음→null), I-2b(ExeFolder 미지정→Execute.Path 폴백)로 분리. 총 테스트 16개로 증가.
  - **[2차 검토 WARNING]** ExeFolder 있음+ExeFilename 빈 경우 첫 if(AND조건) false → 폴백 우회. 의도 불일치. → 조건을 `if(!IsNullOrEmpty(ExeFolder))` 바깥, 안에서 ExeFilename 체크, 끝에 return null로 분리 수정 예정. 해당 케이스 테스트(I-2c) 추가.
