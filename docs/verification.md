## 0.9.0 버전 확정·커밋 범위 (2026-10-27)

- 사용자가 앞선 이름 통일·PATH 등록·기존 데이터 가져오기를 확인하고 커밋·푸시·태그 생성을 요청했다. 설치형 지원은 의견을 요청한 별도 주제이므로 이번 소스에 추가하지 않는다.
- Windows/Terminal 프로젝트와 HelpCatalog 버전을 0.9.0으로 맞췄다. 기존 v0.8.0 태그를 보존하고 새 v0.9.0 태그를 사용한다. 설치형 소스·설치 패키지·GitHub 릴리스는 포함하지 않는다.
- 0.9.0 소스로 핵심 검사 169/169, 도움말 검사 53회(22개 명령·JSON 문서 일치) 통과. Windows x64 자체 포함 단일 Chagok.exe 게시 성공. 이전 기능의 실제 macOS PTY 검증은 아래 2026-10-26 기록을 참고한다.
- 버전 외 기능 구현은 앞서 검증한 소스를 유지했다. 실제 Windows의 구체적인 수동 확인 항목 전체가 검증됐다고 해석하지 않는다.
- 빌드·검사·게시 로그: /private/tmp/chagok090-{build,checks,help,publish}.log.

## Chagok 이름 통일·고정 EXE·사용자 PATH 명령 (2026-10-26)

- GitHub origin은 이미 `ksyrang/Chagok`이다. 로컬 저장소 폴더를 `FileOrganizer`에서 `Chagok`으로 변경했다. Core/Windows/Checks 프로젝트·파일·네임스페이스, XAML 클래스·참조, 터미널 연결 소스, 개발 명령·문서를 Chagok으로 통일했다. AssemblyName은 기존 Chagok을 유지한다.
- 게시 후 버전 파일명으로 바꾸던 MSBuild Target을 제거해 `Chagok.exe`로 고정했다. ZIP에는 버전을 남기며 앱 정보와 version 명령으로 버전을 확인한다. 과거 배포 ZIP·태그는 변경하지 않는다.
- Windows 배포본의 `path add/status/remove`를 추가했다. 사용자 PATH의 현재 EXE 폴더만 등록·조회·해제한다. 다른 항목·환경 변수 원문·레지스트리 값 형식을 보존하며 시스템 PATH는 변경하지 않는다. 중복 등록을 피하고 변경을 알리되 기존 터미널은 재시작해야 한다. 이 명령은 설정·기록·세션 잠금을 열지 않는다.
- 사용자 선택에 따라 기본 데이터를 `%LOCALAPPDATA%\Chagok`으로 변경했다. 첫 기본 데이터 사용 시 구버전 잠금을 잡고 settings.json/history.jsonl/settings.json.backup 원문을 임시 폴더에 복사·검증한 뒤 새 폴더로 전환한다. 기존 원본은 보존하고 이미 있는 새 데이터/완료 표식은 우선한다. 빈 새 폴더는 가져오며, 다른 파일만 있는 새 폴더·손상·복사 오류·링크는 원본 보존 후 중단한다. --data-dir 별도 위치는 가져오지 않는다. 구버전 단일 실행 mutex 이름과 구버전 임시 파일 제외 기준은 호환성을 위해 유지한다. 새 이동 임시 파일은 `.chagok-`를 사용하고 규칙 검사·파일명 분석·기준 폴더 분석 모두 새/구 접두사를 제외한다.
- 핵심 검사 **169/169 통과**. 기존 TUI 테스트가 실제 안내 `F1/? 도움말` 대신 예전 문구를 요구하던 기대값을 수정했다. 화면 동작은 변경하지 않았다. PATH 항목 보존·중복·해제·금지 경로 및 새/구 임시 파일 제외 회귀를 추가했다. 데이터 가져오기 9개 검사에서 원문·ID·백업·부분 기록 보존, 캐시 제외, 덮어쓰기 방지, 빈/충돌 폴더, 손상·부분 복사 실패와 재시도, 구버전 잠금·해제, 링크·겹침 차단, 완료 표식, 새/구 데이터 보호를 확인했다. 검사에는 임시 데이터만 사용했다.
- 도움말·인수 검사 **53회 통과**, 명령 22개와 `docs/cli-reference.json`이 런타임 출력과 일치한다. help/version 및 비 Windows PATH 명령 거부·잘못된 인수 처리에서 데이터 폴더가 만들어지지 않는 것을 확인했다.
- 실제 macOS CLI/PTY smoke: CLI 명령 18개, 파일 105개 이동, 2페이지 조회, 선택 이동, 설정 공유, 다른 프로세스 중복 실행 차단, 비정상 종료 후 잠금 해제, TUI 탐색 통과. 증거 `/private/var/folders/6v/d8xjf8d57lg5yfbg7qgmv7g00000gn/T/chagok-cli-e2e-ut7d_jwy`.
- 실제 macOS 기준 폴더 TUI smoke(별칭·복수 후보·그룹 지정·취소/명시 이동·캐시 정리)와 파일명 분석 CLI/TUI smoke(조회 무변경·조건/필터·작은 창·규칙 초안·확인 이동·캐시 정리)도 통과했다. 증거 `/private/tmp/chagok-reference-tui-2d60vuku`, `/private/var/folders/6v/d8xjf8d57lg5yfbg7qgmv7g00000gn/T/chagok-analysis-oonacj29`.
- Windows x64 자체 포함 단일 EXE 게시 성공, 경고·오류 0개. PE x64/console, 번들 463개 항목, Chagok.dll/Chagok.Core.dll과 게시한 빌드 결과의 바이트 일치, 브랜드·탐색기 자산 8개 원본 보존, 최종 src/tests/scripts와 검증 복사본 일치를 확인했다. EXE 77,283,379 bytes, SHA256 `0aa4aa3d46f3bd25dd9860891a5219f66141ed20226a0b3e5a3aaebeb9f348c1`.
- 실제 Windows의 PATH 등록·해제·새 터미널 명령 검색·첫 데이터 가져오기·로그인 세션 간 구버전 차단·GUI/XAML·탐색기 연동은 이 macOS 환경에서 실행하지 못했다. Windows 확인 항목을 windows-test-guide.md에 추가했다. NuGet 복원은 공식 서버에 연결해 수행했다.
- 검증 중 외장 디스크 SDK의 AppleDouble 보조 파일을 MSBuild가 읽는 오류가 있어 `._*`를 제외한 내부 SDK·소스 복사본을 사용했다. 로그 `/private/tmp/chagok-rename-{build,checks,help,terminal,reference,analysis,publish}.log`, 패키지 검증 `/private/tmp/chagok-rename-package-verification.json`.
- 커밋·푸시·버전 변경·태그·GitHub 릴리스는 수행하지 않았다.

## 0.8.0 · 정리 방식 시작 화면과 기준 폴더 TUI (2026-10-20)

- GUI 시작 화면에 규칙으로 정리/기준 폴더로 정리를 나란히 배치했다. 기존 규칙 목록·검사·실행 화면은 유지하고 시작 화면으로 돌아가는 링크를 추가했다. 파일명 분석은 보조 기능으로 유지한다.
- 기준 폴더 방식만 검사 위치를 이번 작업에 한해 바꿀 수 있다. 같은 등록 경로는 제외 설정을 복제하고, 다른 경로는 영구 등록/규칙/설정을 변경하지 않는다. GUI 입력 변경 시 이전 이동 계획 생성을 차단한다.
- TUI 1/2 진입, O 시작 복귀, 검색어/검색 방법 편집, 후보 확인, 파일/그룹 목적지 지정, 보류, 페이지, 이동 계획 검토와 기존 명시 실행을 연결했다.
- 관련 핵심 검사 44개 통과: ReferenceOrganization 11개, OrganizationStart 1개(60/80/110열), 기존 분석/규칙/계획/실행 회귀 32개. 전체 검사를 실행했다고 주장하지 않는다.
- 실제 macOS PTY: 새 기준 폴더 흐름에서 별칭·복수 후보 선택·그룹 전체 지정·취소·5개 파일 명시 이동·설정 바이트 보존 통과. 별도 60열/24행·등록 폴더 없음·205개/3페이지·전체 그룹 지정·페이지 간 선택·취소와 파일/설정 보존 통과. 기존 규칙 생성/편집/개별 이동과 파일명 분석→규칙 초안→확인 후 이동 smoke도 통과했다.
- 증거: /private/tmp/chagok-reference-tui-cd3d1axz, /private/tmp/chagok-reference-pages-unlwn8e_, chagok-tui-rule-g5oqp3kq, chagok-analysis-4ax9ybmw. 핵심 회귀 로그 /private/tmp/chagok080-regression.log.
- Windows x64 자체 포함 단일 EXE 게시 성공. XAML 및 명시적 테마 색 연결을 정적으로 확인한다. 실제 Windows 렌더링·테마 전환·비활성/선택 상태·배율·IME는 macOS에서 실행하지 못했다. 수동 확인 항목을 windows-test-guide.md에 추가했다.
- 외장 SSD 읽기 오류로 내부 임시 복사본에서 검증했다. 반영 시 기준 SHA256과 원본을 비교해 다른 변경을 보존하고, 테스트/게시한 소스와 SSD의 최종 파일 일치를 확인한다. 배포물 검증 결과는 /private/tmp/chagok080-package-verification.json, 동기화 결과는 /private/tmp/chagok080-source-sync.json에 저장한다.
- 이전 ZIP을 보존한다. 커밋·푸시·GitHub 릴리스는 생성하지 않는다.

# 0.6.2 검증 · 2026-10-20

- 기존 조건 선택 GUI/TUI를 보존하고 정규식 입력 방법 창을 예시/직접 연습 TabControl로 분리. 예시 버튼에서 해당 식·파일명을 채워 이동, 탭만 왕복할 때는 초안 유지. 고정 prefix/suffix와 수정 fragment를 볼드/밑줄/색으로 구분하고 연결된 부분 편집칸 제공.
- FilenameRegexLessons의 4개 템플릿·14개 추천 수정과 설명은 실제 Preview 결과를 기준으로 구성. x 시작/y 정확/x 시작 또는 y, 접두사/연도 수정의 위치·의미와 결과를 연결. 전체 고급 편집은 유지하며 알려진 구조가 아니면 수정 범위를 추측하지 않고 안내 편집을 중지한다. 괄호 포함 예시는 비캡처 그룹으로 OR 범위를 보존.
- 실행 중 입력/예시/추천/적용 잠금, 탭 전환만 허용. 닫힘 뒤 continuation의 UI 갱신/이벤트 전달 차단. 연습 적용은 현재 식·첫 파일명만 분석 입력에 채우며 실제 파일·규칙·설정·분석 DB 변경 없음.
- 핵심 133/133 통과. 템플릿 조립/분해·고정 문법 보존, x/y 실제 비교와 OR 범위, 14개 추천/표시 결과 일치, 다른 전체 구조에서 분리 거부·잘못된 수정 식 오류 검사 4개 추가. 미구현 모델의 실패를 확인 후 구현(chagok-062-red.log). 기존 조건/분석/규칙/이동·저장 검사 포함.
- 보존 대상 5개 파일 SHA256(조건 Core/GUI/TUI, 분석 TUI, 규칙 GUI) 작업 전후 일치. 실제 macOS PTY의 조건 선택/편집/취소/추가삭제/AND/규칙 재편집/직접식·기본괄호 복귀/CLI metadata 해제 검사 재실행 통과. 증거 및 로그는 chagok-062-condition-preservation.log에 기록.
- Windows x64 단일 EXE 게시 성공. 컨트롤 DP 소유자 참조를 Control로 명시한 최종 소스로 게시·소스/테스트 복사본/managed bundle/7브랜드·탐색기 자산/9 ICO 일치 확인. EXE 77,251,959 bytes, SHA256 dbd86b73e989753734762805d2bc6f6921a310846b3759f3df030658127786f6.
- 도움말 JSON 19개/버전0.6.2와 문서 일치, XAML3개/상수 탭 스타일·템플릿 XML2개 파싱, HTML 앵커·git diff --check 통과. 읽기 전용 추가 검토에서 중요한 문제 없음. 조건 엔진/이동·TUI 화면은 바꾸지 않아 전체 기존 PTY3종은 반복하지 않음.
- 미검증: 실제 Windows 탭 템플릿 로딩·선택/키보드·IME·라이트/다크/시스템·배율·스크롤·UI 비동기 상태. 정적 XML/교차 게시를 실제 화면 실행으로 삼지 않는다. Windows 점검표에 추가. 이번 의존성 검사는 기존 복원 캐시 기반이며 새 온라인 취약성 조회를 수행하지 않음.
- 0.6.2 로컬 ZIP은 단일 EXE·LICENSE·외부 고지·최신 문서·SHA 목록 포함, ZIP CRC/내부해시·문서일치 확인. 0.6.0 태그와 기존0.6.0/0.6.1 ZIP 보존. 새 커밋/태그/GitHub 릴리스는 요청하지 않아 생성하지 않음.
- 로그 /private/tmp/chagok-062-checks.log, chagok-062-publish.log, chagok-062-condition-preservation.log. 빌드 복사본 /private/tmp/chagok-060-build는 디렉터리 이름만 이전 버전이며 최종 소스는0.6.2.

# 0.6.1 검증 · 2026-10-20

