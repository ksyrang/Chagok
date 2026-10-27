# 차곡 브랜딩

현재 승인 시안은 `chagok-approved-board-v2.png`입니다. 2026-10-19 사용자가 첨부한 원본을 그대로 보관했습니다. 세 겹 폴더와 흐르는 지그재그 심볼이며, 하단 왼쪽의 남색 사각 아이콘을 앱 자산으로 사용합니다.

- 앱 이름: 차곡 / CHAGOK, 제작자: by ksyrang
- 새 아이콘 파생 원본: `chagok-icon-v2-source.png`
- PNG·ICO: `../../src/Chagok.Windows/Assets/chagok.png`, `chagok.ico`
- ICO 크기: 16·20·24·32·40·48·64·128·256px
- 적용: EXE·창·작업 표시줄·트레이·메인 상단·탐색기 패키지 로고·HTML 매뉴얼

파생 원본은 이미지 생성 도구로 앱 아이콘만 분리·정리한 이미지이며 원본을 그대로 자른 픽셀은 아닙니다. 남색 바탕 밖은 투명합니다. 파생 원본을 PNG/ICO/패키지 로고 크기로 변환했습니다. 이전 승인 원본 `chagok-approved-board.png`도 보존합니다.

설정 경로 `%LOCALAPPDATA%\FileOrganizer`는 호환성을 위해 유지합니다.

0.4.2 TUI 배너는 `src/Chagok.Terminal/BrandBanner.cs`에 블록 문자로 별도 정의합니다. 승인 아이콘의 밝은 심볼을 작은 셀로 옮기고 CHAGOK 픽셀 글자를 붙였습니다. 터미널 실행 중 이미지 파일을 불러오지 않으며 앱 PNG/ICO를 교체하지 않습니다.
