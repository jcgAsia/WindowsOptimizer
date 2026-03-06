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

※ 신규 추가 항목: keymapping (탭브라우저 제어는 autotab 설정 공유)

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
| **autotab** | on | 새 탭에서 URL 열기 (백그라운드 탭) — URL 맵핑 및 키워드 맵핑 공통 적용 |
|  | off / null / 없음 | 탭은 열되, 클릭 시 URL 이동 — URL 맵핑 및 키워드 맵핑 공통 적용 |
| **autotab\_cycletime** | 숫자 (초) | 탭 재작동 대기시간 — URL 맵핑과 키워드 맵핑이 동일 카운터 공유 |
|  | 0 / null / 없음 | cycletime 무시, frequency 횟수로만 제어 |

※ autotab 제어 변수(autotab\_cycletime, frequency)는 URL 맵핑과 키워드 맵핑이 공통으로 사용한다. 둘 중 하나가 실행되면 동일한 cycletime 카운터와 frequency 카운트가 함께 차감된다.

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

## **3.4 키워드 맵핑 기능 (keymapping) \[신규\]**

| 변수명 | 값 | 동작 |
| ----- | ----- | ----- |
| **keymapping** | on | 검색엔진 URL 키워드 감지 기능 활성화 |
|  | off / null / 없음 | 기능 비활성화 |

**용도: 구글·네이버 검색 URL에 지정 키워드가 포함된 경우 Target URL을 새 탭에서 자동 열기**  
탭 열기 방식, cycletime, frequency는 autotab 설정을 URL 맵핑과 공유하여 사용한다. keymapping 전용 cycletime/frequency 변수는 없다.

# **4\. 기능별 작동 로직**

## **4.1 탭브라우저 기능 (URL 맵핑)**

**대상 브라우저: Chrome, Edge**

작동 플로우:

1. 브라우저 활성 탭 URL 모니터링  
2. URL에서 도메인 추출  
3. mapping.xml의 trigger 도메인과 매칭 확인  
4. 매칭 시 조건 체크 (autotab 공유 카운터 기준):

\- autotab\_cycletime \> 0 → 마지막 탭 실행(URL 맵핑 또는 키워드 맵핑) 후 cycletime 경과 여부  
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

## **4.3 키워드 맵핑 기능 \[신규\]**

**작동 조건: keymapping \= on**

**감지 대상 검색엔진 URL 패턴:**

* Google: www.google.com/search?q=\*  
* Naver:  search.naver.com/search.naver?query=\*

작동 플로우:

20. 브라우저 활성 탭 URL 모니터링  
21. 현재 URL이 감지 대상 검색엔진 URL인지 확인  
22. 검색엔진 URL이 아닌 경우 → 키워드 맵핑 로직 건너뜀  
23. 검색엔진 URL인 경우 → URL에서 검색 쿼리 파라미터(q 또는 query) 값 추출  
24. mapping.xml의 \<keymap\> 항목 순서대로 순회:

\- 각 keymap의 keywords 목록 중 하나라도 검색 쿼리에 포함되면 매칭  
\- 키워드 비교는 대소문자 구분 없음  
\- 키워드는 부분 일치(contains) 방식으로 매칭

25. 매칭된 keymap에 대해 autotab 공유 카운터로 조건 체크:

\- autotab\_cycletime \> 0 → 마지막 탭 실행(URL 맵핑 또는 키워드 맵핑) 후 cycletime 경과 여부  
\- autotab\_cycletime \= 0 → frequency 횟수만 체크

26. 조건 충족 시: autotab 설정에 따라 target URL을 새 탭에서 열기  
27. 첫 번째 매칭된 keymap 항목만 실행 후 순회 중단 (중복 실행 방지)  
28. autotab 공유 frequency 카운트 증가 및 실행 시간 기록

**autotab 공유 제어 원칙:**

* URL 맵핑(4.1)과 키워드 맵핑(4.3)은 동일한 autotab\_cycletime 타이머와 frequency 카운터를 사용  
* URL 맵핑이 먼저 실행되어 cycletime이 리셋되면, 해당 cycletime이 경과하기 전까지 키워드 맵핑도 작동하지 않음  
* 반대로 키워드 맵핑이 먼저 실행된 경우에도 동일하게 적용됨  
* 이를 통해 사용자가 연속적으로 불필요한 탭이 열리는 것을 방지  
* 히든윈도우 기능(openhd)은 별도의 openhd\_cycletime을 사용하므로 autotab 공유 카운터와 무관

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

## **5.4 키워드 맵핑 XML 구조 \[신규\]**

### **5.4.1 키워드 맵핑 XML 예시**

\<keymap\>  
  \<keywords\>호텔,여행,휴가,호캉스,해외여행\</keywords\>  
  \<target\>toastpop.net/adurl\_cps.php?m=agoda\&amp;a=A100637853\</target\>  
  \<frequency\>2\</frequency\>  