- 0.6.0은 122/122 재검증·최종 EXE/소스/번들 일치와 staged 자격증명 패턴 검사를 거쳐 06a56f7a740b8252636febd31db89bd9a1f431e5에 커밋. annotated tag v0.6.0 생성 후 origin/main과 태그를 atomic push 완료. 이전 테스트 ZIP 보존.
- 새 0.6.1은 GUI/TUI 조건 선택을 공통 FilenameConditionSet으로 구현. []/()/{} 안의 시작/정확/끝/포함, OR/AND, 1~10개·값 100자 리터럴 escape. 같은 괄호 안에서 AND 판단하며 실제 괄호 안 전체 값을 그룹화. 기존 괄호·직접 정규식·도움말 연습 유지.
- 옵션 Conditions/규칙 GroupConditions와 생성 정규식 불일치를 검증 단계에서 차단. 조건에서 만든 규칙은 정규식과 정확한 구분값도 저장. GUI/TUI/CLI 직접 식 수정 시 metadata 해제, 저장/취소 복제 유지.
- 핵심 129/129 통과. OR 예시·네 비교와 AND 경계·리터럴 기호·괄호 종류·JSON/규칙 왕복·오류/제한·실제 분석→정확 그룹 규칙→개별 계획·초안 clone·문자열 비교와 동등성 추가. 신규 테스트가 미구현 타입으로 실패하는 것을 확인 후 구현(로그 chagok-061-red.log). 기존 전체 회귀 포함.
- 실제 macOS PTY 조건 smoke 통과: G 조건 폼/괄호 순환·I 설명/60열·값 변경·적용·취소·빈값 저장 차단·동적 추가/삭제·AND·정확 그룹 조건 규칙 저장·재편집 취소·직접 E/기본 B·CLI 직접식 수정 metadata null/원본·설정 보존. 증거 chagok-conditions-ht_iszga.
- 기존 파일명 분석 CLI/PTY smoke 통과(괄호·정규식·페이지·취소/미리보기·규칙 생성·명시 이동·원본/캐시), 증거 chagok-analysis-t41suj3j. 기존 폴더/규칙 생성·편집·복제·최근 위치/규칙명 폴더·저장 실패 재시도·개별 이동·폴더 전체 검사 회귀 통과, 증거 chagok-tui-rule-cb8go84t.
- GUI 조건창 초기화 nullable 경고를 컨트롤 선언 초기화로 수정한 뒤 Windows x64 단일 EXE 게시 성공. 최종 소스/테스트와 복사본·managed bundle·7종 브랜드/탐색기 자산·9 ICO 일치 검증. EXE 77,244,716 bytes, SHA256 e2c085ac7d06d2b46b9da6767328107f4c93ad63f571f97bdae3139a914dcab6.
- 도움말 JSON 19개/버전 0.6.1과 docs/cli-reference.json 일치, XAML 3개 파싱·HTML 내부 앵커·git diff --check 통과. 추가 읽기 검토에서 중요·치명 문제 없음. 취약성 경고를 끄지 않고 기존 복원된 의존성을 사용한 빌드이며 이번 작업에서 새 온라인 취약성 조회를 수행한 것은 아님.
- 미검증: 실제 Windows WPF 조건창/콤보/키보드/IME·테마·배율·모니터별 크기, 트레이/탐색기·WMI·휴지통. Windows 점검표에 추가. Mac 교차 게시·PTY는 이 검사를 대신하지 않음.
- 0.6.1 로컬 테스트 ZIP에 단일 EXE·LICENSE·외부 라이선스 고지·최신 문서 포함, CRC/내부 SHA 목록·문서/소스 일치 확인. 이전 0.6.0 보존. 0.6.1 GitHub 릴리스는 생성하지 않음.
- 로그 /private/tmp/chagok-061-integrated-checks.log, chagok-061-condition-tui.log, chagok-061-analysis-tui.log, chagok-061-tui-regression.log, chagok-061-publish.log. SDK /private/tmp/chagok-sdk-20261020, 빌드 복사본 /private/tmp/chagok-060-build(디렉터리 이름만 이전 버전이며 소스는 0.6.1).

# 0.6.0 검증 · 2026-10-20

### 입력 도움말 직접 연습

- 예시마다 직접 연습을 펼쳐 정규식과 최대 10줄/4,000자의 파일명을 바꾸고 결과 보기로 시험. 파일명/찾은 전체 문자열/추출 그룹을 표로 나누며 그룹별 개수를 표시. 단계 안내와 예시별 바꿔 보기 과제, 경로 없이 파일명·확장자를 넣는 안내 추가.
- 연습한 식 사용은 현재 식과 첫 번째 파일명만 분석 화면에 채움. 실제 파일/설정/분석 저장소를 읽거나 바꾸지 않음. 공통 ExtractRegex를 재사용한 FilenameGroups.Preview로 전체 일치/그룹을 함께 조회. 입력 변경 시 오래된 표·합계를 숨기고 계산 중 입력/실행/적용 잠금, Task.Run으로 계산. 정규식 입력 Enter는 창 닫기 대신 연습 실행.
- 핵심 122/122 통과. 연습 전체 일치/명명 캡처 차이, 미일치/빈 일치, 같은 파일명 줄의 반복 구분값 1회 집계, 그룹 개수·기존 분석 Extract와 일치, 빈/잘못된/긴 식·시간 초과 검사 추가. 기존 분석/규칙/이동·저장 회귀 포함. 도움말 네 예시를 CLI 실제 분석으로 재확인하고 원본/설정 바이트 보존, 증거 chagok-help-examples-la1scqgr.
- 최종 Windows x64 단일 EXE 게시·소스/테스트 복사본·번들/7자산/9 ICO 일치, 도움말 JSON 19개/문서, XAML/HTML 앵커/git diff --check 통과. 읽기 전용 추가 검토에서 중요한 문제 없음. EXE 77,236,179 bytes, SHA256 5bdfebd520cd0ce11867ff360b8aececed9a7c8596020bcad80302b51ddf8e9e.
- 실제 Windows에서 펼치기/파일명·식 편집/실행 중 반응/결과 표/접기·다시 열기/연습한 식 사용/Esc/Enter·IME/테마·배율은 미검증. 점검표 추가. TUI 자체는 바꾸지 않아 실제 PTY 전체 회귀는 재실행하지 않음.
- 버전 0.6.0 유지, 직전 0.6.0 테스트 ZIP과 풀린 폴더를 dist/archive에 보존. GitHub 게시하지 않음. 로그 /private/tmp/chagok-060-practice-checks.log, chagok-060-practice-publish.log.


### 입력 방법 가독성 보완

- 원인: 작은 시스템 MessageBox에 긴 문장·기호·예시를 한꺼번에 넣어 파일명/정규식/결과를 구분하기 어려웠고 앱 테마와도 달랐다.
- 변경: 테마 리소스를 사용하는 큰 도움말 창, 제목·예시 카드 4개·입력 식/파일명/그룹 분리, Ctrl+C 가능한 코드 입력, 예시 사용 버튼, 접힌 기호/집계 설명, 작은 창 스크롤/고정 닫기/Esc. 예시 선택은 식·시험 파일명만 변경하고 재분석이나 이동을 하지 않는다. 기본 툴팁은 짧은 안내로 교체.
- 실제 검증: 창에 선언한 네 식/시험 파일명/그룹을 공통 CLI 분석 엔진으로 읽어 네 예시 모두 예상 그룹·파일 목록에 일치, 원본/설정 바이트 보존 확인. 증거 chagok-help-examples-a_bchw7n. Windows x64 단일 EXE 게시, 최종 소스/테스트와 빌드 복사본·번들 DLL/7자산/9 ICO 일치. 도움말 JSON 19개/문서 일치, XAML 파싱/HTML 앵커/git diff --check 통과. 파일 이동 엔진과 TUI는 바꾸지 않아 전체 회귀는 재실행하지 않음.
- EXE 77,231,673 bytes, SHA256 0373915e74478ab84d2891ffc3227f59da0f67a1ae7f62528567f430d45fb583. 버전 0.6.0 유지. 직전 0.6.0 테스트 ZIP은 dist/archive에 보존, GitHub 게시하지 않음.
- 미검증: 실제 Windows 도움말 창의 글자/줄 간격·배율·모니터별 크기·라이트/다크/시스템·키보드/스크린리더 동작. Windows 점검표 추가. 로그 /private/tmp/chagok-060-help-publish.log.


### 같은 0.6.0 추가 보완 · 직접 경로·정규식

- GUI 별도 기본 위치 입력칸을 제거하고 주 이동할 폴더를 직접 편집하도록 변경. 최종 경로에서 규칙명 한 개를 역산하고 입력 완료 시 갱신, 이름/옵션/최근 위치/찾아보기 변경과 강제 자동 생성 정책 유지.
- GUI 직접 입력 분류 정규식/입력 방법·툴팁/시험 파일명 미리보기. TUI E 직접 선택형 정규식 폼·예시·전체 설명·미리보기·저장 검토/취소. CLI --regex/--group-expression. GroupExpression과 정확한 구분값 Pattern 저장, 기존 괄호 Group 호환, 공백/대소문자 보존.
- 핵심 119/119 통과. 캡처/전체 일치/반복 캡처/빈 값·공백·한글·대소문자·오류/길이 제한, 분석/규칙 JSON 왕복·정확 비교, 100ms 초과 시 분석과 이동 검사 중단/임시 캐시 정리/원본 보존, 경로 직접 편집·접미 중복 방지·이름 변경 검사 포함. 검사 중 정규식 시간 초과를 일반 파일 생략 경고로 삼지 않도록 전용 예외 구분.
- 실제 macOS CLI/PTY: 직접 식 분석/확장자 그룹 집계, 잘못된 식 적용 차단·미리보기·취소·좁은 창·그룹 규칙 저장·그룹 내 파일만 명시 이동 통과. 증거 chagok-analysis-p8h0z1aa. 기존 폴더/규칙 직접 편집·취소/저장 재시도·최근 위치·하위 폴더·개별 이동 회귀 통과, 증거 chagok-tui-rule-tm8kzuez.
- 최종 Windows x64 단일 EXE 교차 게시 및 src/tests 복사본·managed bundle·7종 자산·9종 ICO 일치 검증. EXE 77,229,651 bytes, SHA256 452b8255b6f09a3cf85f07cf0e6fe3eab41c7e6d0c33228a7a4e60c7be9427c8. 도움말 19개·JSON 문서 일치, XAML 3개 파싱, HTML 내부 앵커·git diff --check 통과.
- 실제 Windows GUI 경로 직접 입력/한글 IME·정규식 미리보기/툴팁·테마·배율·탐색기 동작은 미검증. Windows 점검표에 추가. CLI에는 별도 시험 파일명 미리보기 명령을 추가하지 않았으며 GUI/TUI의 입력 폼에서 제공.
- 버전 0.6.0 유지. 이전 0.6.0 테스트 ZIP/풀린 폴더를 dist/archive에 보존하고 새 테스트 ZIP에 LICENSE/외부 고지/최신 설명서 포함. 이번 보완에서 GitHub 커밋·게시하지 않음.
- 로그 /private/tmp/chagok-060-custom-checks.log, chagok-060-custom-analysis.log, chagok-060-custom-regression.log, chagok-060-custom-publish.log. 아래 첫 구현 결과는 추가 보완 전 기록이다.

### 첫 구현 검증

- 승인 시안의 폴더별 집계/구분값 목록/선택 그룹 파일 목록을 GUI에 추가. 등록 폴더 분석·규칙 화면 진입, []/()/{}·재귀·2개 이상 필터, 개수순·비율, 그룹/파일 100개 페이지, 전체 경로·일부 생략 경고·진행/취소·규칙 초안 연결.
- 공통 FilenameAnalysis 엔진은 이름만 읽고 임시 SQLite에 파일별 구분값/집계 저장. 디스크 순회·임대/정리 재사용. 같은 값은 파일당 1회·다중 값은 각각 집계, 대소문자·공백 구분, 빈/중첩 괄호 제외. 폴더 제외·최종 목적지·캐시·링크 제외.
- TUI P 파일명 분석: 넓은 창 세 영역/좁은 창 현재 영역, Tab/화살표·필터·괄호·재귀·페이지·I/W·C 규칙. CLI analyze/JSON도 같은 엔진 사용. 그룹 초안은 NameMatch.Group으로 집계와 같은 조건을 사용하며 저장·검사·사용자 실행 승인을 재사용.
- 핵심 115/115 통과. 새 분석 10개: 추출·대소문자·반복/다중 값·총계, 재귀/제외/목적지/자가 캐시, 폴더별/전체 및 누락 경고, 206그룹/205파일 페이지, SQL 구분값 주입 방지/스냅샷, 그룹 규칙 일치, 활성 임대 보존/정리, 취소/원본 보존, 링크 제외. 기존 회귀와 도움말 포함.
- 실제 CLI/PTY: 분석/필터 변경 시 설정 바이트·원본·목적지 불변, square/round/curly·재귀·폴더별/합산·페이지 범위 오류, TUI 세 영역/70열 작은 창·I·도움말·필터·괄호·재귀, C 규칙 조건/최근 위치 미리 채움·저장만으로 미이동, 이후 명시 run으로 [a] 파일만 이동/[A]·하위·제외 보존. 종료 후 캐시 정리. 증거 chagok-analysis-7s9ierjy.
- 기존 TUI 폴더/규칙 생성·편집·복제·직접 항목·최근 위치·하위 폴더 이동·작은 창·취소·저장 실패 재시도 회귀 통과. 구분값 옵션 추가에 맞게 선택 순환 검사를 갱신. 증거 chagok-tui-rule-qo8jkv29.
- 최종 소스/테스트와 빌드 복사본 일치, Windows x64 단일 EXE 게시·PE/console·managed bundle 일치·7종 브랜드/탐색기 자산·9종 ICO 검증. EXE 77,225,269 bytes, SHA256 aa33f9223a66cacbab2f958a352af04904639acf5f3f766d0697e00c1a8b310b. 생성 실패 시 오류 래핑을 보완한 최종 소스로 핵심 검사/게시 재수행.
- XAML 정적 파싱, HTML 내부 앵커, CLI 도움말 19개 JSON과 docs/cli-reference.json 일치. Windows 실제 GUI/테마/배율/키보드/접근 거부·드라이브 분리·동시 프로그램 이벤트는 미검증으로 점검표에 추가. 실제 전체 메모리 측정은 수행하지 않음.
- 테스트 ZIP은 LICENSE·기존 외부 고지·최신 설명서를 포함. 이전 버전 보존. 이번 작업에서는 GitHub 커밋/릴리스·개발 공유 페이지 갱신을 하지 않음.
- 로그 /private/tmp/chagok-060-checks.log, chagok-060-analysis-tui.log, chagok-060-tui-regression.log, chagok-060-publish.log. SDK /private/tmp/chagok-sdk-20261020, NuGet /private/tmp/chagok-nuget-20261020.

