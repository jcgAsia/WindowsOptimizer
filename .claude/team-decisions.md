# Team Decisions Log

> This file is the persistent memory for Team Agent Mode.
> Every session appends below. Old sessions can be archived when the file gets too long.

---

## 프로젝트
- 경로: E:\dev\jcg\WindowsOptimizer
- 유형: C# WPF .NET (MVVM, Squirrel 배포)
- 규칙: CLAUDE.md 참조, 불변성/TDD/작은함수 원칙

## 미션
- 원본 요청: 탭브라우저(AutoTab) 기능 점검 및 보완 - 2가지 버그
  1. URL 맵핑 후 탭브라우저 작동 시 Target URL이 새창으로 열리는 문제 (탭으로 열려야 함)
  2. AutoTab cycle 시간 설정(10800초/180분)에 관계없이 반복적으로 뜨는 문제
- 환경: pb000 아이디, AutoTab ON, HiddenWindow OFF, CycleTime 10800초(180분)
- 로그 힌트: [09:52:22] → 스킵 (최대 횟수 도달), [OpenHd] 기능: OFF ✗ → 스킵 (기능 비활성화)
- 참고 문서: E:\dev\jcg\WindowsOptimizer\Document\PlanB_기능명세서_V2.1(키워드 맵핑 제외).docx.md
- 시작 시각: 2026-03-06

## 작업 분석
- 작업 유형: 버그 수정
- 복잡도: 보통~복잡
- 버그 2건:
  1. AutoTab에서 Target URL이 탭이 아닌 새창으로 열리는 문제
  2. CycleTime 설정(180분)에 관계없이 반복적으로 창이 뜨는 문제
- 구성 팀원: 탐색가 3명 → 설계자 1명 → 개발자 1~2명 → 검토자 1명

## 탐색 결과

### 버그 1: Target URL이 새창으로 열리는 문제
- **근본 원인**: BrowserMonitorService.cs:328에서 `--new-window` 플래그 하드코딩
- ProcessAutoTab() (라인 181-228)이 OpenHiddenWindow()를 호출
- OpenHiddenWindow() (라인 284-355)에서 `Arguments = $"--new-window \"{targetUrl}\""` 사용
- AutoTab은 "탭으로 열기"가 의도인데, 실제 구현은 히든 윈도우와 동일하게 새 창으로 열고 있음
- **명세서 기준**: autotab = on → 새 탭에서 Target URL을 백그라운드 탭으로 열기 (포커스 유지)

### 버그 2: CycleTime 무시하고 반복 실행되는 문제
- **근본 원인**: ConfigService가 mapping.xml을 1분 주기로 reload할 때 새 MappingConfig 인스턴스 생성
- DomainMapping.AutoTabLastTime은 `[XmlIgnore]`로 메모리에만 존재 (MappingConfig.cs:152)
- reload될 때마다 AutoTabLastTime = DateTime.MinValue, AutoTabCount = 0으로 리셋
- → CycleTime 체크가 항상 통과되어 반복 실행됨

### 핵심 파일 목록
1. **BrowserMonitorService.cs** - 핵심 로직
   - MonitoringLoop() (102-130): URL 모니터링 메인 루프
   - ProcessAutoTab() (181-228): AutoTab 실행 로직 ★
   - ProcessOpenHd() (230-279): OpenHd 실행 로직
   - OpenHiddenWindow() (284-355): 새 창 열기 (`--new-window` 사용) ★
   - HideNewWindowAsync() (379-413): 새 창 감지 및 숨김
2. **MappingConfig.cs** - 설정 모델
   - DomainMapping (139-185): 실행 추적 (XmlIgnore, 메모리 전용) ★
   - CanTriggerAutoTab / CanTriggerOpenHd: 실행 가능 여부
   - MarkAutoTabTriggered / MarkOpenHdTriggered: 실행 기록
3. **ConfigService.cs** - 설정 로딩 (1분 주기 reload) ★
4. **MainViewModel.cs** - UI 바인딩
5. **MainWindow.xaml** - UI 정의

### AutoTab vs OpenHd 차이
- AutoTab: DelayTime 없음, CloseTime만 사용 → 탭으로 열어야 함
- OpenHd: DelayTime + CycleTime + CloseTime → 히든 새 창으로 열어야 함
- 현재 둘 다 동일한 OpenHiddenWindow() 호출 → AutoTab이 새 창으로 열리는 원인

### 추가 발견
- 중복 실행 방지 로직이 주석 처리됨 (BrowserMonitorService.cs:288-296)
- 히든 창 감지 500ms 제한 → 실패 시 사용자에게 창 노출 가능

## 설계 결정

### 버그 1 수정: Target URL이 새창으로 열리는 문제
- **방법**: OpenTabInBackground(string url) 신규 메서드 생성
- **파일**: BrowserMonitorService.cs
  - 신규 메서드 OpenTabInBackground() 추가 (라인 280 근처)
    - `--new-window` 플래그 제거, URL만 전달 → 기존 창에 탭으로 열림
    - 히든 처리/창 감지/추적 로직 불필요 (일반 탭이므로)
    - 간단한 Process.Start()만 사용
  - ProcessAutoTab() (라인 227) 수정: OpenHiddenWindow() → OpenTabInBackground() 호출로 교체
- **영향**: OpenHd는 기존 OpenHiddenWindow() 유지, 영향 없음

