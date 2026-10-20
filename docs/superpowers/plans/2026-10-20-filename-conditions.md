# Chagok 0.6.1 분류 조건 선택 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 정규식을 몰라도 괄호 안 내용에 대한 조건을 GUI/TUI에서 고르고 같은 분석·규칙 결과를 얻는다. 직접 정규식 입력을 유지한다.
**Architecture:** Core의 FilenameConditionSet이 리터럴 조건을 정규식으로 변환한다. AnalysisOptions.Conditions와 RuleSpec.GroupConditions에 조건을 보관하며 실제 집계/실행은 기존 공통 정규식 엔진을 재사용한다. 조건 메타데이터와 정규식 불일치는 검증 단계에서 차단한다.
**Tech Stack:** C#/.NET 10, WPF, 기존 TuiForm, SQLite 분석 저장소.
**Spec:** 사용자 승인 대화 및 docs/ui-spec.md의 0.6.1 분류 조건 선택 절.

## Constraints

- 0.6.0 commit 06a56f7 / tag v0.6.0을 보존한다. 새 소스/패키지는 0.6.1이다. 새 GitHub 릴리스를 자동 생성하지 않는다.
- 대상 []/()/{} 안, 정확히 같음/시작/끝/포함, 하나라도 또는 모두 만족. 값은 정규식이 아닌 문자 그대로 해석하며 대소문자·공백을 구분한다.
- 최대 10개 조건, 각 값 100자, 정규식 2,000자/파일당 100ms 제한. 괄호 기본/직접 정규식/기존 설정과 이동 승인·잠금을 유지한다.
- 조건 선택은 괄호 안 실제 값을 그룹 이름으로 사용한다. 동일 조건에 맞더라도 xyz/x/y는 각각의 그룹이다. 생성 정규식을 직접 모드로 옮길 수 있으나 임의 정규식을 조건으로 역변환하지 않는다.
- 분석/미리보기/조건 편집은 실제 파일을 이동하지 않는다. 저장 취소/실패에서 기존 조건을 보존한다.

## Tasks

- [x] Core: ConditionChecks에 실패 테스트 추가 후 FilenameConditionSet/조건 enum/컴파일·요약 구현. OR/AND·각 연산·리터럴·괄호·오류·JSON/그룹 규칙 왕복 검증.
- [x] GUI: FilenameConditionEditor 전용 창과 분석 모드/규칙 편집 연결. 행 추가/삭제·조건 선택/값·OR/AND·시험 파일명·그룹/이유·생성식 보기. 직접 정규식/입력 도움말 보존.
- [x] TUI: TuiConditionEditor와 동적 TuiForm 항목, 분석 G/직접 식 E, 규칙 조건 편집 연결. 값/설명/미리보기/취소·동적 행 추가/삭제를 실제 PTY로 검증.
- [x] 통합: 메타데이터/직접 식 전환·기존 Group 호환과 전체 핵심 검사, 분석/기존 TUI smoke, Windows publish 및 소스/번들/자산 검증.
- [x] 전달: 버전·도움말 JSON·문서/Windows 점검표 갱신, 0.6.1 단일 EXE ZIP/라이선스/CRC/SHA 검증, 작업 기록 및 미검증 Windows 실제 UI 보고.

## Review Focus

AND/OR가 괄호 경계를 넘어 맞지 않는지, x 시작과 y 정확히 같음을 혼동하지 않는지, 정규식 메타문자가 리터럴로 처리되는지, 직접 식 수정 뒤 오래된 조건이 남지 않는지, 작은 TUI/동적 삭제/취소에서 상태가 보존되는지를 검토한다.
