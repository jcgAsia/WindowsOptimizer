# Team Decisions Log

> This file is the persistent memory for Team Agent Mode.

---

## 세션: 2026-03-17 이전 수정(WMI, Raw URL) 검증

## 프로젝트
- 경로: D:\dev\WindowsOptimizer
- 유형: C# WPF .NET (MVVM, Squirrel 배포)
- 규칙: CLAUDE.md 참조

## 미션
- 원본 요청: 이전 세션에서 수행한 WMI 제거, Raw URL 전환이 충분히 검토되었는지 재검증. 특히 mapping_editor.html에서 설정 저장 후 ConfigService가 raw.githubusercontent.com에서 로드할 때 CDN 캐시 지연 문제가 없는지 확인.
- 핵심 우려: raw.githubusercontent.com은 CDN 캐시(~5분)가 있어서 mapping_editor에서 설정 변경 즉시 반영이 안 될 수 있음
- 시작 시각: 2026-03-17

## 이전 세션 변경 내용
1. ToastPopupService.cs: WMI 코드 전체 제거, 폴링 직접 호출로 대체
2. ConfigService.cs: api.github.com → GlobalConfig.MappingUrl (raw.githubusercontent.com) 전환
3. WindowsOptimizer.csproj: System.Management 참조 제거

## 작업 분석
- 유형: 코드 리뷰 / 검증
- 복잡도: 보통
- 팀 구성: 탐색가 3명(병렬) → 필요시 설계자 + 구현자 + 검토자

## 탐색 결과

### WMI 제거: 완전 검증됨 (문제 없음)
- 전체 프로젝트에서 System.Management, WMI 관련 참조 0건
- ToastPopupService.cs: 폴링 직접 호출, Process Dispose 처리 완료
- csproj: System.Management 참조 제거 완료
- 빌드 오류 위험 없음

### Raw URL 전환: 기능적으로 정상이나 CDN 캐시 문제 발견!
- 응답 형식: api.github.com과 raw.githubusercontent.com 모두 동일한 파일 원문 반환 → 파싱 로직 호환
- 암호화: C#과 JS 양쪽 Xor256 완전 호환
- **핵심 문제: CDN 캐시 지연**
  - raw.githubusercontent.com은 Fastly CDN 사용, Cache-Control: max-age=300 (5분), 최대 수 시간
  - ConfigService의 no-cache 요청 헤더는 CDN 서버 캐시에 효과 없음
  - mapping_editor에서 저장 → GitHub 커밋 즉시 → raw URL 반영 5분~수 시간 지연
  - 이전 api.github.com은 즉시 최신 데이터 반환했음

### 최적 해결 방안 분석
- 원래 문제: api.github.com 60 req/hr 제한 (1분 폴링 = 60 req/hr → 한도 도달)
- **폴링 주기를 5분으로 늘리면**: 12 req/hr → 한도 대비 20%만 사용
- api.github.com으로 복원 + 폴링 5분 = CDN 캐시 문제 없이 rate limit도 안전

## 설계 결정
- ConfigService.cs 단독 수정
- ReloadIntervalMs: 60000 → 300000 (5분, 12 req/hr)
- URL: GlobalConfig.MappingUrl → api.github.com 직접 조립 (Pid 분기)
- Accept 헤더 복원: application/vnd.github.v3.raw
- GlobalConfig.cs의 MappingUrl은 건드리지 않음

## 완료된 작업
1. ConfigService.cs: ReloadIntervalMs 60초 → 300초 (5분)
2. ConfigService.cs: raw.githubusercontent.com → api.github.com URL 복원 + Accept 헤더 복원

## 변경된 파일
- `Services/ConfigService.cs`: ReloadIntervalMs 변경, URL 복원, Accept 헤더 복원

## 발견된 이슈

### CRITICAL
- 없음

### WARNING
1. _isLoading volatile check-then-act 경쟁 조건 → 기존 코드 이슈, 이번 범위 밖
2. HttpRequestMessage/response IDisposable 미해제 → 기존 코드 이슈, 이번 범위 밖
3. ReloadIntervalMs public set인데 Timer에 반영 안 됨 → 기존 코드 이슈

### SUGGESTION
1. ?ref=main 파라미터 추가 권장 → 방어적 코딩으로 추가할 가치 있음
2. GlobalConfig.MappingUrl과 ConfigService URL 이중 관리 → 향후 정리

### 수정 결정
- SUGGESTION 1만 수정 (?ref=main 추가) — 기본 브랜치 변경 대비

## 남은 작업
- SUGGESTION 1 수정 후 완료
