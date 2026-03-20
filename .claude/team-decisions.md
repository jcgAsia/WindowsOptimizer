# Team Decisions Log

> This file is the persistent memory for Team Agent Mode.

---

## 세션: 2026-03-20 빌드 실패 해결 (NETSDK1047)

## 프로젝트
- 경로: D:\dev\WindowsOptimizer
- 유형: C# WPF .NET (MVVM, Squirrel 배포)
- 규칙: CLAUDE.md 참조

## 미션
- 원본 요청: build-release.ps1 -Version "1.2.7" 빌드 실패 해결
- 에러: NETSDK1047 - project.assets.json에 'net48/win-x64' 대상 없음
- 에러 메시지: TargetFrameworks에 'net48' 포함 확인, RuntimeIdentifiers에 'win-x64' 포함 필요할 수 있음
- 시작 시각: 2026-03-20 19:33

## 작업 분석
- 유형: 버그 수정 (빌드 실패)
- 복잡도: 보통
- 원인 추정: csproj 파일에서 TargetFramework/RuntimeIdentifier 설정과 restore 결과 불일치
- 팀 구성: 탐색가 2명(병렬) → 개발자 1명 → 검토자 1명

## 탐색 결과
- **근본 원인**: `net48`(.NET Framework 4.8)은 `-r win-x64` RID를 지원하지 않음
- csproj:4 → `<TargetFramework>net48</TargetFramework>` (RID 설정 없음)
- build-release.ps1:105 → `dotnet restore $appProj -r win-x64` (문제)
- build-release.ps1:107 → `dotnet publish ... -r win-x64` (문제)
- restore는 성공처럼 보이지만 project.assets.json에 net48/win-x64 타겟이 생성 안 됨
- net48 앱은 RID 없이도 Windows 전용으로 동작, Squirrel이 배포 담당

## 설계 결정
- **선택지 A (채택)**: build-release.ps1에서 `-r win-x64` 제거 (최소 변경)
  - 105줄: `dotnet restore $appProj -r win-x64` → `dotnet restore $appProj`
  - 107줄: `dotnet publish ... -r win-x64 ...` → `-r win-x64` 제거
  - `--self-contained false`도 net48에서 무의미하므로 제거
- 선택지 B (기각): net8.0-windows 마이그레이션 → 대규모 변경, 위험 높음

## 완료된 작업
1. build-release.ps1:105 → `dotnet restore`에서 `-r win-x64` 제거
2. build-release.ps1:107 → `dotnet publish`에서 `-r win-x64`와 `--self-contained false` 제거

## 변경된 파일
- `build-release.ps1`: 105줄, 107줄 수정

## 발견된 이슈
### CRITICAL: 없음
### WARNING:
1. restore/publish 분리 패턴 — 기능상 문제 없음, 개선 가능하나 불필요
2. 단계 번호 불일치 ([4/4] 완료 후 [3/3] Git) — 로그 혼동, 기능 무관
### SUGGESTION:
1. git add 글로브 패턴 PowerShell 호환성 — 현재 작동 중, 개선 가능
2. Squirrel 버전 하드코딩 — 향후 업데이트 시 주의

## 남은 작업
- 빌드 재실행: `.\build-release.ps1 -Version "1.2.7"` 로 검증 필요
