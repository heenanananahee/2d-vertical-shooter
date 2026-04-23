# 2D 세로형 슈팅 게임

Unity 2D 기반 세로형 슈팅(Vertical Shooter) 프로토타입 프로젝트입니다.

이 저장소에는 플레이 가능한 샘플 씬이 포함되어 있으며, 플레이어 이동/사격, 적 스폰, 피격 판정 등 핵심 게임 루프를 구현한 상태입니다. 현재 구조는 Unity 6 + URP 환경에서 동작하도록 구성되어 있습니다.

## 프로젝트 개요

- 장르: 2D 세로형 슈팅
- 엔진: Unity 6
- 렌더 파이프라인: URP (Universal Render Pipeline)
- 입력: Unity Input System + 레거시 키보드 입력 폴백
- 메인 씬: `Assets/Scenes/SampleScene.unity`
- 메인 게임플레이 스크립트: `Assets/PlayerBounds2D.cs`

## 주요 기능

- 카메라/수동 경계 기반 플레이어 이동 제한
- 쿨다운 기반 연속 발사
- 플레이어 위치/발사 지점 기준 탄환 생성
- 좌/우 보조탄 위치 자동 정렬
- 탄환 자동 이동 및 수명 시간 후 삭제
- 씬/프리팹 후보 기반 적 스폰
- 적 하강 이동 및 화면 이탈 시 정리
- 트리거 콜라이더 기반 피격 판정
- 적 체력(HP) 및 파괴 처리
- 적 피격 오버레이 이펙트(템플릿 기반)
- 씬 시작 시 템플릿/적 오브젝트 자동 준비 로직

## Unity 버전 및 의존성

- Unity Editor: `6000.4.3f1`
- 주요 패키지 (`Packages/manifest.json` 기준):
  - `com.unity.inputsystem` 1.19.0
  - `com.unity.render-pipelines.universal` 17.4.0
  - `com.unity.2d.animation` 14.0.3
  - `com.unity.2d.tilemap.extras` 7.0.1

## 실행 방법

1. Unity Hub를 실행합니다.
2. 현재 폴더를 프로젝트로 추가합니다.
3. Unity Editor `6000.4.3f1` 버전으로 엽니다.
4. `Assets/Scenes/SampleScene.unity` 씬을 엽니다.
5. Play 버튼을 눌러 실행합니다.

## 조작 방법

- 이동: `WASD` 또는 방향키
- 발사: `Space`

입력 처리는 다음 두 방식을 모두 지원합니다.

- New Input System (`UnityEngine.InputSystem`)
- Legacy Input 폴백 (`Input.GetAxisRaw`, `Input.GetKey`)

## 빌드 설정

현재 빌드 대상 씬:

- `Assets/Scenes/SampleScene.unity`

## 코드 구조

현재 게임플레이 코드는 아래 파일에 집중되어 있습니다.

- `Assets/PlayerBounds2D.cs`

이 파일에 포함된 클래스:

1. `PlayerBounds2D`
   - 플레이어 이동/경계 제한/사격/탄환 소스 탐색 처리
   - 씬에 스포너가 없으면 자동 생성

2. `ProjectileMover2D`
   - 탄환 방향 이동
   - 수명 시간 후 자동 삭제

3. `PlayerBulletHit2D`
   - 탄환 트리거 콜라이더/리짓바디 보장
   - 자식 스프라이트 파트에도 필요 시 콜라이더 부착

4. `EnemyHurtboxRelay2D`
   - 자식 콜라이더 충돌 이벤트를 부모 적 유닛으로 전달

5. `EnemyUnit2D`
   - 적 이동/체력/피격/사망 처리
   - 피격 오버레이 표시 및 정리

6. `TimedDestroy2D`
   - 지정 시간 후 오브젝트 자동 파괴 유틸리티

7. `EnemySpawner2D`
   - 적 스폰 소스 탐색
   - 카메라 상단 범위에서 랜덤 간격 스폰
   - 씬 템플릿 정리 및 씬 적 오브젝트 자동 연결

## 게임 진행 흐름

1. 씬 시작
2. 플레이어 컴포넌트가 스포너 존재 여부 확인 후 필요 시 생성
3. 매 프레임 이동 입력 읽기
4. 발사 키 유지 + 쿨다운 조건 충족 시 탄환 발사
5. 스포너가 주기적으로 적 생성
6. 적이 아래로 이동
7. 탄환 충돌 시 적 체력 감소
8. 체력이 0이면 적 제거

## 참고 사항 및 현재 한계

- 여러 게임플레이 클래스가 단일 파일(`PlayerBounds2D.cs`)에 모여 있습니다.
- 일부 템플릿 탐색이 이름 규칙(`Enemy A/B/C`, `... hit`)에 의존합니다.
- 자동 설정 로직 특성상 씬 오브젝트 이름 일관성이 중요합니다.
- 프로토타입 단계 구조이므로 추후 스크립트 분리/프리팹 구조화 리팩터링이 권장됩니다.

## 개선 아이디어

- 클래스별 스크립트 파일 분리
- 점수 시스템 및 UI 추가
- 플레이어 체력/게임오버 흐름 추가
- 탄환/적 오브젝트 풀링 적용
- 사운드 및 VFX 보강
- 플레이 모드 자동 테스트 추가

## 저장소 목적

이 저장소는 Unity 2D 세로형 슈팅 연습용 베이스라인 프로젝트입니다. 핵심 슈팅 루프 구현과 Unity 씬/컴포넌트 연결 흐름을 학습하는 데 목적이 있습니다.
