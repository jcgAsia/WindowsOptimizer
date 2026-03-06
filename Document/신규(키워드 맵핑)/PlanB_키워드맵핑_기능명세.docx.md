**PlanB \- Windows Optimizer**

기능 추가 명세

**키워드 맵핑 (Keyword Mapping)**

# **1\. 개요**

키워드 맵핑은 기존 URL 맵핑과 유사한 구조의 신규 기능으로, 사용자가 Google 또는 Naver에서 검색할 때 입력한 키워드를 감지하여 지정된 Target URL을 새 탭에서 여는 기능이다.

URL 맵핑이 활성 탭의 도메인을 감지하는 것과 달리, 키워드 맵핑은 검색엔진 URL의 쿼리 파라미터에 포함된 검색어를 감지한다. 또한 URL 맵핑이 1:1 매핑인 것과 달리 키워드 맵핑은 n:1 매핑으로, 여러 키워드를 하나의 Target URL에 묶어 등록할 수 있다.

**히든윈도우 기능(openhd)에는 적용하지 않는다. 키워드 맵핑은 autotab(새 탭 열기) 방식으로만 작동한다.**

# **2\. 감지 대상 검색엔진 및 URL 패턴**

키워드 맵핑은 아래 두 검색엔진의 URL에서만 작동한다. 그 외 도메인의 URL 변경은 키워드 맵핑 로직을 건너뛴다.

| 검색엔진 | 감지 파라미터 | URL 패턴 예시 |
| ----- | ----- | ----- |
| Naver | query= | search.naver.com/search.naver?...\&query=%EC%87%BC%ED%95%91%EB%AA%B0 |
| Google | q= | www.google.com/search?q=%EC%87%BC%ED%95%91%EB%AA%B0 |

키워드 감지는 URL 디코딩 후 쿼리 파라미터 값(query= 또는 q=)을 기준으로 수행한다.

URL 예시:  
Naver: https://search.naver.com/search.naver?where=nexearch\&sm=top\_hty\&fbm=0\&ie=utf8\&query=%EC%87%BC%ED%95%91%EB%AA%B0  
Google: https://www.google.com/search?q=%EC%87%BC%ED%95%91%EB%AA%B0

# **3\. URL 맵핑과의 비교**

| 항목 | URL 맵핑 (기존) | 키워드 맵핑 (신규) |
| ----- | ----- | ----- |
| 감지 방식 | 활성 탭의 도메인 일치 여부 | 검색엔진 URL의 쿼리 파라미터에 키워드 포함 여부 |
| 감지 대상 | 모든 도메인 | Google, Naver 검색 URL 한정 |
| Trigger 단위 | 도메인 1개 → Target 1개 (1:1) | 키워드 n개 → Target 1개 (n:1) |
| Target 수 | 1개 | 1개 |
| 탭 열기 방식 | autotab 설정 따름 | autotab 설정 공유 |
| cycletime 제어 | autotab\_cycletime 사용 | autotab\_cycletime 공유 |
| frequency 카운터 | autotab 공유 카운터 | autotab 공유 카운터 (동일) |
| 히든윈도우 적용 | 적용 | 미적용 |
| XML 태그 | \<map\> | \<keymap\> |

# **4\. XML 설정 구조**

## **4.1 전역 제어 변수 추가**

기존 \<config\> 블록에 keymapping 활성화 변수를 추가한다.

\<config\>  
  ...기존 변수...  
  \<keymapping\>on\</keymapping\>   \<\!-- on: 활성화 / off 또는 없음: 비활성화 \--\>  
\</config\>

keymapping 전용 cycletime 변수는 없다. autotab\_cycletime과 frequency를 URL 맵핑과 공유한다.

## **4.2 \<keymap\> 태그 구조**

키워드 맵핑은 기존 \<map\> 태그와 구분하기 위해 \<keymap\> 태그를 신규 사용한다. 동일한 \<mappings\> 블록 내에 \<map\>과 혼용 가능하다.

\<mappings\>

  \<\!-- URL 맵핑 (기존) \--\>  
  \<map\>  
    \<trigger\>agoda.com\</trigger\>  
    \<target\>toastpop.net/adurl\_cps.php?m=agoda\&amp;a=A100637853\</target\>  
    \<frequency\>2\</frequency\>  
  \</map\>

  \<\!-- 키워드 맵핑 (신규) \--\>  
  \<keymap\>  
    \<keywords\>호텔,여행,휴가,호캉스,해외여행\</keywords\>  
    \<target\>toastpop.net/adurl\_cps.php?m=agoda\&amp;a=A100637853\</target\>  
    \<frequency\>2\</frequency\>  
  \</keymap\>

\</mappings\>

## **4.3 \<keymap\> 필드 정의**

| 필드 | 설명 |
| ----- | ----- |
| keywords | 감지할 키워드 목록. 쉼표(,)로 구분하여 다수 입력 가능 (n:1 매핑) |
| target | 키워드 매칭 시 새 탭에서 열 광고 URL |
| frequency | 최대 작동 횟수 (autotab 공유 카운터 기준) |