\</keymap\>

※ 기존 \<map\> 태그와 구분하기 위해 \<keymap\> 태그를 신규 사용

### **5.4.2 필드 정의**

| 필드 | 설명 |
| ----- | ----- |
| keywords | 감지할 키워드 목록 (쉼표 구분, 다수 입력 가능) — n:1 매핑 |
| target | 키워드 매칭 시 열어줄 광고 URL |
| frequency | 최대 작동 횟수 |

### **5.4.3 키워드 매칭 규칙**

* keywords 필드는 쉼표(,)로 구분된 키워드 목록 (n:1 매핑)  
* 각 키워드는 검색 쿼리 값에 대해 부분 일치(contains) 방식으로 매칭  
* 키워드 비교는 대소문자 구분 없음  
* 하나의 \<keymap\>에서 여러 키워드 중 하나라도 매칭되면 해당 target URL 실행  
* 복수의 \<keymap\> 항목이 매칭될 경우, XML 순서상 첫 번째 항목만 실행 (중복 실행 방지)  
* keywords 값이 비어 있거나 null인 경우 해당 항목은 무시

### **5.4.4 mappings 내 구조 예시**

\<mappings\>  
  \<\!-- URL 맵핑 (기존) \--\>  
  \<map\>  
    \<trigger\>agoda.com\</trigger\>  
    \<target\>toastpop.net/adurl\_cps.php?m=agoda\&amp;a=A100637853\</target\>  
    \<frequency\>1\</frequency\>  
  \</map\>

  \<\!-- 키워드 맵핑 (신규) \--\>  
  \<keymap\>  
    \<keywords\>호텔,여행,휴가,호캉스,해외여행\</keywords\>  
    \<target\>toastpop.net/adurl\_cps.php?m=agoda\&amp;a=A100637853\</target\>  
    \<frequency\>2\</frequency\>  
  \</keymap\>  
\</mappings\>

※ \<map\> 항목과 \<keymap\> 항목은 동일한 \<mappings\> 블록 내에서 혼용 가능

# **6\. 기능 간 독립성**

세 기능(탭브라우저 URL 맵핑, 히든윈도우, 키워드 맵핑)의 on/off는 독립적으로 제어된다. 단, URL 맵핑과 키워드 맵핑은 모두 탭브라우저로 작동하기 때문에 탭 열기 방식(autotab), 재작동 대기시간(autotab\_cycletime), 횟수(frequency)는 공유한다.

| 탭브라우저 기능 (URL 맵핑) | 히든윈도우 기능 | 키워드 맵핑 기능 (신규) |
| ----- | ----- | ----- |
| autotab \= on/off | openhd \= on/off | keymapping \= on/off |
| autotab\_cycletime 제어 | openhd\_time 제어 | autotab\_cycletime 공유 사용 |
| frequency 카운터 관리 | frequency 카운터 관리 | frequency 카운터 공유 사용 |
| 브라우저 새 탭 열기 | 화면 밖 새 창 열기 | 브라우저 새 탭 열기 |
| 포커스 유지 | 자동 닫기 | 포커스 유지 |
| 도메인 단위 매칭 (1:1) | 도메인 단위 매칭 (1:1) | 키워드 단위 매칭 (n:1) |

# **7\. 예외 처리**

| 상황 | 처리 |
| ----- | ----- |
| XML 파싱 실패 | 로컬 캐시된 이전 설정 사용 |
| 서버 접속 불가 | 로컬 캐시된 이전 설정 사용 |
| 브라우저 미실행 | 대기 상태 유지 |
| trigger 매칭 실패 | 다음 URL 변경 시까지 대기 |
| 잘못된 변수값 | 해당 변수 기본값 적용 |
| keywords 미입력 또는 빈 값 | 해당 keymap 항목 무시 (스킵) |
| 검색엔진 URL이 아닌 일반 URL | 키워드 맵핑 작동하지 않음 (검색엔진 한정) |
| 동일 URL에서 복수 키워드 동시 매칭 | 첫 번째 매칭된 keymap 항목만 실행, 중복 실행 없음 |

# **8\. 기본값 정리**

| 변수 | 기본값 (null/없음 시) |
| ----- | ----- |
| forcedown | off (정상 작동) |
| autotab | off (클릭 시 이동) |
| autotab\_cycletime | 0 (시간 무관) |
| openhd | off (비활성화) |
| openhd\_delaytime | 0 (즉시 열기) |
| openhd\_closetime | 10초 |
| openhd\_cycletime | 0 (시간 무관) |
| keymapping | off (비활성화) |
| ※ keymapping의 cycletime/frequency는 autotab 설정을 공유함 | — (별도 변수 없음) |

문서 버전: 2.1  |  작성일: 2026-03-06