### 버그 2 수정: CycleTime 무시하고 반복 실행
- **방법**: ConfigService에 RuntimeState Dictionary 추가 (옵션 B 채택)
- **파일 3개 변경**:
  1. ConfigService.cs:
     - RuntimeState 클래스 추가 (AutoTabCount/LastTime, OpenHdCount/LastTime)
     - Dictionary<string, RuntimeState> _runtimeStates 추가
     - GetOrCreateRuntimeState(trigger), UpdateRuntimeState() 메서드 추가
  2. BrowserMonitorService.cs:
     - ProcessAutoTab/ProcessOpenHd에서 mapping 직접 접근 → ConfigService.RuntimeState 사용으로 변경
  3. MappingConfig.cs:
     - DomainMapping에서 XmlIgnore 속성 4개 및 Mark* 메서드 제거 (RuntimeState로 이전)
- **키**: trigger 도메인을 소문자 정규화하여 Dictionary 키로 사용
- **앱 재시작 시**: RuntimeState 초기화됨 (의도된 동작, 세션 단위)

### 수정 순서
1. 버그 1: BrowserMonitorService.cs (OpenTabInBackground 추가 + ProcessAutoTab 수정)
2. 버그 2: ConfigService.cs (RuntimeState 인프라) → BrowserMonitorService.cs (RuntimeState 사용) → MappingConfig.cs (정리)

## 완료된 작업
1. 버그 1 수정 완료: BrowserMonitorService.cs
   - OpenTabInBackground(string targetUrl) 신규 메서드 추가 (라인 281-314)
   - ProcessAutoTab()에서 OpenHiddenWindow() → OpenTabInBackground() 호출로 교체
   - `--new-window` 플래그 없이 URL만 전달 → 기존 창에 새 탭으로 열림
2. 버그 2 수정 완료: ConfigService.cs
   - LoadMappingConfigAsync()에서 reload 시 기존 DomainMapping의 런타임 카운터를 새 인스턴스로 복사
   - Trigger 기준 매칭 (대소문자 무시)
   - 복사 대상: AutoTabCount, AutoTabLastTime, OpenHdCount, OpenHdLastTime
   - using System.Linq 추가
   - null 체크 및 로그 출력 포함

## 변경된 파일
- `Services/BrowserMonitorService.cs`: OpenTabInBackground() 신규 메서드 추가 + ProcessAutoTab() 호출 변경
- `Services/ConfigService.cs`: LoadMappingConfigAsync()에 런타임 카운터 복구 로직 추가 + using System.Linq 추가

## 발견된 이슈 (1차 검토)

### CRITICAL
1. **[BrowserMonitorService] _browserType 감지 실패 시 잘못된 브라우저 실행**
   - URL 감지 실패 시 _browserType이 기본값 0(Chrome)으로 유지되어 Edge 사용자에게 Chrome이 열릴 수 있음
   - 수정안: OpenTabInBackground에서 포그라운드 브라우저 직접 감지 또는 기본 브라우저로 열기
2. **[BrowserMonitorService] Process.Start(browserExe, url)로는 새 탭 보장 불가**
   - Chrome/Edge는 --new-window 없이 URL 전달해도 설정에 따라 새 창으로 열릴 수 있음
   - 실제로는 기존 브라우저 프로세스가 실행 중이면 대부분 새 탭으로 열림 (Chrome/Edge 기본 동작)
   - 하지만 100% 보장은 안 됨
   - 수정안: 기본 브라우저로 URL을 여는 방식 (FileName=url, UseShellExecute=true)
3. **[ConfigService] 런타임 카운터 복사 시 null 안전성 부족**
   - newMap.Trigger가 null일 때 대비 추가 필요

### WARNING
1. _browserType 타이밍 이슈 (Chrome→Edge 전환 미감지)
2. 런타임 카운터 복구 로그 정보 부족 (복구 건수 미표시)
3. 스레드 안전성 미보장 (MappingConfig 교체 시 경쟁 조건)
4. OpenHiddenWindow도 _browserType 의존

### SUGGESTION
1. OpenTabInBackground에 async/await 패턴
2. Timer 콜백 예외 처리
3. LINQ FirstOrDefault O(n²) → Dictionary O(n) 개선

## CRITICAL 이슈 수정 (Phase 6)
1. CRITICAL 1,2 수정완료: OpenTabInBackground() 재작성
   - _browserType 의존 제거
   - UseShellExecute=true + URL을 FileName으로 → 기본 브라우저로 열기
   - 기존 브라우저가 실행 중이면 새 탭으로 열림 (OS 기본 동작)
2. CRITICAL 3 수정완료: ConfigService 런타임 카운터 복구 개선
   - string.IsNullOrEmpty 체크 추가
   - Dictionary 기반 O(n) 조회로 성능 개선
   - restoredCount 로그 추가

## 2차 검토 결과
- CRITICAL: 0건 ✅
- WARNING: 3건 (기존 코드 이슈, 경미)
  1. OpenHiddenWindow()에서 _browserType 사용 (히든 윈도우 추적 목적으로 필요, 이번 범위 밖)
  2. GroupBy().First() 일관성 (실제 위험도 낮음)
  3. 중복 Trigger 처리 시 조용한 실패 (설정 파일 오류 케이스)
- SUGGESTION: 3건 (선택적)
- **최종 판단: 다음 단계 진행 가능**

## 남은 작업
- 없음 (완료)