# 0.5.8 검증 · 2026-10-20

- 새 규칙에 최근 기본 이동 위치를 미리 채우고 콤보박스 유지. 기존 규칙 편집은 원래 경로, 탐색기 명시 목적지는 전달 경로를 우선. 초기값 조회만으로 설정이나 최근 기록을 바꾸지 않음.
- GUI의 ‘규칙명 폴더 사용’을 켜면 주 이동할 폴더 입력칸에 최종 경로를 읽기 전용으로 표시. 아래 기본 위치 입력·찾아보기·최근 위치 선택·규칙명 변경 시 갱신. 끄면 기본 경로 편집으로 복원하며 저장은 기본 경로만 사용.
- TUI 폴더 7개·규칙 15개 항목에 기능·연계 설명 추가. I에서 현재 값과 전체 설명을 스크롤. 조건별 비활성 이유를 구분하고 강제 자동 생성은 ‘켜짐 · 자동 적용’으로 표시. 조회 항목 Enter는 수정 상태를 만들지 않음.
- 핵심 105/105 통과. 초기값의 최근 옵션·명시 목적지 우선순위·설정 무변경, 상태별 설명 검사 추가. 기존 이동·저장·취소·공유 설정·자동 제외·이전 JSON 회귀 유지.
- 실제 macOS PTY 통과: 새 규칙 최근 기본 위치 자동 채움·I 전체 값/설명, 임시 입력 위치에서 최근 위치 재선택, 규칙명 폴더 강제 자동 생성 표시·비활성 이유·전체 설명, 실제 개별 하위 폴더 이동, 취소·편집·복제·작은 창·설정 실패 재시도. 증거 chagok-tui-rule-4zv0n60r. 설명 영역을 추가하면서 경로 끝이 기본 화면에 보인다는 테스트 가정은 I 전체 값 검증으로 수정.
- Windows x64 단일 EXE 게시와 최종 src/tests 복사본 일치, PE/console, 번들 managed payload·브랜드/탐색기 7종 자산·9종 ICO 검증. EXE 77,202,530 bytes, SHA256 e0ed34c6e92650b121cc07a43495524ab8e6197fcfbccf74bcf0414c23ed6391.
- help JSON과 docs/cli-reference.json 일치. 설명서 HTML 내부 앵커 검사 통과. ZIP에 LICENSE와 네이티브 고지를 유지. 이전 릴리스 보존, GitHub 게시/커밋은 수행하지 않음.
- 미검증: 실제 Windows GUI의 경로 입력·읽기 전용/기본 위치 전환·테마·배율·IME. Windows 점검표에 추가. 빌드 초기 NuGet 취약성 조회가 네트워크 제한으로 실패하여 경고를 끄지 않고 공식 NuGet에 연결해 복원 완료.
- 로그 /private/tmp/chagok-058-checks.log, chagok-058-tui.log, chagok-058-publish.log. SDK /private/tmp/chagok-sdk-20261020, 패키지 /private/tmp/chagok-nuget-20261020.

# 0.5.7 검증 · 2026-10-20

- 최근 이동 위치를 검사 폴더별로 표시·기록하는 옵션 추가. 기본 꺼짐. 성공한 규칙 저장 시 기본 위치 10개를 최신순·중복 없이 보관하며 취소·저장 실패·꺼짐 때 새 기록 없음.
- 기본 위치와 규칙명 폴더 옵션을 분리. 최종 경로 미리보기·규칙 카드·대시보드·상세·검사·자동 제외·충돌·실행에 공통 적용. 옵션을 켜면 기존 자동 생성 설정과 무관하게 실행 때 생성. Windows 금지 문자·예약 이름·경로 탈출 거부.
- 핵심 103/103 통과. 규칙명 변경·옵션 해제·복제/JSON 보존, 검사 시 미생성·실행 시 실제 생성/이동, 최종 경로 자동 제외·번호 충돌, 최근 10개·폴더 분리·중복 제거·꺼짐 기록 보존, 저장 실패/재시도와 이전 JSON 기본값 검사 포함.
- 실제 macOS PTY 회귀 통과. 폴더 최근 위치 옵션 저장 → 첫 규칙의 기본 위치 기록 → 다음 규칙에서 최근 위치 선택 → 규칙명 하위 폴더로 개별 실행 → 취소 시 설정 바이트 보존. 기존 폴더/규칙 생성·편집·복제·개별 실행·작은 창·설정 실패 재시도도 통과. 증거 chagok-tui-rule-5pmjmheu.
- CLI 새 두 옵션 입력·scan 최종 Target·create off 상태의 규칙명 폴더 실제 자동 생성/이동·금지 이름 저장 거부/설정 바이트 보존 통과. help JSON과 docs/cli-reference.json 일치. 증거 /private/tmp/chagok-057-cli-an5gt182.
- Windows x64 단일 EXE 게시 성공. 최종 src/tests와 빌드 복사본 일치. PE x64/console, 단일 파일 managed payload와 빌드 DLL 일치, 로고/탐색기 7종 자산과 9종 ICO 임베딩 검증.
- EXE 77,200,564 bytes. SHA256 27e977b9815d376c359b01236137a9632e0c1ac94eee72597860c30f9099a0ca. 이전 릴리스 보존. 새 패키지는 LICENSE·최신 문서·네이티브 라이선스 고지를 포함하며 GitHub에는 아직 게시하지 않음.
- 미검증: 실제 Windows GUI/콤보박스·다크/라이트/시스템·배율·키보드·프로그램 자동 실행·휴지통. Windows 점검표에 회귀 항목 추가. 이전 버전으로 새 설정 편집 시 새 옵션 유실 가능성을 README에 명시.
- 임시 SDK/패키지 캐시 일부가 정리되어 기존 SDK 원본에서 새 내부 복사본을 만들고 공식 NuGet에서 의존성을 복원. SDK /private/tmp/chagok-sdk-20261020, 패키지 /private/tmp/chagok-nuget-20261020. 로그 /private/tmp/chagok-057-checks.log, chagok-057-tui.log, chagok-057-publish.log.

# 0.5.6 검증 · 2026-10-19

- 폴더 추가/편집/복제와 규칙 추가/편집을 항목 선택형 목록으로 변경. ↑↓/Tab/1~9/Home/End 선택, Enter/E 텍스트 편집, ←→ 선택값 변경, I 전체 값, S 검토 후 Y 저장.
- 텍스트 입력창 취소는 해당 항목만 취소하고 초안을 유지. 목록 Esc는 전체 초안 취소. 검토 취소/저장 검증 실패도 초안을 유지해 바로 수정 가능. 비활성 패턴/프로그램 항목의 기존 값 보존.
- 검사 경로 수정 시 제외 경로를 현재 기준 경로에서 재배치하므로 수정 순서와 관계없이 일관되게 표시. 재배치 실패 시 기존 경로·제외 목록을 유지.
- 핵심 96/96 및 CLI 도움말 40회 통과. help JSON/문서 명세 일치.
- 실제 macOS PTY: 규칙 목적지·확장자부터 직접 수정/한글·커서 편집/빈 Enter 유지/항목 취소와 전체 취소/조건 비활성/패턴 보존/누락 목적지 저장 실패 후 복구/검토 취소 초안 유지 통과. 저장 후 새 규칙 선택·개별 TXT 이동/PDF 보존·전체 검사 범위도 유지.
- 기존 규칙의 확장자 하나만 편집한 뒤 ID·나머지 필드가 그대로인지 확인. 폴더 생성/프로그램 조건·경로, 폴더 복제/제외 경로 재배치·원본 보존, 복제 폴더 이름만 편집 후 ID/나머지 값 보존 통과.
- 60열 × 24줄에서 마지막 항목 자동 스크롤·저장/취소 안내, 180자 입력 항목 취소/전체 취소·settings.json 바이트 보존 통과. 설정 직접 선택·저장 실패 재시도 회귀 통과.
- Windows x64 단일 EXE 게시, 최종 소스/자산 일치·번들 DLL/7종 자산/9종 ICO 검증. 최종 제외 경로의 세미콜론 구분 안내 문구를 보완해 재게시. EXE 77,198,350 bytes, SHA256 2f88fba7cc57ea8d3f1c915eda4768cda27675725b7f096bb30c48b05d9b5360.
- 미검증: 실제 Windows 콘솔 화면/폰트/IME·GUI/탐색기/WMI/휴지통. 의존성 캐시 복원이며 최신 온라인 취약성 조회 아님.
- 로그 /private/tmp/chagok-056-checks.log, chagok-056-tui.log, chagok-056-publish.log. PTY 증거 chagok-tui-rule-dvtffujh. 이전 릴리스 보존.

# 0.5.5 검증 · 2026-10-19

- 설정을 순차 입력에서 항목 선택형 화면으로 변경.
- 설정 네 항목과 현재 값·구역 테두리·항목 설명·미저장 표시를 한 화면에 표시. ↑↓/Tab/1~4로 선택, ←→/Enter/Space 변경, S 저장, Esc 취소. GUI 전용 설정과 TUI 팔레트·실제 탐색기 등록을 구분.
- 임시 설정 복사본만 수정하며 S 전에는 저장하지 않음. 원래 값으로 되돌리면 미저장 표시 해제. 변경 없이 S는 파일을 다시 쓰지 않음. 저장 실패 시 오류와 수정 값을 유지해 S로 재시도.
- 핵심 96/96 통과. 특정 필드만 변경·원본 보존·되돌리기와 60/80/100열 전체 항목/설명/저장/취소 표시 검사 추가. CLI 도움말 40회 및 런타임 JSON/명세 일치.
- 실제 macOS PTY: 다른 항목을 거치지 않고 네 번째 항목 선택·수정·도움말 왕복·Esc 취소, 설정 파일 바이트 보존. 60열 화면에서 네 번째 항목만 저장 후 다른 값/폴더/규칙 보존, 재진입/값 되돌리기/변경 없는 저장 시 바이트 보존 확인.
- 테스트 전용 settings.json.tmp 디렉터리로 저장 실패를 유발하고 원본 유지·오류 표시·같은 화면 재시도 성공 확인. 기존 폴더/규칙 생성·복제·프로그램 조건·개별 이동·전체 검사·상세·취소 회귀 통과.
- Windows x64 단일 EXE 게시. 최종 소스/자산 일치·번들 DLL/7종 자산/9종 ICO 포함 검증. EXE 77,195,415 bytes, SHA256 d1e792a2a44647c438f755d40d10f3eecd72576b370fff21afd726d6a510915f.
- 미검증: 실제 Windows 콘솔 폰트/화면/IME·GUI/탐색기/WMI/휴지통. 의존성 캐시 복원이며 최신 온라인 취약성 정보 조회 아님.
- 로그 /private/tmp/chagok-055-checks.log, chagok-055-tui.log, chagok-055-publish.log. PTY 증거 chagok-tui-rule-hsq6gj6c. 이전 릴리스 보존.

# 0.5.4 검증 · 2026-10-19

- 폴더 추가/편집/복제와 규칙 추가/편집을 공통 TuiForm으로 표시. 매 단계 화면을 갱신하고 테두리·구역·현재 값·번호 선택지·최근 입력·검사 폴더 문맥 유지. 저장 전 TuiDetails 구성을 스크롤한 뒤 Y로 저장.
- 입력은 기존 TerminalText 편집 로직을 재사용하되 지정 행 안에서 갱신. 커서 위치 조회를 하지 않음. Esc, /cancel, /clear, 빈 Enter 기본값, 한글·커서 편집 유지.
- 핵심 94/94 통과. CLI 도움말 40회 통과 및 0.5.4 런타임/JSON 명세 일치.
- 실제 macOS PTY: 규칙 추가·즉시 선택·저장 전 검토 스크롤·개별 TXT 이동/PDF 보존·전체 검사·취소·상세·도움말·선택 배경 회귀 통과. 폴더 실제 생성/프로그램 시작 조건/프로그램 경로/감지 후 처리 저장 및 폴더 복제/새 ID/검사 경로 보존 확인.
- 60열 × 24줄 실제 PTY에서 파일명 조건 4개와 Enter/Esc 안내 동시 표시, 180자 입력 후 Esc 취소·settings.json 바이트 보존 확인. 기존 한글 삭제/Home/End/Delete와 폴더·규칙·설정 취소 회귀도 통과.
- Windows x64 단일 EXE 게시 성공. 최종 소스/자산 일치·번들 DLL/7종 자산/9종 ICO 포함 검증. EXE 77,194,253 bytes, SHA256 8ec37d1df19206e159008d7e9105b2edb52be6d1c4cd82ca132e8079ad3656f5.
- 미검증: 실제 Windows 터미널 화면/IME/배율/폰트·GUI/탐색기/WMI/휴지통. 캐시 의존성 복원이며 최신 온라인 취약성 정보 조회 아님.
- 로그 /private/tmp/chagok-054-checks.log, chagok-054-tui-final.log, chagok-054-publish.log. PTY 증거 chagok-tui-rule-itpzqq3j. 이전 릴리스 보존.