## **4.4 키워드 매칭 규칙**

* keywords 필드는 쉼표(,)로 구분된 키워드 목록을 입력한다 (n:1 매핑).  
* 각 키워드는 검색 쿼리 값에 대해 부분 일치(contains) 방식으로 매칭한다.  
* 키워드 비교는 대소문자 구분 없음.  
* 하나의 \<keymap\>에서 여러 키워드 중 하나라도 매칭되면 해당 target URL을 실행한다.  
* 복수의 \<keymap\> 항목이 매칭될 경우, XML 순서상 첫 번째 항목만 실행한다 (중복 실행 방지).  
* keywords 값이 비어 있거나 null인 경우 해당 항목은 무시한다.

# **5\. autotab 공유 제어**

URL 맵핑과 키워드 맵핑은 모두 autotab으로 새 탭을 여는 방식을 사용한다. 따라서 탭 열기 방식, 재작동 대기시간, 횟수 제한을 공유하여 중복 탭 오픈을 방지한다.

| 공유 제어 변수 | 적용 범위 |
| ----- | ----- |
| autotab | URL 맵핑 \+ 키워드 맵핑 공통 — 탭 열기 방식 (백그라운드 / 클릭 시 이동) |
| autotab\_cycletime | URL 맵핑 \+ 키워드 맵핑 공통 — 어느 쪽이 실행되어도 동일 타이머 리셋 |
| frequency | URL 맵핑 \+ 키워드 맵핑 공통 — 어느 쪽이 실행되어도 동일 카운터 차감 |

**공유 제어 동작 원칙:**

* URL 맵핑이 먼저 실행되어 autotab\_cycletime이 리셋되면, cycletime이 경과하기 전까지 키워드 맵핑도 작동하지 않는다.  
* 키워드 맵핑이 먼저 실행된 경우에도 동일하게 적용된다.  
* frequency 카운터도 양쪽이 공유하므로, URL 맵핑 실행 횟수와 키워드 맵핑 실행 횟수의 합산이 frequency 한도를 초과하면 더 이상 작동하지 않는다.  
* 히든윈도우(openhd)는 별도의 openhd\_cycletime을 사용하므로 autotab 공유 카운터와 무관하다.

# **6\. 작동 플로우**

1. 브라우저 활성 탭 URL 모니터링  
2. 현재 URL이 Naver 또는 Google 검색 URL인지 확인  
3. 검색 URL이 아닌 경우 → 키워드 맵핑 로직 건너뜀  
4. 검색 URL인 경우 → 쿼리 파라미터(q 또는 query) 값 추출 및 URL 디코딩  
5. \<keymap\> 항목을 XML 순서대로 순회하여 keywords 목록과 매칭 확인  
6. 매칭된 경우 → autotab 공유 카운터로 조건 체크:

\- autotab\_cycletime \> 0 → 마지막 탭 실행(URL 맵핑 또는 키워드 맵핑) 후 cycletime 경과 여부 확인  
\- autotab\_cycletime \= 0 → frequency 횟수만 체크

7. 조건 충족 시 → autotab 설정에 따라 target URL을 새 탭에서 열기  
8. 첫 번째 매칭 keymap만 실행 후 순회 중단  
9. autotab 공유 frequency 카운트 증가 및 실행 시간 기록

# **7\. 예외 처리**

| 상황 | 처리 |
| ----- | ----- |
| keywords 미입력 또는 빈 값 | 해당 \<keymap\> 항목 무시 (스킵) |
| 검색엔진 URL이 아닌 일반 URL | 키워드 맵핑 로직 건너뜀 |
| 복수 \<keymap\> 항목이 동시에 매칭 | XML 순서상 첫 번째 항목만 실행 |
| autotab\_cycletime 미경과 상태 | URL 맵핑·키워드 맵핑 모두 작동하지 않음 (공유 타이머 기준) |
| frequency 한도 초과 | URL 맵핑·키워드 맵핑 모두 작동하지 않음 (공유 카운터 기준) |
| keymapping \= off 또는 없음 | 키워드 맵핑 기능 전체 비활성화 |

# **8\. 관리 UI 요구사항**

기존 URL Mappings 관리 화면과 동일한 다크 테마로 Keyword Mappings 관리 화면을 추가한다.

**URL Mappings UI와의 차이점:**

* Trigger Domain 입력 필드 대신, 다수의 키워드를 태그 형태로 입력할 수 있는 Trigger Keywords 입력 필드를 사용한다.  
* Enter 또는 쉼표(,) 입력 시 키워드가 태그로 추가되며, 개별 삭제가 가능하다.  
* Target URL, Frequency, Delete 구성은 URL Mappings UI와 동일하다.

관리 UI는 mapping.xml의 \<keymap\> 항목을 CRUD하는 인터페이스이며, XML 직접 편집 없이 키워드 맵핑을 관리할 수 있어야 한다.

PlanB 기능명세서 V2.1 참조  |  작성일: 2026-03-06