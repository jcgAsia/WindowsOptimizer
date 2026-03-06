# Keyword Mapping Feature - Implementation Decisions

## Task Summary
키워드 매핑 기능 전체 구현:
1. C# 백엔드: Model, ConfigService, BrowserMonitorService에 키워드 매핑 로직 추가
2. C# WPF UI: 읽기전용 DataGrid로 키워드 매핑 목록 표시 (Tab 2)
3. HTML 에디터: mapping_editor.html에 Keyword Mappings CRUD 섹션 추가

## Key Decisions
- C# 앱은 View Only, CRUD는 mapping_editor.html에서만
- 공유 카운터: URL맵핑 + 키워드맵핑이 동일 target일 때 frequency 합산
- OpenHd는 키워드 매핑과 무관 (별도 카운터)
- Google/Naver 호스트 판별: 엄격한 도메인 매칭 (google.com, search.naver.com)
- XSS 방지: data-attribute + 이벤트 위임 방식 사용

## Files Modified
### C# Backend
- Models/MappingConfig.cs: KeywordMapping 클래스, keymapping on/off, MappingList.KeyMaps
- Services/ConfigService.cs: 키워드 매핑 카운터 복구 로직
- Services/BrowserMonitorService.cs: 공유 카운터, ProcessKeywordMapping, ProcessKeywordAutoTab

### C# UI
- MainWindow.xaml: TabControl(모니터링 + Keyword Mappings), KeyMap 배지
- MainWindow.xaml.cs: 로그 색상 추가
- ViewModels/MainViewModel.cs: KeywordMappingItems (읽기전용), 상태 프로퍼티

### Web Editor
- ServerFiles/mapping_editor.html: Keyword Mappings CRUD (태그 입력, generateXML, parseXML, diff)