# 0.5.3 검증 · 2026-10-19

- 원인: 폴더/규칙 상세를 일반 오류 Notice로 전달했고 Clean이 줄바꿈을 제거해 경로·이름·목적지가 붙어 표시됨.
- 변경: 폴더/규칙 전용 상세 화면. 항목 이름과 값을 구분하고 기본/검사·파일 조건·이동·실행 조건·ID를 섹션별 테두리로 표시. 긴 경로는 셀 폭으로 줄바꿈하고 전체 내용을 스크롤로 확인. Esc/Enter 목록 복귀, F1/? 문맥 도움말.
- 핵심 94/94 통과. 30/60/100열 내용 폭·긴 한글 경로 보존·프로그램 조건/한글 라벨 검사 추가.
- 실제 macOS PTY 폴더 I 상세/스크롤/목록 복귀와 규칙 I 상세/이동 동작 스크롤/목록 복귀 통과. 상세보기 전후 settings.json 바이트 일치, 기존 폴더·규칙 선택 유지. 기존 규칙 추가·즉시 선택·개별 이동·전체 검사·취소·도움말·선택 배경 회귀도 통과.
- CLI 도움말 40회 통과, 0.5.3 런타임 JSON과 문서 일치.
- Windows x64 단일 EXE 게시, 최종 소스/자산 일치·번들 DLL/7종 자산/9종 ICO 포함 확인. EXE 77,192,701 bytes, SHA256 fe3d82912c34c968cb6b2fdd960c633a0ece4fdf209a1f7c1f0c52e4e7d1f982.
- 미검증: 실제 Windows 콘솔 폰트/색상/화면·GUI/탐색기/WMI/휴지통. 의존성 캐시 복원이며 최신 온라인 취약성 조회 아님.
- 로그 /private/tmp/chagok-053-checks.log, chagok-053-tui.log, chagok-053-publish.log. PTY 증거 chagok-tui-rule-p3s0h972. 기존 릴리스 보존.

# 0.5.2 검증 · 2026-10-19

- TUI CHAGOK 이름을 사용자 승인 D안 도트 문자로 교체. 시안 JSON과 실제 배너 5줄의 이름/심볼 문자열 일치 확인. 청록 심볼·파랑 이름·NO_COLOR·배너 뒤 색 복원 유지.
- 핵심 93/93 통과. 기존 60/80/110열 × 24/32줄 폭/높이 검사 포함.
- 실제 macOS PTY에서 D안 이름 표시/이전 블록 글자 제거 확인. 규칙 추가/새 행 선택/개별 TXT 이동·PDF 보존/폴더 전체 검사/취소/도움말/선택 배경 범위 회귀 통과.
- CLI 도움말 40회 통과. 0.5.2 런타임과 docs/cli-reference.json 일치.
- Windows x64 단일 EXE 게시 성공. 최종 소스/자산 일치, 번들 DLL/7종 자산/9종 ICO 포함 확인. EXE 77,190,616 bytes, SHA256 95811f27600dab596d2af9d4f8216adf3b9afa70adeb1e0821c836b292d9649a.
- 실제 PTY 기반 docs/tui-preview.html 갱신. Windows 실제 폰트/팔레트/화면은 미검증. GUI/탐색기/WMI/휴지통 실행도 이 환경에서 미검증. 캐시 의존성 복원이며 최신 온라인 취약성 정보 조회 아님.
- 로그: /private/tmp/chagok-052-checks.log, chagok-052-tui.log, chagok-052-publish.log. PTY 증거 chagok-tui-rule-rszikbio. 기존 릴리스 보존.

# 0.5.1 검증 · 2026-10-19

- 원인: 선택 문자가 들어간 전체 출력 줄에 배경색을 지정해 좌우 패널과 간격까지 강조됨. 파일명 속 >도 잘못된 선택색을 만들 수 있었음.
- 변경: 실제 선택 위치를 행/셀 열/내용 폭으로 전달. 현재 조작 패널만 배경 강조, 반대쪽 선택은 ·와 청록 글자. 테두리와 패널 간격은 원래 배경 유지. 미리보기에도 명시적인 선택 범위를 사용.
- 실제 macOS PTY에서 ANSI 선택 배경이 테두리/개행을 포함하지 않음과 반대 패널의 · 표시 확인. 규칙 추가·즉시 선택·개별 이동·전체 검사·취소·도움말 왕복도 통과. 로그 /private/tmp/chagok-051-tui-final.log.
- 핵심 93/93 통과. 한글/이름 속 >, 좁은 창/넓은 창, 다른 행의 선택, 빈 목록에서 선택 범위와 셀 폭 검사.
- 도움말 CLI 40회 조회 통과. docs/cli-reference.json과 0.5.1 런타임 일치.
- Windows x64 단일 EXE 게시 성공. 최종 소스/자산 일치, 번들 DLL/7종 브랜드·셸 자산/9종 ICO 포함 검증.
- EXE 77,190,633 bytes, SHA256 ea9ae38050a7548cf1e1fe3ef273d68fa94e65180fc17fbc867513495df414c3.
- 미검증: 실제 Windows 콘솔 화면·폰트·팔레트·GUI·탐색기·WMI·휴지통. 의존성은 기존 캐시로 복원했으며 최신 온라인 취약성 조회는 수행하지 않음. GUI 실행 시 콘솔 잠깐 표시되는 기존 시작 방식은 변경하지 않음.

# 0.5.0 검증 · 2026-10-19

- TUI 심볼을 승인 아이콘의 40×20 표본에서 옮긴 20×5 점 문자로 세밀히 표현. 기존 CHAGOK 픽셀 글자 유지, 청록 심볼/파랑 글자 복원. 실행 중 이미지 읽기 없음.
- 폴더/규칙 패널·상세·미리보기·기록 목록 테두리, 선택 문자/강조. 60/80/110열 × 24/32줄에서 문자 폭·행 수와 실행/도움말 안내 검사.
- F1/? 기능 설명, 문맥에 맞는 설명 시작·좌우 기능 전환·스크롤·Esc 복귀. 설정/규칙/미리보기 선택 보존.
- HelpCatalog 18개 명령: 목적·입력 형식/필수값/기본값/허용값·사용 예·변경 효과·승인·환경·안전 기준·종료 코드. help [명령], [명령] --help, --json. help/version은 세션 잠금과 설정 읽기 없이 처리.
- 핵심 92/92 통과. 명령별 도움말 파싱·AI JSON 안전 정보·전체 카탈로그·작은 창 테두리 레이아웃 추가.
- tests/help_smoke.py 실제 40회 CLI 조회 통과. 모든 18개 명령의 help 명령/--help JSON 일치, docs/cli-reference.json과 런타임 명세 일치, 없는 명령 거부, 도움말이 데이터 폴더/잠금을 만들지 않음.
- 실제 macOS PTY: 메인/미리보기 도움말 왕복 후 설정·선택 보존, TUI 세션 잠금 중 CLI 도움말 정상 조회. 기존 폴더 문맥/추가/취소/한글 편집/개별 TXT 이동·PDF 보존/전체 검사/정상 종료 통과.
- 기존 CLI 18명령·105파일 이동·2페이지·선택·설정·교차 잠금·비정상 종료 후 재진입 검사 통과.
- Windows x64 단일 EXE 게시·최종 소스 일치·번들 DLL/7종 자산·9종 ICO 포함 검증. EXE 77,189,012 bytes, SHA256 ee6491b48b4366d52f4718297d0b838d40c09b07070bbcbfcb7d522686efc1bb.
- docs/tui-preview.html은 실제 PTY 출력에서 경로/이름을 예시로 바꾼 문자·SGR 색 미리보기. HTML 정적 구조·제어 문자·개인 경로 잔존 확인. 로컬 file: 브라우저 열기는 CUA URL 정책으로 거부되어 브라우저 렌더 검증은 수행하지 못함. 우회하지 않음.
- 미검증: 실제 Windows 점 문자 폰트/폭/색, F1/?·IME·탐색기·GUI·트레이·WMI·휴지통. 캐시 패키지 복원이며 최신 온라인 취약성 정보 조회 아님.
- 로그: /private/tmp/chagok-050-checks-final.log, chagok-050-help-final.log, chagok-050-tui-final.log, chagok-050-smoke.log, chagok-050-publish.log. PTY 증거 chagok-tui-rule-1qsus78t.

# 0.4.4 검증 · 2026-10-19

- 원인: TUI 입력이 ReadLineAsync로 Enter만 기다려 작성 중 목록 복귀 경로가 없었음.
- 처리: 키 단위 입력으로 Esc 및 /cancel + Enter를 폼 전용 취소 예외로 처리. 앱 취소 토큰/Ctrl+C와 구분. 폴더/규칙/설정은 기존 복사본 저장 흐름을 유지해 취소 시 저장하지 않음.
- 입력 유지: 빈 Enter는 기존 값, /clear는 비우기. 좌우/Home/End/Delete/Backspace, 한글/결합 문자 삭제, 긴 입력의 한 줄 가로 이동. 입력 갱신은 상대 위치로 수행해 터미널 커서 위치 응답에 의존하지 않음.
- 핵심 89/89 통과.
- 실제 macOS PTY: 폴더 추가 첫 칸에서 부분 입력 후 Esc, 다음 경로·제외 경로에서 /cancel, 폴더 편집/복제·규칙 추가·설정에서 Esc 복귀. 취소 전후 settings.json 바이트 일치. 앱 종료/선택 변경 없음.
- 실제 입력에서 가나 두 글자 Backspace, ASCII Backspace, Home/Right/Delete/End 수정 뒤 new-rule 저장 확인. 규칙 추가 문맥·새 행 선택·개별 TXT 이동/PDF 보존·전체/선택 취소·정상 종료도 통과.
- Windows x64 self-contained 단일 EXE 게시·최종 소스 일치·번들 DLL/7종 자산·9종 ICO 포함 검증. EXE 77,177,824 bytes, SHA256 1bd6a1d28f93701020eb7d096aff5fbd92343f4c64c7a2fa494e39df38f6d0cf.
- 미검증: 실제 Windows 한글 IME 조합/붙여넣기/콘솔 입력·화면, 탐색기/GUI/트레이/WMI/휴지통. 실제 IME 조합 중 Esc와 조합 완료 후 Esc를 구분해 테스트 PC에서 확인해야 함.
- 캐시 패키지 복원이며 최신 온라인 취약성 정보 갱신을 수행하지 않았음.
- 로그: /private/tmp/chagok-044-checks-final.log, chagok-044-tui-final.log, chagok-044-publish.log. PTY 증거 chagok-tui-rule-8g1c2mug.

# 0.4.3 검증 · 2026-10-19

- 규칙 추가/편집 시작 및 저장 확인에 선택 폴더 이름·전체 경로 표시. R 이 규칙만 검사 및 실행 / A 폴더 전체 검사 및 실행을 별도 줄로 표시. 이동 전 확인 유지.
- TUI 상단만 정돈: 굵은 일정한 획의 CHAGOK, 더 넓은 심볼, 청록 단색. 80열 미만은 심볼을 생략해 이름을 잘리지 않게 함. GUI 자산은 변경하지 않음.
- publish 후 MSBuild가 Chagok-v0.4.3.exe로 이름을 변경한 결과 확인. 내부 어셈블리 Chagok·리소스 경로는 유지.
- 탐색기 패키지 manifest는 현재 EXE 파일명으로 XML 구성. ampersand가 들어간 버전 파일명 이스케이프와 잘못된 이름 거부 검사 추가.
- 핵심 89/89 통과. 실제 macOS PTY에서 두 번째 검사 폴더 선택 → 규칙 추가의 이름/전체 경로 → 새 규칙 선택 → TXT만 이동/PDF 보존 → 전체 검사 → 취소 보존 → 정상 종료 통과. 개별/전체 실행 문구가 실제 화면 출력에 있음을 확인.
- Windows x64 self-contained 단일 EXE 게시, 최종 소스/자산 일치, 번들 DLL·7종 자산·9종 ICO 검증. EXE 77,177,026 bytes, SHA256 0d1ef23eeeafe45ffcef10f3a2990bc02b5d7eba947e0161989b5dfa72701783.
- 미검증: 실제 Windows 버전 EXE 실행·탐색기 재등록/첫 메뉴·글꼴/팔레트·트레이·WMI·휴지통. SDK 캐시 소스로 복원해 최신 온라인 취약성 정보는 갱신하지 않았음.
- 로그: /private/tmp/chagok-043-checks.log, chagok-043-tui-final.log, chagok-043-publish.log. PTY 증거 chagok-tui-rule-ljp0q54h.

# 0.4.2 검증 · 2026-10-19

- 변경: 선형 로고를 5줄 블록 심볼+CHAGOK 픽셀 글자로 교체. 원본 아이콘의 밝은 영역을 14×10 셀 문자로 옮겨 코드에 저장했으며 실행 중 이미지를 읽지 않음. 청록/파랑 두 색과 본문 글자색 복원, 밝은 콘솔 배경·NO_COLOR 분기.
- 핵심 88/88 통과. 실제 macOS PTY 폼 저장·새 규칙 선택·R 개별/A 전체·취소·정상 종료 통과.
- 실제 출력에서 ANSI 96m(밝은 청록), 94m(밝은 파랑), 배너 뒤 39;49m(기본색 복원) 확인. NO_COLOR를 상속한 첫 실행에는 색 코드가 없음을 확인한 뒤 색 검사 프로세스에서만 해당 변수를 제거해 색상 검사 수행.
- Windows x64 self-contained 단일 EXE 게시·최종 소스/자산 일치·번들 DLL과 7종 자산·9종 ICO 포함 검증.
- EXE 77,176,418 bytes, SHA256 8d7e0b22c5325e40210dc5026acd9a96e9a930ac867fc0f21e414999fbf63ec5.
- 미검증: 실제 Windows 폰트/색 팔레트·밝은 배경에서의 외관. 기존 Windows GUI/트레이/WMI/휴지통 항목도 이 변경에서 재검증하지 않음. 오프라인 캐시 복원이며 최신 온라인 취약성 정보 갱신 아님.
- 로그: /private/tmp/chagok-042-checks.log, chagok-042-tui.log, chagok-042-publish.log. 실제 PTY 증거: chagok-tui-rule-hlx8nqib.

