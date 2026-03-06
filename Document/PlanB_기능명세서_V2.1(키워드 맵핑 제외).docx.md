**PlanB \- Windows Optimizer**

기능명세서

# **1\. 개요**

| 프로젝트 | PlanB |
| :---- | :---- |
| **파일명** | Windows Optimizer |
| **설정파일** | mapping.xml |
| **Git** | github.com/jcgAsia/WindowsOptimizer\_Updater |

# **2\. XML 설정 구조**

\<?xml version="1.0" encoding="UTF-8"?\>  
\<config\>  
  \<forcedown\>off\</forcedown\>  
  \<autotab\>on\</autotab\>  
  \<autotab\_cycletime\>1800\</autotab\_cycletime\>  
  \<openhd\>on\</openhd\>  
  \<openhd\_delaytime\>0\</openhd\_delaytime\>  
  \<openhd\_closetime\>600\</openhd\_closetime\>  
  \<openhd\_cycletime\>1800\</openhd\_cycletime\>  
  \<keymapping\>on\</keymapping\>  
  \<mappings\>...\</mappings\>  
\</config\>

# **3\. 제어 변수 정의**

## **3.1 전역 제어**

| 변수명 | 값 | 동작 |
| ----- | ----- | ----- |
| **forcedown** | on | exe 전체 기능 중지 (모든 기능 비활성화) |
|  | off / null / 없음 | 정상 작동 |

**용도: 서버에서 원격으로 프로그램 전체 기능을 즉시 중단시킬 때 사용**

## **3.2 탭브라우저 기능 (autotab)**

| 변수명 | 값 | 동작 |
| ----- | ----- | ----- |
| **autotab** | on | 새 탭에서 URL 열기 (백그라운드 탭) — URL 맵핑 적용 |
|  | off / null / 없음 | 탭은 열되, 클릭 시 URL 이동 — URL 맵핑 적용 |
| **autotab\_cycletime** | 숫자 (초) | 탭 재작동 대기시간 — URL 맵핑 카운터 |
|  | 0 / null / 없음 | cycletime 무시, frequency 횟수로만 제어 |

## **3.3 히든윈도우 기능 (openhd)**

| 변수명 | 값 | 동작 |
| ----- | ----- | ----- |
| **openhd** | on | 화면 밖 숨겨진 새 창으로 광고 URL 열기 |
|  | off / null / 없음 | 기능 비활성화 |
| **openhd\_delaytime** | 숫자 (초) | 히든 윈도우 열기 전 지연 시간 |
|  | 0 / null / 없음 | 즉시 열기 (지연 없음) |
| **openhd\_closetime** | 숫자 (초) | 숨겨진 창 유지 시간 |
|  | null / 없음 | 기본값 10초 |
| **openhd\_cycletime** | 숫자 (초) | 동일 도메인 재작동 대기시간 |
|  | 0 / null / 없음 | cycletime 무시, frequency 횟수로만 제어 |


# **4\. 기능별 작동 로직**

## **4.1 탭브라우저 기능 (URL 맵핑)**

**대상 브라우저: Chrome, Edge**

작동 플로우:

1. 브라우저 활성 탭 URL 모니터링  
2. URL에서 도메인 추출  
3. mapping.xml의 trigger 도메인과 매칭 확인  
4. 매칭 시 조건 체크 (autotab 공유 카운터 기준):

\- autotab\_cycletime \> 0 → 마지막 탭 실행(URL 맵핑) 후 cycletime 경과 여부  
\- autotab\_cycletime \= 0 → frequency 횟수만 체크

5. 조건 충족 시:

\- autotab \= on → 새 탭에서 target URL 열기 (포커스 유지)  
\- autotab \= off → 탭 생성 후 클릭 시 URL 이동

6. autotab 공유 frequency 카운트 증가 및 실행 시간 기록

## **4.2 히든윈도우 기능**

**작동 조건: openhd \= on**

작동 플로우:

7. 브라우저 활성 탭 URL 모니터링  
8. URL에서 도메인 추출 및 trigger 매칭 확인  
9. 매칭 시 조건 체크:

\- openhd\_cycletime \> 0 → 마지막 실행 후 cycletime 경과 여부  
\- openhd\_cycletime \= 0 → frequency 횟수만 체크

10. 조건 충족 시: 현재 브라우저 창 목록(HWND) 저장  
11. openhd\_delaytime 초 대기 (0이면 즉시 진행)  
12. 화면 밖 좌표에 새 창으로 target URL 열기 (예: left=-9999, top=-9999)  
13. openhd\_closetime 초 대기 (쿠키 드롭 시간 확보)  
14. 새로 생성된 창만 찾아서 WM\_CLOSE로 닫기  
15. frequency 카운트 증가 및 실행 시간 기록

**창 닫기 로직 상세:**

16. 현재 브라우저 창 목록 재조회  
17. 이전 목록과 비교하여 새로 생긴 창 식별  
18. 새로 생긴 창에만 WM\_CLOSE 메시지 전송  
19. 기존 사용자 창은 절대 닫지 않음

# **5\. 도메인 맵핑 구조**

## **5.1 맵핑 XML 예시**

\<map\>  
  \<trigger\>search.naver.com\</trigger\>  
  \<target\>www.agoda.com/ko-kr/book\</target\>  
  \<frequency\>2\</frequency\>  
\</map\>

## **5.2 필드 정의**

| 필드 | 설명 |
| ----- | ----- |
| trigger | 모니터링할 도메인 (예: search.naver.com) |
| target | 열어줄 광고 URL |
| frequency | 최대 작동 횟수 |

## **5.3 도메인 매칭 규칙**

* trigger는 도메인 단위 매칭 (서브도메인 포함)  
* search.naver.com → search.naver.com/\* 모든 경로 매칭  
* 대소문자 구분 없음

