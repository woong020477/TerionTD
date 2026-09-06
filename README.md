# TerionTD

> 우주 정거장 Terion에서 타워를 건설하고, 자신의 라인에 누적되는 적을 저지하는 최대 4인 타워 디펜스 게임입니다.

Unity 클라이언트의 게임플레이, UI, 소켓 API 연동과 멀티플레이 상태 동기화를 구현한 프로젝트입니다. 플레이어마다 라운드와 승패를 독립적으로 관리하며, 머신건·로켓·레이저·화염방사 타워를 건설하고 연구할 수 있습니다.

이 저장소는 코드 리뷰를 위한 클라이언트 스크립트와 플레이 이미지 모음입니다. 씬·프리팹·상용 에셋을 포함한 전체 Unity 프로젝트가 아니므로 이 저장소만으로 게임을 빌드할 수는 없습니다.

- [클라이언트 스크립트](Info_Data/script)
- [.NET 게임 서버 저장소](https://github.com/woong020477/TDGameServer)

![TerionTD 플레이 화면](Info_Data/README_Title1.png)

## 프로젝트 개요

| 항목 | 내용 |
|---|---|
| 장르 / 인원 | 타워 디펜스 / 최대 4인 |
| 클라이언트 | Unity 6000.3.21f1, C#, URP, uGUI·TextMeshPro, DOTween |
| 대상 플랫폼 | Windows PC, 개발·검증 환경 macOS |
| 통신 | 직접 정의한 JSON 소켓 프로토콜: TCP는 인증·로비·채팅, UDP는 게임 이벤트·상태 |
| 연동 서버 | .NET 8, MySQL 8.0, Ubuntu VPS |
| 게임 권한 | 호스트 클라이언트가 적 HP·웨이브·결과를 확정하는 호스트 권한 모델 |

### 주요 구현

- 로그인 → 로비 → 방 입장 → 게임 씬으로 이어지는 세션과 UI 흐름
- 방 목록·상세 정보·슬롯 이동·방장 변경·강퇴 응답의 화면 반영
- 플레이어 소유권과 영역 검사를 적용한 타워 건설·이동·업그레이드
- 플레이어별 라운드, 생존 적 수, 휴식 시간 및 개별 승패 처리
- 호스트 상태 스냅샷을 활용한 비호스트 적 이동 보간과 제한된 외삽
- 고정 표적 연사, 실제 피해와 시각 연출 분리, 투사체 풀링
- 공격 범위 표시와 클릭 판정 분리: SphereCollider 반경을 초록색 LineRenderer로 표시

## 먼저 읽으면 좋은 코드

전체 코드를 순서대로 읽기보다 다음 진입점에서 관련 책임을 따라갈 수 있도록 구성했습니다.

| 관심 영역 | 진입점 | 확인할 내용 |
|---|---|---|
| 게임 시작과 권한 | [GameManager](Info_Data/script/Gameplay/Match/GameManager.cs) | 참가자 초기화, 호스트 변경, 개별 결과 |
| 플레이어별 진행 | [WaveManager](Info_Data/script/Gameplay/Match/WaveManager.cs) | Running/Waiting/Finished 전환과 라운드 경계 |
| 전투와 풀링 | [TowerController](Info_Data/script/Gameplay/Towers/TowerController.cs) → [BurstProjectile](Info_Data/script/Gameplay/Projectiles/BurstProjectile.cs) → [Enemy](Info_Data/script/Gameplay/Enemies/Enemy.cs) | 발사, 단일 피해 요청, 호스트 판정, 표적 상실 시 반환 |
| TCP API 연동 | [AuthManager](Info_Data/script/Networking/Session/AuthManager.cs) → [TcpLineConnection](Info_Data/script/Networking/Transport/TcpLineConnection.cs) | 세션·응답 이벤트와 소켓 I/O의 분리 |
| UDP 동기화 | [UDPClient](Info_Data/script/Networking/Transport/UDPClient.cs) → [메시지 계약](Info_Data/script/Networking/Messages) | 메인 스레드 전달, 권한 검사, P2P/중계 경로 |
| 소유권과 화면 | [BuildingSystem](Info_Data/script/Gameplay/Building/BuildingSystem.cs) · [UIManager](Info_Data/script/UI/Hud/UIManager.cs) | 요청자 영역 검사와 로컬 선택 유지 |

## 스크립트 구조

Unity 프로젝트의 `Assets/Scripts`와 동일한 기능별 구조로 공개합니다. `Editor`에는 별도의 배치 검증 코드를 모았습니다.

```text
Info_Data/script/
├── Gameplay/
│   ├── Match/          게임 초기화, 플레이어별 웨이브·결과
│   ├── Player/         로컬 입력, 골드, 건설 모드
│   ├── Enemies/        적 시뮬레이션, 상태 재현, 스폰 큐
│   ├── Towers/         표적·공격 주기, 타워 베이스와 범위 표시
│   ├── Projectiles/    연사 투사체 공통 수명주기와 무기별 연출
│   └── Building/       그리드 건설·이동과 플레이어 영역 검사
├── Networking/
│   ├── Session/        인증 세션과 서버 접속 설정
│   ├── Transport/      TCP 프레이밍, UDP 송수신·메인 스레드 전달
│   ├── Messages/       인증·방·피어·타워·적·매치의 JSON 계약
│   └── Identity/       소유자와 로컬 ID를 결합한 엔티티 키
├── UI/
│   ├── Login/          로그인 화면
│   ├── Lobby/          방 목록·상세·참가 화면
│   ├── Chat/           채팅 입력과 수신 표시
│   ├── Hud/            게임 상태와 선택·업그레이드 패널
│   ├── Settings/       옵션 화면과 설정 저장
│   └── Common/         입력 필드·기존 UI 호환 보조 코드
├── Data/
│   ├── Towers/         타워 종류, 업그레이드 데이터와 조회 카탈로그
│   └── Waves/          웨이브 정의와 적 배치 데이터
├── Input/              입력 이벤트와 선택 레이캐스트
├── Presentation/       카메라·오디오
├── Infrastructure/     투사체 풀 관리
└── Editor/             전투·선택·리팩터링 회귀 검증
```

### 책임을 나눈 기준

- UI는 사용자의 요청을 만들고 응답을 표시합니다. 소켓의 연결·프레이밍은 `TcpLineConnection`, 세션 상태와 응답 이벤트는 `AuthManager`가 담당합니다.
- JSON 계약을 `Messages`로 옮겨 네트워크 처리 코드를 읽지 않아도 필드와 요청/결과의 차이를 확인할 수 있게 했습니다. 서버 호환성을 위해 필드명과 타입은 유지했습니다.
- 타워는 공격을 조정하고, [TowerUpgradeCatalog](Info_Data/script/Data/Towers/TowerUpgradeCatalog.cs)는 JSON 로딩과 레벨별 피해·비용 조회를 담당합니다.
- `BurstProjectile`은 추적·명중·반환을 공유합니다. `Bullet`과 `Rocket`은 모델 방향, 스턴 시간, 명중 이펙트처럼 실제로 다른 부분만 정의합니다.
- 폴더 이동 시 MonoScript GUID와 Inspector 필드 이름을 유지했습니다. 주석은 권한, 취소·정리 시점, 다른 구현을 선택하지 않은 이유를 중심으로 작성했습니다.

큰 조정자 클래스가 모두 분해된 구조는 아닙니다. `Enemy`의 UI·보스 연출, `UIManager`의 패널별 책임, `AuthManager`의 인증 화면 의존성은 추가로 분리할 부분입니다.

## 네트워크와 상태 소유권

```text
로그인·로비·채팅
Unity 클라이언트 ── TCP / 줄바꿈 JSON ── .NET 서버 ── MySQL

게임 중
타워 소유 클라이언트 ── 피격 요청 ── 호스트 클라이언트
모든 참가자          ←─ 확정 HP·웨이브·결과 ── 호스트 클라이언트
```

게임 이벤트는 UDP를 사용합니다. 직접 연결이 확인된 피어끼리는 P2P로 전송하고, 직접 경로가 준비되지 않았으면 같은 방의 VPS 중계를 사용합니다. 서버는 방과 피어 정보를 관리하며, 게임의 적 시뮬레이션을 대신 실행하지는 않습니다.

따라서 서버가 없는 완전한 P2P나 전용 서버 권한 게임으로 설명하지 않습니다. 또한 호스트 권한 판정과 공격 수치에 대한 완전한 부정행위 검증은 별개의 문제입니다.

## 이슈 트래킹: 클라이언트 관점의 문제 해결 5건

아래 사례는 실제 코드와 수정 기록에 근거합니다. 적용한 구조와 검증 범위를 구분했으며, 측정하지 않은 FPS 개선율은 기재하지 않았습니다.

### 1. TCP 수신 단위와 JSON 메시지 단위가 일치하지 않는 문제

증상: 서버 응답을 한 번의 `Read` 결과로 파싱하면 JSON 일부만 도착하거나 여러 응답이 붙어 있을 때 처리가 깨질 수 있었습니다.

원인: TCP는 메시지가 아니라 바이트 스트림을 전달합니다. 클라이언트가 소켓의 수신 경계를 애플리케이션 메시지 경계로 취급한 것이 문제였습니다.

해결: 전송 시 JSON 뒤에 줄바꿈을 붙이고, [JsonLineBuffer](Info_Data/script/Networking/Transport/JsonLineBuffer.cs)가 완성된 줄만 반환하도록 했습니다. 남은 조각은 다음 수신까지 보관하고, 연결을 종료하면 누적 상태도 정리합니다. [TcpLineConnection](Info_Data/script/Networking/Transport/TcpLineConnection.cs)은 이를 인증·로비 코드에서 분리합니다.

선택 이유와 검증: 기존 서버의 줄바꿈 계약과 맞는 작은 전송 계층을 만들었습니다. 분할·병합·CRLF·재연결 초기화·버퍼 한도를 배치 검사했습니다. UTF-8 문자 자체가 바이트 경계에서 나뉘는 경우의 증분 디코딩은 별도 개선 과제입니다.

### 2. 서버 상태는 바뀌었는데 방 상세·참가 UI가 남는 문제

증상: 자리 이동·방장 위임 후 상대 화면이 늦게 갱신되거나, 강퇴·방 삭제 뒤에도 열린 패널에 이전 정보가 남았습니다. 자동 갱신까지 수동 갱신 완료 문구를 표시하는 문제도 있었습니다.

원인: 방 목록, 선택한 방 상세, 참가 중인 방을 서로 다른 화면 상태로 관리하면서 서버 응답과 함께 무효화할 경로가 부족했습니다.

해결: [AuthManager](Info_Data/script/Networking/Session/AuthManager.cs)의 응답 이벤트를 [LobbyManager](Info_Data/script/UI/Lobby/LobbyManager.cs)가 구독하도록 하고, 최신 목록에서 사라진 방의 상세를 닫도록 했습니다. 강퇴 시 참가 패널과 세션의 현재 방 정보를 함께 정리합니다. 목록 요청에는 수동 갱신 여부를 따로 기록해 알림 표시를 구분했습니다.

선택 이유와 검증: 버튼 클릭 때 화면만 바꾸는 대신 서버가 전달한 상태를 화면의 근거로 삼았습니다. 코드에 반영된 갱신 경로와 컴파일은 확인했으며, 현재 리팩터링 뒤의 강퇴·위임·연속 재입장은 두 클라이언트 UI 회귀 시험 대상입니다.

### 3. 다른 플레이어의 타워 상태가 내 선택·조작 UI에 섞이는 문제

증상: 다른 플레이어가 업그레이드하면 내 선택 창이 열리거나, 타인의 타워에서 이동 버튼이 활성화되고 다른 영역에 건설·이동할 수 있었습니다.

원인: 클라이언트마다 발급하는 로컬 ID만으로는 다른 소유자의 엔티티를 구분할 수 없고, 원격 상태 갱신과 로컬 선택 동작에도 소유권 경계가 필요했습니다.

해결: [NetworkEntityKey](Info_Data/script/Networking/Identity/NetworkEntityKey.cs)에서 소유자 인덱스와 로컬 ID를 결합합니다. [UIManager](Info_Data/script/UI/Hud/UIManager.cs)는 현재 선택한 타워만 갱신하고, 이동 버튼과 실행 경로에서 소유자를 확인합니다. [BuildingSystem](Info_Data/script/Gameplay/Building/BuildingSystem.cs)은 배치 지점이 요청자 영역인지 검사합니다.

선택 이유와 검증: 버튼을 숨기는 것만으로 끝내지 않고 조회·표시·실행 경계를 함께 정리했습니다. 현재는 코드와 컴파일 수준을 확인했으며, 다른 플레이어의 건설·연구·이동을 동시에 실행하는 재시험이 남아 있습니다. 이 클라이언트 검사가 완전한 서버 보안을 대신하지는 않습니다.

### 4. 개별 라운드와 호스트 교체 뒤 타이머 재개 문제

증상: 독립적으로 진행해야 할 라운드가 공유되거나, 호스트 교체 뒤에도 비호스트 타이머가 멈춰 있었습니다.

원인: 호스트의 판정 권한, 각 플레이어 라인의 진행 상태, 내 화면에 표시할 타이머를 구분하지 않으면 동일 상태를 여러 곳에서 갱신하거나 새 호스트가 진행 상태를 이어받지 못합니다.

해결: [WaveManager](Info_Data/script/Gameplay/Match/WaveManager.cs)는 한 라인의 Running/Waiting/Finished 상태를 관리하고, 표시 대상은 로컬 라인으로 제한합니다. 스폰 완료 후 적을 모두 처치하거나 제한 시간이 끝나면 라운드 경계를 처리합니다. 진행이 계속되는 라인은 5초 휴식 뒤 다음 라운드로 넘어가며 생존 수 한도를 다시 검사합니다. [GameManager.ApplyHostChanged](Info_Data/script/Gameplay/Match/GameManager.cs)가 스포너와 웨이브의 권한 인계를 연결합니다.

선택 이유와 검증: 판정은 호스트 한 곳에서 하되 라인 상태는 개별로 유지하는 구조입니다. 관련 분기와 컴파일을 확인했지만, 새 리팩터링 버전에서 실행 중 호스트 종료와 타이머 재개를 다중 PC로 재검증해야 합니다.

### 5. 연사 중 파괴된 Enemy 참조와 반복 패킷 문제

증상: 머신건·다연장 로켓 공격 중 `MissingReferenceException`이 발생했고, 비호스트 화면이 끊겼습니다.

원인: 늦게 도착한 발사 이벤트가 사망한 적을 참조했습니다. `currentEnemy?.transform`은 Unity의 파괴된 오브젝트 판정을 대신하지 못합니다. 기존 버스트는 매 발마다 발사·피격 요청을 반복하고 다연장 로켓은 주변 적 검색도 수행했습니다.

해결: [TowerController](Info_Data/script/Gameplay/Towers/TowerController.cs)는 패킷에 지정된 살아 있는 대상만 재현합니다. 한 버스트의 표적을 고정하고 첫 명중에 기존 명목 합계인 공격력 5.5배를 한 번 판정합니다. 나머지 탄환과 HP 게이지는 연출로 분리했습니다. [BurstProjectile](Info_Data/script/Gameplay/Projectiles/BurstProjectile.cs)은 대상 상실 이벤트에서 투사체를 즉시 반환하고 잔탄을 다른 적에게 넘기지 않습니다.

선택 이유와 검증: 투사체를 전부 없애지 않고 연출과 실제 판정의 횟수를 분리했습니다. 격리된 Unity 검사에서 기존 예외를 재현했고 연사 관련 79개 검사 항목이 통과했습니다. 로컬 UDP 소켓에서는 비호스트 소유 타워의 버스트당 발사 1개·피격 요청 1개·호스트 HP 응답 1개를 확인했습니다. 실제 두 PC의 프레임 개선은 아직 측정하지 않았습니다.

트레이드오프: 다연장 광역 피해를 단일 표적 집중으로 바꾸었고, 첫 합산 명중 시 사망하면 나머지 연출도 종료합니다. 스턴 역시 버스트 단위 판정이므로 이전의 시간차 다중 명중과 모든 밸런스 결과가 같지는 않습니다.

## 검증 범위와 남은 과제

- 실행 확인: macOS 클라이언트의 VPS 로그인·로비 진입, 두 클라이언트 TCP·UDP 통신은 수동 테스트로 확인했습니다.
- 자동 확인: 구조 정리 뒤 Unity 배치 컴파일, 씬·프리팹 스크립트 참조 798개, 업그레이드 레벨·타워 종류 1,000개 조합, TCP 텍스트 프레이밍, 연사 79개 검사 항목과 타워 5종의 본체/범위 클릭 구분 검사가 통과했습니다. [검증 진입점](Info_Data/script/Editor/PortfolioValidation.cs)
- 구분: 격리된 배치 검사와 실제 게임의 Play Mode·Windows 빌드·멀티플레이 성능 검증은 다릅니다. 이번 구조 정리 뒤 Windows 재빌드와 두 PC 회귀 시험이 필요합니다.
- 다음 개선: UDP 주요 이벤트의 유실·역전 처리, UTF-8 증분 디코딩, 반복 폭발 이펙트 풀링, 큰 UI/적 클래스의 책임 분리와 자동 테스트 확대.
- 서버 연동 측면에서는 전송 암호화와 인증·공격 요청 검증도 더 보완해야 합니다. 학습·포트폴리오 프로젝트를 상용 보안 수준으로 표현하지 않습니다.

## 플레이 이미지

<details>
<summary>추가 화면 보기</summary>

![플레이 화면 2](Info_Data/README_Title2.png)
![플레이 화면 3](Info_Data/README_Title3.png)
![플레이 화면 4](Info_Data/README_Title4.png)
![플레이 화면 5](Info_Data/README_Title5.png)
![플레이 화면 6](Info_Data/README_Title6.png)
![플레이 화면 7](Info_Data/README_Title7.png)
![플레이 화면 8](Info_Data/README_Title8.png)

</details>