# 0.4.1 검증 · 2026-10-19

- 수정 전 실제 TUI 재현: 기존 규칙 12개에서 새 규칙 저장 후 JSON에는 반영됐지만 새 행과 선택 규칙은 화면에 표시되지 않았음. /private/tmp/chagok-tui-rule-repro.py, 증거 chagok-tui-rule-42t6rtc0.
- 수정: 저장 성공 ID를 최신 설정에서 찾아 선택·표시 위치 이동. F/N을 폴더/규칙 추가로 구분. R은 선택 규칙, A는 폴더 전체 검사로 고정. 검사 범위와 저장 완료·개수 표시. 문자 로고는 이미지/이모지 없이 상단에 유지.
- 핵심 88/88 통과. 선택 규칙 필터가 다른 규칙의 파일을 포함하지 않는 검사 추가.
- 실제 macOS PTY tests/tui_rule_smoke.py: 왼쪽 영역 N 규칙 추가, 13번째 새 행 선택·표시·개수, R로 TXT만 이동하고 PDF 보존, 왼쪽 영역 R도 개별 범위, A는 전체 범위, 추가 취소 후 설정·선택 보존, 로고 표시·정상 종료 통과.
- 기존 CLI/TUI 스모크도 통과: CLI 18명령·PDF 105개 이동·2페이지·선택 이동·설정 공유·중복 잠금·비정상 종료 후 재진입.
- 최종 Windows x64 self-contained 단일 EXE 게시 성공. 최종 소스/자산과 빌드 복사본 일치. 번들 내부 DLL·7종 브랜드/셸 자산·9종 ICO 포함 검증. EXE 77,176,187 bytes, SHA256 3e806d522d2d607fc98544b08dc50148ab86483970a2cb071c570db9b09b96ff.
- 미검증: 실제 Windows 콘솔 글꼴/화면/입력·GUI/트레이·WMI·휴지통. macOS PTY 결과를 Windows 실행 결과로 해석하지 않음. 기존 Windows 점검표에 회귀 항목 추가.
- 패키지는 캐시 소스로 복원했으며 최신 온라인 취약성 정보 갱신은 수행하지 않음.
- 실행 로그: /private/tmp/chagok-041-checks.log, chagok-041-tui.log, chagok-041-smoke.log, chagok-041-publish.log. HTML 구조·내부 링크·스크립트 구문 정적 확인, 브라우저 재렌더는 미검증.

# 0.4.0 검증 · 2026-10-19

- 실제 수행: 핵심 87/87 통과. GUI/탐색기/CLI 인수 분기와 빈/넓은/좁은 대시보드 표시 검사 포함.
- 실제 macOS 터미널 프로세스: CLI 18개 명령, PDF 105개 이동, 두 번째 페이지, 선택한 2개만 이동, 설정 공유, 중복 실행 거부·비정상 종료 후 잠금 해제 통과.
- 최종 Windows x64 self-contained 단일 EXE 게시 성공. 소스·자산과 내부 빌드 복사본 일치. PE 콘솔 서브시스템·x64, 9종 ICO 실제 EXE 포함, 단일 파일 번들의 Chagok.dll과 게시 DLL 바이트 일치 확인.
- 번들 안 새 PNG/ICO·3종 탐색기 로고·manifest·기존 native DLL 원문 일치. native DLL은 변경하지 않았음.
- EXE SHA256: f3ee3283089fac84a5a09f81ebed33647680b19e0040ed175522c0b1458a42c0 (77,175,670 bytes).
- HTML: 새 아이콘·0.4.0·tui 명령 반영. 구조/내부 링크/스크립트 문법 정적 검사. 이번 버전 브라우저 렌더는 미검증.
- 미검증: 실제 Windows 더블 클릭의 콘솔 일시 표시, GUI/트레이/탐색기/TUI의 아이콘과 화면, PowerShell/cmd 파이프·종료 대기/코드, WMI·휴지통·배율. Windows 테스트 PC에서 확인 필요.
- 캐시 패키지로 복원. 최신 온라인 취약성 정보는 갱신하지 않았으며 이를 취약성 검사 통과로 해석하지 않음.
- 실행 증거: /private/tmp/chagok-040-checks-final.log, chagok-040-smoke-final.log, chagok-040-publish.log, chagok-040-verify.py.

# 버전 0.3.0 CLI·TUI 검증 결과

검증일: 2026-10-19 (Asia/Seoul). macOS arm64 / .NET SDK 10.0.402.

## 구현과 운영 선택

GUI의 기존 JSON 버전 1·데이터 위치·설정 필드·기록을 터미널과 공유합니다. 기존 Windows Local mutex와 session.lock 파일 핸들을 함께 보유하며 설정 로드 전에 획득합니다. 전체 GUI/CLI/TUI/watch 세션을 독점하므로 동시에 둘을 열어 동기화하는 기능은 아닙니다. 기존 GUI도 같은 세션의 mutex 개체 존재를 확인합니다. 모든 새 클라이언트는 데이터 폴더 잠금도 확인합니다.

검사·디스크 계획·이동 코드를 재사용하며 새로운 영구 삭제 경로는 없습니다. Windows 모니터와 휴지통 구현은 같은 소스를 링크합니다. 터미널은 경로를 입력받고, GUI의 기본 폴더 선택 창은 유지합니다. GUI와 터미널 모두 차곡 데이터 저장소를 원본 검사 범위에서 보호합니다.

## 실제 수행한 검사

- 핵심·터미널 검사 **85/85 통과**. 기존 76개와 세션 중복/재획득, 폴더 중첩 거부, GUI 설정·ID 보존, 잘못된 규칙의 상태 보존, 인수 오류, 이름 중복, 선택 항목 이동·결과/기록 저장, 실행 전 기록 오류, 제어 문자·한글 표시 폭 검사를 포함합니다.
- 재실행 가능한 `tests/terminal_smoke.py`: **CLI 명령 18회**, PDF 105개 이동, 2페이지 5건, scan 무이동, 비대화형 run/remove 승인 요구, 설정 보존, 폴더·규칙 새 ID 복제와 제외 경로 재배치, 기록 확인.
- 실제 별도 TUI 프로세스에서 Enter/R로 규칙·미리보기 이동, Space로 하나 해제, I 전체 경로 상세, 실행 승인 후 나머지 두 파일만 이동한 것을 확인했습니다.
- TUI 프로세스가 보유 중이면 두 번째 CLI가 코드3으로 거부되고 설정 파일이 보존됨을 확인했습니다. 이 테스트 자식 프로세스를 강제 종료한 뒤 다른 CLI가 정상 진입했습니다. OS 핸들 해제 후 잠금 재사용을 확인한 것이며 실제 Windows named mutex 실행 검증은 아닙니다.
- macOS에서 replace 휴지통 미지원 시 코드4·실패 결과와 원본/기존 대상 내용 보존을 확인했습니다. Windows 휴지통 실행 검증과 구분합니다.
- 최종 소스의 테스트/터미널 Debug 빌드: 경고0·오류0. GUI와 터미널 Release win-x64 self-contained 단일 EXE 게시 성공. 네이티브 DLL 소스/바이너리는 변경하지 않았습니다.
- 내부 임시 복사본을 사용해 순차 MSBuild로 검증했습니다. 원본과 최종 빌드 복사본의 코드·프로젝트·화면·네이티브 자산 등 **55개 파일** 및 공통 빌드 설정의 해시 일치를 확인했습니다.
- GUI의 네이티브 DLL·manifest·라이선스·이미지 3개의 실제 리소스 내용/이름을 확인했습니다. 두 EXE의 PE x64·버전 0.3.0.0·SQLite native 번들 항목을 확인했습니다.
- HTML 설명서는 버전/날짜·13번째 터미널 장을 추가했습니다. 내부 앵커·중복 ID·13개 장·JavaScript 문법을 확인했습니다. 이번 변경에서 브라우저 렌더링을 다시 실행하지는 않았습니다.

## 배포 파일

- Chagok.exe SHA256: `102b9f02aa4d6b65b5f0a6b3f732928e6fd9ac71bf163652bf80ace8c8cf4483`
- chagok-cli.exe SHA256: `a438c3ff28d8d2bc4e56fbfe5ae63e3cfa9cedca92af6ea26a7865e20ebe4c0c`

ZIP CRC·파일별 SHA256SUMS 일치와 원본/게시 자산 보존을 확인했습니다. 이전 배포 파일은 보존합니다.

## 복원 환경과 미검증 범위

온라인 NuGet 복원은 자동 승인 검토 서비스의 모델 용량 오류로 실행되지 않았습니다. 최신 취약성 정보를 갱신하지 못했습니다. 취약성 검사를 끄는 속성을 추가하지 않고, 기존 로컬 NuGet 패키지 캐시를 명시적 소스로 사용해 오프라인 복원했습니다. 따라서 오프라인 빌드 성공을 최신 의존성 취약성 검사 통과라고 설명하지 않습니다.

기본 병렬 MSBuild 복원이 멈춘 사례가 있어 내부 디스크 복사본에서 `-m:1 -p:BuildInParallel=false --disable-build-servers`로 복원·빌드·게시했습니다. 앞서 멈춘 복원 두 개는 종료된 것을 확인했으며 최종 빌드와 혼용하지 않았습니다.

실제 Windows에서의 GUI↔터미널 전환, 기존 버전 mutex, 서로 다른 로그인 세션, Windows Terminal/PowerShell의 폼·키·색·크기 변경, WMI/대체 감지·watch·휴지통·탐색기 요청은 Windows 테스트 PC에서 재확인해야 합니다. 온라인 NuGet 정보 갱신도 후속 확인 항목입니다. GUI UI 실행이나 Windows 프로그램 이벤트를 macOS에서 통과했다고 주장하지 않습니다.

## 재현 명령

SDK와 패키지가 준비된 개발 환경에서 아래를 사용합니다. `--source`는 오프라인 캐시가 필요할 때만 별도로 지정하며 온라인 보안 정보 조회는 따로 확인합니다.

```sh
dotnet restore tests/FileOrganizer.Checks/FileOrganizer.Checks.csproj -m:1 -p:BuildInParallel=false --disable-build-servers
dotnet build tests/FileOrganizer.Checks/FileOrganizer.Checks.csproj --no-restore -m:1 -p:BuildInParallel=false --disable-build-servers
dotnet tests/FileOrganizer.Checks/bin/Debug/net10.0/FileOrganizer.Checks.dll
python3 tests/terminal_smoke.py src/Chagok.Terminal/bin/Debug/net10.0/chagok-cli.dll /path/to/dotnet
```

smoke 검사는 macOS/POSIX 개발 검증용이며 테스트 폴더와 로그 위치를 출력합니다. 실제 Windows 확인은 windows-test-guide.md를 따릅니다.

---

# 버전 0.2.2 검증 결과

검증일: 2026-10-16 (Asia/Seoul). macOS arm64, .NET SDK 10.0.402.

## 원인과 근거

- 사용자 화면의 `FileOrganizer.Core.FolderSpec`: QuickRuleStartDialog는 DisplayMemberPath=Name을 지정했으나 공통 ComboBox 선택 영역의 ItemTemplateSelector 전달이 없었습니다. [WPF 공식 템플릿 예제](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.combobox.selectionboxitemtemplate?view=windowsdesktop-10.0)의 연결을 복원했습니다. 아래 전체 경로는 의도된 보조 정보입니다.
- 사용자 10:07:01 기록: Add-AppxPackage가 0x80080204/0xC00CE169와 `Chagok.AddRule`의 영숫자 패턴 위반을 명시했습니다. manifest Verb Id를 `ChagokAddRule`로 변경했습니다. 기존 HKCU 메뉴 키 이름은 변경하지 않았습니다. PowerShell 예외 본문만 UTF-8로 전달해 진행 CLIXML이 오류를 가리지 않게 했습니다.
- 대체 감지 알고리즘: 경로 집합만 비교해 PID가 전부 바뀌는 연속 재실행을 누락했습니다. 새 회귀 검사 4개 중 3개가 기존 코드에서 실패(73/76)했고 수정 후 76/76 통과했습니다. 빈 표본 없는 교체, 5회 연속 재실행, 경로 미확인 실행본의 연속성, 다른 EXE와의 분리를 확인했습니다. 실제 PC 증상이 전부 이 원인인지는 아직 확인하지 못했습니다.
- 사용자는 ‘Claude 시작, 바탕화면 아이콘 선택’이라고 설명했습니다. 사용자가 등록 경로를 `C:\Users\ASUS\AppData\Local\AnthropicClaude\claude.exe`로 확인했습니다. 실제 프로세스 경로·잔류 프로세스·자동/확인 모드는 미확인입니다. 같은 파일명·다른 실제 경로는 구성당 최대 16개 경로를 한 번씩 진단 기록하며 파일명만으로 일치시키지 않습니다. 기존 첫 실행본/마지막 종료 의미는 바꾸지 않았습니다.

## 실제 검증과 한계

핵심 검사 **76/76 통과**. Windows 대상 Debug 빌드 **성공, 경고 0 / 오류 0**. 이후 버전 manifest와 안내 문구를 수정했으며 최종 소스는 Release 게시로 다시 컴파일했습니다. 250ms 타이머는 Windows에서 실제 측정한 반응 시간 보장이 아닙니다. 초기 스냅샷을 백그라운드 작업 예약 전에 확보하도록 소스로 확인했습니다.

XML 5개 파싱, 버전 0.2.2, Verb Id 영숫자 패턴, 기존 네이티브 DLL PE x64·SHA256 보존을 확인했습니다. 네이티브 코드는 변경·재컴파일하지 않았습니다. XML 파싱과 특정 Id 패턴 확인은 Windows Appx 전체 스키마 검사·배포 성공을 뜻하지 않습니다.

실제 WPF 이름 표시·테마·배율, Windows 11 메뉴 등록·COM 실행, WMI·프로세스 조회·250ms 감지 루프·Claude 재실행·트레이 대기 해제는 Windows 테스트 PC에서 확인해야 합니다. 파일 이동·안전 로직과 저장 설정 형식은 변경하지 않았습니다.

최종 Release win-x64 단일 EXE 게시 성공(종료 코드 0, 경고·오류 출력 없음). 게시 후 DLL·manifest·라이선스·PNG 3개의 실제 내용과 논리 리소스 이름 6개가 최종 managed assembly에 포함됨을 확인했습니다. 최종 EXE PE x64·버전 0.2.2.0·SQLite native 번들 항목을 확인했습니다. EXE SHA256: `cd369b7acd3d5a1ccb6f34032947b5ff1c098a479edd0fb313ac7fc398b5b2bc`. ZIP 내 모든 파일의 CRC와 SHA256SUMS 일치를 확인했습니다.

---

# 버전 0.2.1 검증 결과

검증일: 2026-10-16 (Asia/Seoul). 개발 환경: macOS arm64, .NET SDK 10.0.402.

## 사용자 보고와 확인한 원인

사용자가 2026-10-16 09:25:21 ‘프로그램 감지를 시작하지 못했습니다. 액세스가 거부되었습니다’ 기록을 제공했습니다. 기존 Configure는 WMI 이벤트 등록 과정에서 예외가 나면 모든 감지를 정지하고 대체 경로를 제공하지 않았습니다. 실제 PC의 WMI 접근 정책은 맥에서 조회하지 못했습니다. WMI 등록 거부로 추정하며 새 로그에 등록 단계·HRESULT와 대체 전환을 기록합니다.

## 실제 수행한 검증

핵심 검사 **72/72 통과**. 기존 64개와 다음 8개를 포함합니다.

- 대체 감지 초기 실행 상태에서 거짓 시작을 만들지 않음.
- 첫 실행본 시작·마지막 실행본 종료만 알림.
- 경로 미확인을 종료로 오인하지 않음.
- 불완전한 프로세스 목록을 이전 상태로 유지.
- 같은 이름의 다른 경로 EXE 제외.
- 초기 경로 미확인이 나중에 해소되어도 거짓 시작을 만들지 않음.
- 사라진 미확인 PID 재사용 시 실제 새 실행 감지.
- 자동 메뉴 등록 기본값·이전 설정·해제 상태 보존.

Windows 대상 Debug 빌드: **성공, 경고 0 / 오류 0**. 최종 소스는 Release 게시로 다시 컴파일했습니다. win-x64 self-contained 단일 EXE 게시 성공. 실제 Windows 프로세스 API·WMI 권한 거부·주기 확인 루프는 이 빌드 검증으로 실행 통과했다고 간주하지 않습니다.

네이티브 첫 메뉴 DLL은 Zig 0.13.0으로 x86_64-windows-gnu 교차 빌드했습니다. 최종 `-Wall -Wextra -Werror` 빌드 통과. PE x64, DllGetClassObject·DllCanUnloadNow 두 export, manifest CLSID 일치, 한글 제목, 50/150/44 PNG 자산 크기를 정적으로 확인했습니다. import는 Windows 시스템 DLL과 Windows 11에 포함된 UCRT API 집합이며 별도 C++ 런타임 DLL을 배포하지 않습니다. Native DLL SHA256: `05a4541012ac5bbee98645c2958a79f4d1afb74033352ec4d1ca381e978e5e58`.

빌드 도구 wheel은 ziglang 0.13.0 macOS arm64이며 SHA256은 `5408ad3a6cb342fb4e4e2351a3725adfcae82eaadebe85fdbbdad9c0ac8b1e24`입니다. 초기 C++ 빌드의 export 재선언과 불필요한 C++ 런타임 경고를 정리했고 최종 DLL은 .def export와 Windows 힙 할당을 사용합니다. 도구 자체는 배포하지 않습니다. MinGW-w64 고지는 EXE의 확장 리소스에 포함합니다.

직접 출력한 Native 자산이 검증 후 0으로 변한 현상을 발견했습니다. 당시 관리 어셈블리에는 정상 DLL이 포함되어 있었지만 원본 자산과 달라 배포를 보류했습니다. 정확한 원인은 확인하지 못했습니다. 링커 출력은 임시 내부 디스크에서 완료하고 PE 검사·일반 쓰기·fsync·해시 검증으로 복사하도록 빌드 스크립트를 바꿨습니다. 그 뒤 최종 EXE를 다시 게시했고 Native 자산이 정상 PE로 유지되며 실제 임베딩과 일치함을 확인했습니다.

XAML·프로젝트·앱 manifest·Appx manifest XML과 리소스 경로, 자동 확인·등록 해제 시 자동 설정 끄기·기존 메뉴 유지·개발자 모드 조건·PowerShell 인수 전달·시간 제한·캐시 제외를 정적으로 확인했습니다. 최종 관리 어셈블리에서 Native DLL·manifest·로고·고지의 논리 이름과 6개 payload 내용 일치를 확인했습니다. 최종 ZIP CRC와 포함 파일 SHA256을 패키징 단계에서 검사합니다.

## Windows에서 확인할 항목

**실제 WMI 등록 거부 → 대체 전환, QueryFullProcessImageName 권한, 프로그램 실행·종료, 중지·재설정·종료 시 루프 해제, Windows 11 개발자 패키지 등록·COM 활성화·첫 메뉴 표시·실제 호출·제거는 미검증**입니다. 개발자 모드와 인증서 신뢰는 변경하지 않았습니다. 테스트용 loose package 등록 코드이며 일반 배포용 서명 MSIX는 제공하지 않습니다. 설정 UI·스크롤·테마·키보드·배율도 Windows 테스트가 필요합니다.

대체 감지는 1초 간격이므로 짧은 실행·표본 사이 재시작을 놓칠 수 있습니다. 보호되거나 접근을 거부하는 프로그램은 여전히 경로를 확인하지 못할 수 있습니다. 이름만으로 대상을 추정하거나 접근 실패를 종료로 처리하지 않습니다. 이 한계는 ‘권한 문제를 무조건 해결했다’고 설명하지 않습니다.

## 참고한 공식 문서

- [WMI 네임스페이스와 UAC 접근](https://learn.microsoft.com/en-us/windows/win32/wmisdk/access-to-wmi-namespaces)
- [QueryFullProcessImageName의 제한된 조회 권한](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)
- [Windows 11 IExplorerCommand·패키지 등록](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)
- [개발용 loose 등록과 Developer Mode](https://github.com/microsoft/winappCli/blob/main/docs/security.md)
- [배포 패키지의 서명·신뢰 요구](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)

## 이전 버전 검증 이력

## 0.2.0 검증 기록

검증일: 2026-10-16 (Asia/Seoul). 개발 환경: macOS arm64, .NET SDK 10.0.402.

## 실제 수행한 검증

`tests/FileOrganizer.Checks/FileOrganizer.Checks.csproj`: **최종 핵심 코드에서 64/64 통과**. 기존 54개와 새 기능 10개를 포함합니다. 파일 이동·교체·취소·원본 보존, 디스크 결과·페이지 선택, 설정 보존, 진행·결과 상태 검사를 함께 실행했습니다.

- 복제본의 폴더·규칙 새 ID, 깊은 복사, 원본 보존과 폴더 조건·목적지·규칙 속성 유지.
- 제외 경로의 새 검사 위치 재배치와 범위 밖·검사 폴더 자체 제외 거부.
- 폴더 조건 저장·재로드·이전 JSON 기본값과 잘못된 enum·null 거부.
- 폴더 조건의 켜진 규칙 전체 포함, 수동·다른 프로그램 규칙 포함과 비활성 제외, 규칙 자체 조건의 독립 동작.
- 같은 이벤트의 폴더·규칙 중복 제거와 확인 우선. 폴더 전체를 한 계획으로 검사해 선행 규칙의 건너뛰기와 첫 일치 순서를 유지. 폴더 이벤트가 아닐 때 확인·자동 규칙 그룹도 서로 겹치지 않음.
- 감지 경로 중복 제거와 모든 규칙이 꺼진 폴더의 감지 제외.
- 탐색기 실행 인수의 형식·전체 경로 검사, 공백·한글·&가 있는 EXE 경로 따옴표, 잘못된 실행 인수·명령 경로 거부.
- 한글·공백·& 목적지 전달 형식 왕복과 과대·음수·잘린 요청 거부. 이 검사는 메모리 스트림이며 실제 Windows 프로세스 간 전달은 아닙니다.

Windows 대상 Debug 빌드: **성공, 경고 0 / 오류 0**. 이후 폴더 실행의 순서 보완을 추가하고 핵심 64개를 다시 실행했습니다. 최종 소스는 Release 게시로 다시 컴파일했습니다. 탐색기의 긴 경로·지원하지 않는 경로 오류 처리도 마지막 게시에 포함했습니다. 초기 빌드의 비동기 Dispatcher 호출 경고는 의도적인 예약 호출로 명시해 수정했습니다.

최종 Release 게시: **성공**, win-x64 self-contained 단일 EXE, 네이티브 라이브러리 포함. PE x64·버전·최종 소스 시각·SQLite 의존성·ZIP CRC·SHA256은 배포 포장 단계에서 확인합니다.

XML 4개 파싱과 복제 버튼·폴더 조건·시작 인수·기존 앱 전달·대기 큐·기본 .exe 선택 연결을 정적으로 확인했습니다. 기존 파생 창 테마·단일 실행 잠금·설정 저장 위치·파일 이동 안전 코드는 유지했습니다.

## 실제 Windows에서 확인해야 할 항목

복제 창·폴더 실행 조건·기본 .exe 선택·테마·배율·키보드, 실제 WMI 시작·종료 이벤트, 레지스트리 메뉴 등록·해제·경로 갱신, 미실행/트레이/작업/편집 중 탐색기 요청 전달, 실제 사용자·세션 제한과 연결 시간 제한, UI 대기 20개 한도는 **미검증**입니다. macOS에서는 WPF와 탐색기·Windows 레지스트리를 실행 검증할 수 없습니다. `windows-test-guide.md`의 0.2.0 절차를 수행해야 합니다.

## 구현 근거

탐색기 명령 인수의 따옴표와 Shell verb 등록은 [Microsoft Shell verbs 문서](https://learn.microsoft.com/en-us/windows/win32/shell/fa-verbs)를 참고했습니다. Windows 11의 새 메뉴와 기존 메뉴 구분은 [Microsoft 탐색기 연동 문서](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)에 따릅니다. 같은 사용자로 제한하는 파이프 옵션은 [Microsoft PipeOptions 문서](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions)를 참고했습니다. 문서 확인은 실제 Windows 동작 검증을 대신하지 않습니다.



## 0.1.12 설정 아이콘·앱 정보

- 설정·폴더 설정을 벡터 아이콘으로 변경했습니다. 툴팁과 접근 가능한 이름을 유지합니다. 헤더 버전을 설명으로 바꾸고 실제 버전·데이터 폴더 열기를 설정의 앱 정보로 옮겼습니다.
- XML 4개 파싱, 버튼 아이콘·툴팁, 메인 버전·데이터 버튼 제거, 어셈블리 버전 읽기와 데이터 폴더 열기 연결을 정적으로 확인했습니다. 데이터 폴더 경로와 실패 처리·설정 저장 분리는 소스로 확인했습니다.
- 실제 Windows에서 아이콘 표시·키보드·포커스·버전 표시·데이터 폴더 열기·오류 창·배율은 미검증입니다. 테스트 가이드에 절차를 추가했습니다.

## 0.1.11 가로 테마 아이콘 선택

- 가로 아이콘 3개를 담은 Popup으로 변경했습니다. 툴팁·접근 가능한 이름과 라디오 선택 상태를 제공하며 현재 선택을 배경·테두리로 표시합니다.
- XML 4개 파싱, 아이콘 순서·가로 배치·툴팁·작업 중 닫기를 정적으로 확인했습니다. 선택·설정 동기화·저장 실패 복원·키보드 연결은 소스로 확인했습니다.
- 첫 빌드에서 Popup.Opened 이벤트 형식 불일치가 발생해 EventArgs로 수정했습니다. 최종 소스로 다시 게시했습니다.
- 실제 Windows 팝업 배치·테마·툴팁·키보드·바깥 클릭·배율은 미검증입니다. Windows 테스트 가이드에 절차를 추가했습니다.

## 0.1.10 메인 테마 아이콘

- 콤보박스 대신 시스템·라이트·다크 상태를 나타내는 벡터 아이콘 버튼과 기본 메뉴를 연결했습니다. 설정 저장 성공·실패와 설정 창 저장 후 아이콘·선택 체크·툴팁·접근 가능한 이름을 갱신합니다.
- 작업 중 버튼 비활성·메뉴 닫기, 메뉴 열 때 현재 항목 포커스, 기존 검사 결과 보존 경로를 소스로 확인했습니다. XAML 파싱과 기존 콤보박스 참조 제거를 정적으로 확인했습니다.
- 실제 Windows에서의 메뉴 배치·체크·테마·키보드·배율·보조 기술은 미검증입니다. Windows 테스트 가이드의 0.1.10 절차를 추가했습니다.

## 0.1.9 앱 설정·종료·규칙 카드

- 설정에 창 닫기 동작과 종료 확인을 추가했습니다. 이전 JSON과 새 설정의 기본값, 저장·재로드·확인 복원, 기존 규칙 보존, 잘못된 enum 값 거부를 핵심 테스트에서 확인했습니다.
- WPF 설정 창·종료 확인창과 메인 창 연결, 확인 취소 시 저장하지 않는 분기, 저장 오류 시 종료 중단, 닫기 이벤트 밖에서 실제 종료 요청, 작업 중 취소 대기와 새 감지 차단을 소스로 확인했습니다. 이 항목은 실제 Windows 종료 동작 검증이 아닙니다.
- 규칙 카드의 상태 스위치, 아래쪽 편집·검사 버튼, 테마 리소스, 키보드 포커스 복원과 토글 상태별 템플릿, XML 4개를 정적으로 확인했습니다.
- 설정 창·종료 확인창의 실제 표시와 X·Escape·다시 묻지 않기·재실행·작업 중 종료, 스위치의 키보드 및 보조 기술 동작, Windows 배율별 화면은 미검증입니다. `windows-test-guide.md`의 0.1.9 절차를 테스트 PC에서 수행해야 합니다.

## 0.1.8 진행 표시·실제 결과·프로젝트 기준

- 실행 진행은 처리한 파일 수, 현재 파일 복사는 실제 바이트를 표시합니다. 복사·복사 검증·게시·원본 삭제 전 재검증 단계를 구분합니다.
- 복사 알림을 약 100ms 단위로 제한하고 완료 후 또는 다른 파일의 지연 알림은 UI에서 무시합니다. 실행 타이머는 종료와 계획 폐기 시 중단합니다.
- 결과는 카운터로 집계하고 파일별 상세는 기존 임시 DB에 저장해 페이지별로 읽습니다. 정상·주의·취소·오류, 취소와 미실행을 구분합니다. 기록 오류가 나면 파일 처리가 끝났어도 오류 결과로 표시합니다.
- 실제 복사에서 바이트 알림·단계 순서와 최종 이동, 알림 중 취소 시 원본 보존을 테스트했습니다. 결과별 집계·오류·늦은 취소·과다 집계 거부, 페이지 이동 후 파일별 결과 유지·중복 저장 거부도 확인했습니다. 저장 오류 뒤 미기록 행은 미실행으로 단정하지 않고 미확인으로 표시하며, 이미 확인한 결과는 유지하는지 순수 표시 상태 코드로 검사했습니다.
- 최종 UI의 XML·이벤트·진행·지연 알림 차단·타이머 중단·동작 감소 분기를 정적으로 확인했습니다. 기본 의미 색 6조합의 대비를 라이트·다크 카드 배경에서 계산했고 최솟값은 6.64:1입니다. 실제 WPF 렌더링·애니메이션·보조 기술 알림·배율은 이 계산과 정적 검사로 통과했다고 간주하지 않습니다.
- `AGENTS.md`는 프로젝트 작업 규칙, `ui-spec.md`는 화면 상태·테마·입력·결과 기준, `windows-test-guide.md`는 실제 재확인 절차를 담당합니다. 사용자 공통 지침은 변경하지 않았습니다.

## 0.1.7 디스크 검사 결과와 페이지 표시

- 실제 앱은 `Planner.ScanToDisk`를 사용합니다. 기존 `Scan` 메모리 방식은 호환 API와 비교 테스트 용도로 남겼으며 앱에서 호출하지 않습니다.
- 검사 결과·경고·충돌 예약·폴더별 경로 정렬을 임시 SQLite 파일에서 처리합니다. 경로·경고 개수에 비례하는 전체 메모리 컬렉션을 사용하지 않습니다. SQLite 쓰기는 최대 500회 단위로 확정합니다.
- 미리보기 100건, 실행 대상 100건씩 읽기를 적용했습니다. 실행 통계는 카운터로 누적하며 결과 전체 목록을 생성하지 않습니다.
- 페이지 간 선택 유지, 페이지 밖 전체 선택·해제, 건너뛰기·보류 선택 금지, 배치 전체 선택 개수·교체 개수를 테스트했습니다.
- 실제 파일을 사용해 기존 검사와 파일·대상·순서·규칙·스탬프를 비교했고, 페이지 밖 선택 파일 이동과 해제 파일 보존을 확인했습니다.
- 디스크에 저장된 교체 계획도 원본 변경 시 보류하고 기존 대상 파일을 보존하는지 확인했습니다.
- 취소 후 캐시 정리, 사용 중 캐시 보존, 잔여 캐시 정리, 검사 안의 임시 저장소 제외, 저장소 자체 검사 거부와 잘못된 저장 위치의 원본 보존을 확인했습니다.
- 긴 경로 결과 10,001건을 저장하고 가비지 수집 후 관리 메모리 증가가 8MiB 미만인지 검사했습니다. 이 수치는 WPF 앱 전체 작업 집합·네이티브 메모리·메모리 최고 사용량이 아니며 앱 메모리 상한을 보장하지 않습니다.
- 메모리 상한 설정은 추가하지 않았습니다. 임시 파일 크기와 디스크 작업량이 증가합니다. 폴더 깊이·규칙 수·경로 길이도 메모리에 영향을 줄 수 있습니다.
- Microsoft.Data.Sqlite 10.0.13과 함께 제공되는 SQLite 네이티브 라이브러리를 사용합니다. 패키지 복원 시 NuGet 취약성 검사를 유지했습니다.
- SQLite의 트랜잭션과 경로 비교용 사용자 정의 collation은 [Microsoft 트랜잭션 문서](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [collation 문서](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/collation)를 참고했습니다.
- 실제 Windows에서 페이지 이동·선택·프로그램 감지·취소·네이티브 라이브러리 로딩·임시 저장소 공간 부족 동작과 앱 전체 메모리는 아직 확인하지 못했습니다.

## 0.1.6 폴더 목록 비활성 배경 수정

- 검사와 이동의 SetBusy에서 FolderList.IsEnabled를 끄는 경로를 확인했습니다. 기존 목록은 기본 테마의 템플릿을 사용하고 있었습니다.
- 목록 템플릿의 배경을 앱의 Background에 연결하고, 비활성 상태에서는 배경색 변경 대신 내용 투명도만 낮춥니다. 선택과 잠금은 유지합니다.
- XML 파싱과 비활성 템플릿의 배경·투명도, 스크롤 설정을 정적으로 확인했습니다. 실제 Windows 다크 모드에서의 수정 결과는 미검증입니다.
- 메모리 사용량은 이 맥에서 WPF 앱을 실행할 수 없어 측정하지 못했습니다. 소스에서는 한 파일씩 128KiB 복사 버퍼를 사용하며 파일 전체를 메모리에 올리지 않습니다. 앱 전체 사용량은 이 버퍼 크기와 다릅니다. 검사 계획·경로·경고·미리보기 행은 메모리에 남으므로 항목 수에 따라 사용량이 증가합니다.

## 0.1.5 영문 파일 이름 변경

- 실행 어셈블리와 파일 이름을 `Chagok` 및 `Chagok.exe`로 변경했습니다. 배포 ZIP은 `Chagok-v0.1.5-win-x64.zip`입니다.
- 화면의 ‘차곡’ 이름과 로고, 설정·기록 경로와 단일 실행 잠금은 유지합니다.
- Windows 대상 최종 게시와 버전·파일 이름·아이콘 리소스·ZIP CRC·SHA256 검증을 통과했습니다. Windows 실제 실행은 이 맥에서 검증할 수 없습니다.

## 0.1.4 이름 변경과 검증 범위

- 실행 어셈블리 이름을 ‘차곡’으로 변경하고 `차곡.exe`를 생성합니다. 배포 ZIP은 `차곡-v0.1.4-win-x64.zip`입니다.
- 제품·파일 속성 제목을 ‘차곡’으로 지정했습니다. 소스 네임스페이스와 설정·기록 경로, 단일 실행 잠금 이름은 유지합니다.
- 이름 변경 후 Windows 대상 빌드와 최종 게시, 생성된 실행 파일 이름·PE 형식·아이콘 리소스·패키지 CRC와 SHA256을 확인했습니다. Windows에서 한글 파일 이름으로 실제 실행하는 검증은 이 맥에서 수행할 수 없습니다.

## 0.1.3 수정과 검증 범위

- 승인 시안 원본을 `assets/branding/chagok-approved-board.png`로 복사하고 원본과 SHA256이 같은지 확인했습니다.
- 앱용 PNG는 승인 시안의 왼쪽 아래 아이콘을 기준으로 이미지 도구로 분리·정리한 파생 자산입니다. 원본 시안 자체는 변경하지 않았습니다.
- 16·20·24·32·40·48·64·128·256픽셀 PNG 프레임으로 ICO를 구성하고 헤더·개수·프레임 크기·범위를 검사했습니다. 앱용 PNG의 알파 채널도 확인했습니다.
- 메인 화면, 창 아이콘, 실행 파일 아이콘과 트레이에 자산을 연결했습니다. 앱 표시 이름은 ‘차곡’, 제작자 표기는 ‘by ksyrang’입니다. 트레이 아이콘은 종료 시 함께 해제합니다.
- 실행 파일 선택을 Windows 기본 OpenFileDialog로 변경하고 `.exe` 필터와 취소 시 입력 보존을 정적으로 확인했습니다. 사용하지 않는 자체 선택 창을 제거했습니다.
- XML 4개 파싱 및 리소스 경로·기본 선택 창 연결을 정적으로 확인했습니다. Windows 탐색기·작업 표시줄·트레이에서 실제 아이콘이 표시되는지는 이 맥에서 실행 확인하지 못했습니다.
- 기존 설정 경로, 단일 실행 잠금 이름, 실행 파일 이름과 핵심 파일 처리 코드는 유지했습니다.

## 0.1.2 수정과 검증 범위

- 사용자 캡처에서 모든 콤보 박스의 화살표·작은 테두리가 왼쪽 글자와 겹치는 현상, 제외 경로 추가 버튼이 입력칸보다 높은 현상을 확인했습니다.
- 콤보 박스의 내부 버튼에 기본 테마 스타일을 적용하지 않고 가로·세로 채우기를 명시했습니다. 글자 대신 경로 도형으로 오른쪽 화살표를 그립니다. 선택 글자색과 팝업 배치 기준도 명시했습니다.
- 제외 경로 입력칸의 위쪽 여백을 입력 행 전체로 이동했습니다.
- 사용자 요청에 따라 검사·이동·제외 폴더 선택을 Windows 기본 창으로 복원했습니다. 선택 취소 시 기존 경로를 유지하고, 제외 범위 검증은 유지합니다.
- XML 4개 파싱과 템플릿의 전체 너비·화살표 위치, 입력 행 여백, 기본 폴더 선택 연결 3곳을 정적으로 확인했습니다. 실제 Windows 렌더링·키보드 동작 검증은 아닙니다.

## 제공 영상과 사용자 보고

- 사용자가 0.1.0의 프로그램 실행과 핵심 파일 이동이 되는 것 같다고 보고했습니다. 이는 사용자 확인이며 이 맥에서 실행한 윈도우 테스트가 아닙니다.
- 제공한 약 49초 녹화본에서 다크·시스템 모드의 본문과 상태 표시줄이 흰색으로 남고 밝은 글자가 겹치는 현상을 확인했습니다.
- 0.1.1에서 파생 창에 배경 스타일을 명시하고, 버튼 글자색 상속과 기본 컨트롤 테마를 수정했습니다. 폴더·프로그램 선택은 앱 테마를 사용하는 창으로 변경했습니다.
- 이후 사용자 캡처에서는 다크 본문의 배경과 글자가 보이며, 콤보 박스와 제외 경로 행의 배치 문제가 확인되었습니다. 0.1.2와 0.1.3의 실제 Windows 화면은 아직 실행 확인하지 못했습니다.

## 아직 수행하지 못한 검증

- 실제 윈도우에서 WPF 화면을 열고 폴더 선택·키보드·배율·테마 전환 확인.
- 윈도우 트레이와 알림, 프로세스 시작·종료 WMI 이벤트와 권한 차이.
- 실제 윈도우 휴지통, 휴지통 미지원 드라이브의 교체 중단.
- 윈도우 파일 잠금과 NTFS·외장 드라이브 간 이동, 드라이브 분리·공간 부족.
- 장시간 실행, 대량 파일 성능, ARM 윈도우 실행.

이 항목은 맥에서 통과했다고 간주하지 않습니다. `windows-test-guide.md`로 테스트 PC에서 확인해야 합니다.

## 테스트 패키지

패키지는 서명하지 않은 테스트 빌드입니다. IDE·SDK 없이 실행하도록 .NET 실행 환경을 포함했습니다. 최종 ZIP의 CRC와 포함 파일은 패키징 단계에서 검사하며, 실행 파일 해시는 ZIP 안의 `SHA256SUMS.txt`에 포함합니다.

## 0.6.3 · 입력 방법 탭 표시 정리 (2026-10-20)

- Windows x64 단일 파일 자체 포함 게시 성공. 경고·오류 없음. 소스와 내부 빌드 복사본 일치, 실제 번들 DLL·아이콘 9개 크기·브랜드/탐색기 자산 7개 임베딩 확인.
- 핵심 검사 133/133 통과. CLI 도움말 19개와 문서 스키마 일치, XAML 파일 3개와 코드 내 탭 템플릿 2개 XML 파싱 성공, HTML 내부 앵커와 git diff --check 통과.
- 예시/직접 연습 템플릿의 크기 변경 트리거 제거, 별도 헤더 행·닫힌 테두리·일정한 두께 적용. 분석/연습 로직은 변경하지 않음.
- macOS에서 실제 WPF를 실행하지 못했으므로 Windows의 탭 잘림 개선·테마·배율·키보드 표시는 미검증. Windows 회귀 체크리스트에 추가.
- 분류 기준 통합은 대화용 시안만 제공. 앱의 5개 선택 항목과 집계 의미는 유지.

## 0.6.4 · 분류 조건 통합 (2026-10-20)

- 새 통합 요구를 검증하는 테스트가 구버전에서 2건 실패하는 것을 확인한 뒤 구현. 최종 핵심 검사 138/138 통과: 세 종류 괄호, 제한 없음/조건 지정, 괄호 포함 여부, 기존 JSON 의미, 공백·대소문자, 미리보기·디스크 분석·파생 규칙·단일 검사 일치와 원본 보존.
- 실제 POSIX PTY 조건 스모크 통과: 120/60열 폼, 항목 설명, 제한 없음/조건 지정 왕복·조건 값 보존, 괄호 포함 전환, 미리보기, 조건 추가/삭제·오류·AND/OR·취소, native/plain 그룹 규칙 저장·재조회·CLI 단독 검사, 직접 식 변경 시 메타데이터 해제, 원본 무이동·캐시 정리. 증거: chagok-conditions-zsnom4ds.
- CLI/실제 PTY 분석 스모크 통과: 집계·페이지·재귀·제외·세 괄호·직접 정규식·새 G 조건 폼·규칙 초안과 명시 실행, 캐시 정리. 증거: chagok-analysis-vyalku3m.
- 별도 코드 리뷰에서 GUI Pattern.Trim으로 공백 그룹 범위가 달라질 수 있음을 확인하고 Group 저장 시 원문 보존으로 수정. 핵심 테스트로 공백 그룹 규칙 의미를 검증. 실제 WPF 저장 동작은 Windows 재확인 필요.
- Windows x64 자체 포함 단일 EXE 게시 성공, 경고/오류 없음. 최종 소스·빌드 복사본 일치, 실제 번들 DLL 및 브랜드/탐색기 자산 7개·아이콘 9개 크기 확인. CLI 도움말 19개/JSON 문서 스키마·XAML 3개·HTML 앵커·git diff --check 통과.
- 실제 Windows GUI의 새 조건 패널·스크롤·탭·테마·배율·포커스는 macOS에서 실행하지 못했다. Windows 체크리스트에 추가. GitHub 커밋/태그/릴리스는 생성하지 않음.

## 0.6.5 · 분석 조건 영역이 결과 표를 누르는 문제 (2026-10-20)

- 사용자 Windows 캡처에서 조건 패널 아래 표가 헤더 높이 정도로 줄어드는 현상을 확인. 기존 Auto 조건 행의 최대 330 DIP 스크롤이 가용 높이를 차지하고 결과 행이 남은 높이만 받는 배치를 조사했다.
- 조건 행을 화면 가용 높이에 따른 별표 행(최대 280 DIP)으로 변경하고 결과 행은 최소 180 DIP·2배 비율로 배분. 조건 선택/직접 입력 공용 스크롤을 사용하고 조건 내부 ScrollViewer는 ContentControl로 교체했다. 작은 화면은 최소 560 DIP 내용과 뷰포트 높이 바인딩·외부 스크롤로 접근한다.
- 영향 범위는 파일명 분석 화면의 배치·스크롤·창 크기와 모드 전환. 분석 엔진·설정 모델·TUI·파일 이동 코드는 변경하지 않았으며 관련 없는 전체 기능·핵심 로직 검사를 반복하지 않았다.
- 분석 XAML 파싱과 행/컨테이너 구조 확인, 버전 0.6.5·도움말 문서의 버전 외 내용 보존, git diff --check 통과. Windows x64 단일 EXE 자체 포함 게시 성공(경고/오류 없음), 최종 소스·빌드 복사본과 번들/자산 일치 확인.
- 실제 WPF 레이아웃·스크롤·최소 창/배율·포커스 표시와 표 페이지/규칙 만들기 접근은 macOS에서 실행하지 못했다. Windows 체크리스트에 재현 조건과 함께 추가했다. GitHub 게시 없음.

## 0.6.6 · 분석 설정/결과 아코디언 (2026-10-20)

- 0.6.5의 동시 분할 배치를 사용자가 불편하다고 평가해 설정과 결과 중 한 내용만 펼치는 방식으로 교체. 화면 진입은 설정, 분석 시작/성공은 설정 요약·진행/결과, 실패·취소는 설정으로 전환한다. 설정 왕복과 같은 결과의 폴더 전환은 입력/이전 결과를 보존한다.
- 영향 범위: 분석 화면 상태·설정 검증·진행·결과 표시·포커스. 분석 엔진/TUI/파일 이동과 나머지 전체 기능 검사는 반복하지 않았다.
- 연결된 실제 UI 상태 클래스의 관련 검사만 `--filter AnalysisAccordion`으로 실행해 6/6 통과: 최초 진입, 진행 중 편집 방지, 성공 후 결과 보존, 오류/취소, 변경 표시·복원, 실제 분석 기준의 비교. 내용 제한 없음의 숨긴 초안과 동일 생성식의 메타데이터 차이를 변경으로 오인하지 않음을 포함한다.
- 같은 식을 직접 입력으로 전환한 경우 메타데이터 차이를 조건 변경으로 오인하는 테스트가 5/6으로 실패함을 확인한 뒤 비교 범위를 수정하고 6/6 재확인. 검사 필터의 빈 선택·잘못된 인수가 종료 코드 2를 반환하는 것도 확인했다.
- 별도 범위 리뷰에서 사전 검증 오류 후에도 결과가 펼쳐져 있을 수 있는 경로를 확인해 ShowSettings를 적용. 기존 결과/입력을 유지하며 수정할 설정을 펼친다. XAML 파싱·Windows x64 단일 EXE 자체 포함 게시, 소스/번들/자산 일치, 도움말 스키마의 버전 외 보존·git diff --check 확인.
- 실제 Windows WPF의 배율/테마·접기/펼치기·그룹/파일 표 스크롤·페이지·규칙 만들기·키보드 포커스는 macOS에서 실행하지 못했다. Windows 체크리스트에 추가했다. GitHub 게시 없음.

관련 검사 실행 예:

```text
dotnet build tests/FileOrganizer.Checks/FileOrganizer.Checks.csproj -m:1 -p:BuildInParallel=false --disable-build-servers
dotnet tests/FileOrganizer.Checks/bin/Debug/net10.0/FileOrganizer.Checks.dll --filter AnalysisAccordion
```

## 0.7.0 · 기준 폴더 정리 GUI (2026-10-20)

- 변경 범위: 기준 폴더/검색어/별칭 설정, 목적지 후보 비교와 결정, 미분류 파일 그룹, 전용 GUI 설정/결과 아코디언, 기존 미리보기·이동 실행 연결. 파일별 계획 생성을 Planner 공통 함수로 분리하고 기존 실행기·설정 스키마·첫 일치 규칙의 동작은 유지했다. 아코디언 템플릿은 기존 내용을 그대로 App 리소스로 옮겨 두 화면에서 공유한다.
- 새 ReferenceOrganization 검사 10/10 통과: 폴더명·별칭·괄호/대소문자 기준, 동일 목적지 중복 집계 방지, 복수 후보 보류와 사용자 선택 반영, 새 그룹/중첩 그룹/폴더명 검증, 205개 파일·105개 그룹 페이지와 전체 그룹 적용, 재귀/제외/기준 폴더 중첩/링크 제외, 분석 이후 원본 변경·삭제 보류, 실제 이동·이름 충돌·새 폴더 생성·예약 경로 충돌·실행 전 변경 방어, 저장소 손상 시 중단, 정규식·취소·캐시 정리.
- 기존 관련 검사 32/32 통과: 일반 규칙 첫 일치와 규칙별 검사, 미리보기/하위 폴더/제외, 이름 충돌·건너뛰기·교체·휴지통 실패·원본 변경·취소, 디스크 계획/페이지 선택/결과 저장, 기존 AnalysisAccordion 6개, 버전이 바뀐 CLI 도움말. 새 검사와 합계 42/42. 원본 이동·보존은 macOS 테스트 임시 파일로 실행했으며 Windows UI/휴지통 실동작 검증은 아니다. 무관한 프로그램 감지·탐색기 등록·TUI 조작 전체 검사는 반복하지 않았다.
- 초기 테스트 임시 경로가 macOS의 /var 링크를 포함해 차단된 것을 확인하고 기존 검사와 같은 /private/tmp 경로로 수정했다. 포함 검색의 대소문자 무시 동작에 맞춰 테스트 기대값을 수정했다. 제품 안전 기준을 완화하지 않았다.
- 후속 변경: 폴더 새로 가져오기에서 같은 목적지의 검색어 초안을 보존하고, 기존/새 목적지 선택을 라디오로 구분했다. DB 폐기 오류 처리 변경 뒤 저장소 오류/정규식·취소·캐시 정리의 관련 검사 2개를 재실행해 모두 통과했다. 최종 핵심 검사 프로젝트 빌드는 경고/오류 0.
- NuGet 복원: 불완전한 이전 캐시 대신 기존 정상 캐시 /private/tmp/chagok-nuget-20261020을 사용했다. 제한 환경에서 실패한 취약성 정보 조회는 승인된 네트워크 접근으로 복원에 성공했다. 프로젝트에서 취약성 검사나 경고를 끄지 않았다.
- Windows x64 자체 포함·압축 단일 EXE 게시 성공. 최종 소스 수정 시각이 게시물보다 앞서는지 확인했다. PE x64/콘솔 진입점, EXE 안의 앱·Core 어셈블리가 Release 출력과 동일함, e_sqlite3.dll 포함, 기존 브랜드/탐색기 자산 7개 일치와 버전 0.7.0 포함을 확인했다. 기존 네이티브 DLL은 수정하지 않았다.
- App/기존 분석/새 창 XAML 파싱 통과. CLI help --json과 docs/cli-reference.json의 전체 내용이 일치한다. 기존 설정 파일과 공개 0.6.6 릴리스는 변경하지 않았다.
- 미검증: 실제 Windows WPF의 새 창·작은 작업 영역/100·150·200% 배율·라이트/다크/시스템·키보드/보조 기술·취소 중 닫기·감지 요청 대기. docs/windows-test-guide.md의 0.7.0 항목으로 확인해야 한다. 영구 규칙/검색어 저장과 새 TUI/CLI 진입점은 이번 범위에 포함하지 않았다.

검사 로그: /private/tmp/chagok070-related-checks.log. 게시물 검증: /private/tmp/chagok070-artifact-verification.json. EXE SHA256: af9bcff429749c2facc5d477cca2a36d7e3f91d2f95466dcd041128e7d1b6db8.

- 패키지 생성 과정에서 외장 드라이브가 자동 생성한 macOS AppleDouble 파일을 검출했다. ZIP과 SHA256 목록에서 모든 `._*`·`.DS_Store` 항목을 제외하도록 수정했다.

## 0.7.1 · 분석 화면 다크 테마 글자색 수정 (2026-10-20)

- 사용자 Windows 스크린샷에서 기준 폴더 창과 기존 분석 화면의 제목·항목명·시험 결과가 검게 표시되는 것을 확인했다. 디자인 스펙에 이미 테마 기준이 있었으며 구현에서 명시적 텍스트 색 연결이 누락됐다.
- 두 화면의 TextBlock 43개 전체에 기존 명시 색 또는 TextBrush가 연결되는지 확인했다. 기준 폴더 창의 라디오 2개와 공통 AnalysisSection, EditorUi.Field의 항목명, FilenameConditionPanel 시험 결과/오류에 DynamicResource/SetResourceReference를 적용했다. 일반 텍스트는 TextBrush, 보조 문구는 SubBrush, 조건 오류는 ErrorBrush를 사용한다. 글로벌 TextBlock 스타일은 추가하지 않았고 PrimaryButton의 OnAccentBrush를 보존했다.
- 영향 범위는 두 분석 화면·조건 패널과 Field를 쓰는 편집 화면의 항목명·공유 아코디언이다. 파일 검색/이동·설정 모델·목적지 선택 로직은 바꾸지 않아 관련 없는 전체 기능 검사를 반복하지 않았다.
- XAML 파싱과 텍스트/라디오의 명시적 색 연결, 공통 스타일 및 프로그램 생성 텍스트 경로를 검토했다. 두 테마의 Text/Sub/Error와 Page/Surface 조합 12개를 계산해 모두 4.5:1 이상: 라이트 5.50~14.67, 다크 6.93~15.07. 강조 버튼 글자/배경도 4.5:1 이상이다. 정적 팔레트 값 기준이며 비활성 투명도·실화면 합성 결과를 측정한 것은 아니다.
- 0.7.1 Windows x64 자체 포함·압축 단일 EXE 게시 성공. 실제 Windows WPF 렌더링·테마 전환·비활성/선택 상태·배율은 이 macOS에서 실행하지 못했다. docs/windows-test-guide.md에 재확인 항목을 추가했다.
- 정적 검토 기록: /private/tmp/chagok071-theme-review.json. 배포물 검증 결과는 /private/tmp/chagok071-package-verification.json에 저장한다.
