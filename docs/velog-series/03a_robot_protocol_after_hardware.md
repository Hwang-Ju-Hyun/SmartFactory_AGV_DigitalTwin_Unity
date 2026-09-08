# [SmartFactory AGV] 차체가 도착한 뒤 RobotProtocol은 어떻게 달라졌을까?

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: 초기 RobotProtocol 설계  
> 핵심 키워드: `TCP`, `Binary Protocol`, `Trajectory`, `Capability`, `STATUS`, `ARRIVED`, `Correction`

## 0. 들어가며

예전에 Unity protocol과 ESP32 RobotProtocol을 분리한 글을 작성했다.

당시에는 실제 차체가 오기 전이었다. ESP32가 route를 받으면 progress를 임시로 증가시켜 Server와의 통신 loop를 먼저 확인했다.

글의 마지막에도 이렇게 적었다.

```text
현재:
  progress를 임시 계산
  STATUS 전송

차체 연결 후:
  encoder로 거리 계산
  heading 보정
  motor PWM 제어
  실제 STATUS 전송
```

그리고 실제로 차체, TB6612FNG와 encoder를 연결하자 protocol에 필요한 의미가 훨씬 많아졌다.

단순 `ROUTE_COMMAND → STATUS → ARRIVED`만으로는 부족했다.

```text
이 ESP32가 어떤 명령을 실행할 수 있는가?
경로를 node 목록으로 보낼 것인가, motion waypoint로 보낼 것인가?
도착 후 Vision correction은 어느 route에 속하는가?
같은 correction이 두 번 오면 어떻게 할 것인가?
motor fault detail을 packet 구조 변경 없이 어떻게 남길 것인가?
```

이번 글은 초기 RobotProtocol 글의 후속편이다.

## 1. 전체 연결 구조는 유지했다

가장 중요한 구조는 바뀌지 않았다.

```text
Unity가 ESP32를 직접 제어하지 않는다.
ESP32가 Unity에 직접 상태를 보내지 않는다.
```

전체 흐름은 다음과 같다.

```text
C++ Server
  ├─ 작업·경로·예약 결정
  ├─ ESP32에 trajectory 전송
  └─ ESP32 상태를 world state에 반영

ESP32
  ├─ trajectory 검증
  ├─ motor/encoder로 실행
  └─ STATUS/ARRIVED/ERROR 보고

Unity
  └─ Server가 확정한 상태를 표시
```

즉 초기 글에서 정한 책임 경계는 실제 차체를 붙인 뒤에도 유효했다.

## 2. HELLO에 Capability를 추가했다

초기 handshake는 version, client type과 AGV ID를 확인했다.

실제 firmware profile이 늘어나면서 같은 ESP32 binary라도 지원 범위가 달라졌다.

```text
Preview build:
  trajectory를 parse/validate/store만 함
  motor output 없음

Physical fleet build:
  trajectory 실행 가능
  node correction 가능
```

그래서 HELLO에 capability bitmask를 포함했다.

```cpp
enum ClientCapability : uint32_t
{
    CAPABILITY_NONE               = 0,
    CAPABILITY_TRAJECTORY_COMMAND = 1u << 0,
    CAPABILITY_TRAJECTORY_PREVIEW = 1u << 1,
    CAPABILITY_NODE_CORRECTION    = 1u << 2
};
```

```cpp
struct HelloPayload
{
    uint16_t protocolVersion;
    ClientType clientType;
    uint32_t requestedAgvID;
    uint32_t capabilities;
};
```

Server는 접속했다는 사실만으로 자동 경로를 보내지 않는다. Physical Fleet에 필요한 capability가 실제로 광고됐는지 확인한다.

```text
TCP connected
  ≠ motion ready

HELLO accepted
+ trajectory capability
+ correction capability
+ Vision ready
  → automatic dispatch 가능
```

## 3. Node 목록 대신 Trajectory Waypoint를 사용했다

초기 `ROUTE_COMMAND`는 node와 도착/출발 시간을 전달하는 구조였다.

하지만 ESP32가 실제로 움직이려면 node ID만으로는 부족하다.

```text
Node 1 → Node 6
```

만 보고는 다음을 알 수 없다.

- 로봇 기준으로 앞으로 몇 mm인지
- 먼저 제자리 회전해야 하는지
- 목표 heading이 얼마인지
- 어느 waypoint가 node boundary인지
- 어느 지점에서 반드시 멈춰야 하는지

그래서 `TRAJECTORY_COMMAND`를 추가했다.

```cpp
struct TrajectoryWaypoint
{
    float forwardMm;
    float leftMm;
    float headingRad;
    float targetSpeedMmPerSecond;
    uint32_t nodeID;
    uint8_t flags;
};
```

좌표는 robot-local frame을 사용한다.

```text
+forward:
  신뢰한 trajectory 시작 heading 방향

+left:
  forward에서 CCW 90° 방향
```

Flag는 waypoint 의미를 전달한다.

```cpp
TRAJECTORY_FLAG_NODE_BOUNDARY
TRAJECTORY_FLAG_STOP
TRAJECTORY_FLAG_ROTATE_IN_PLACE
TRAJECTORY_FLAG_FINAL
```

Server는 전역 맵 경로를 알고, ESP32는 전달받은 local primitive를 encoder target으로 바꾼다.

## 4. Packet 크기를 compile time에도 검사했다

ESP32 receive buffer는 무한하지 않다.

현재 protocol은 최대 64 waypoints와 2048-byte frame 제한을 가진다.

```cpp
constexpr uint16_t kMaxTrajectoryWaypoints = 64;
constexpr uint16_t kMaxFrameSize = 2048;
```

Waypoint wire size와 fixed payload를 이용해 최대 frame이 buffer를 넘지 않는지 `static_assert`로 확인했다.

```cpp
static_assert(
    kFrameHeaderSize
    + kPacketBodyHeaderSize
    + kMaxTrajectoryPayloadSize
    <= kMaxFrameSize,
    "Maximum trajectory frame exceeds the ESP32 receive limit");
```

Runtime parser에서도 waypoint count, format version, payload size와 trailing byte를 검사한다.

## 5. STATUS progress를 encoder에서 만들었다

초기 글에서는 progress를 임시 증가시켰다.

현재 Physical Fleet에서는 실제 encoder target과 current count를 사용한다.

```text
leftProgress  = leftCount / leftTarget
rightProgress = rightCount / rightTarget

route progress:
  더 느린 wheel의 진행률을 기준으로 제한
```

한쪽 wheel만 target에 도달했다고 전체 route를 완료로 표시하지 않기 위해서다.

STATUS에는 다음 의미가 들어간다.

```text
currentNodeID
currentLinkID
progress
x / z / heading
velocity
battery field
robot state
```

다만 현재 firmware에서 중요한 실제 motion feedback은 encoder와 state이며, `battery` field가 존재한다고 해서 정밀한 배터리 계측까지 완료됐다고 주장하지는 않는다.

## 6. ARRIVED는 encoder target만으로 보내지 않았다

Packet 하나를 더 보내는 것처럼 보이지만 ARRIVED는 Server state를 다음 node로 확정시키는 terminal event다.

그래서 다음 조건을 통과해야 한다.

```text
routeID 일치
final waypoint 완료
양쪽 wheel target 도달
settling 완료
unsafe output 없음
TCP session 유효
ARRIVED 중복 전송 아님
```

ARRIVED를 보낸 뒤에는 바로 다음 correction을 임의로 실행하지 않고 `NODE_WAIT` 상태에서 Server 명령을 기다린다.

## 7. NODE_CORRECTION protocol

Vision 기반 node 보정을 위해 두 packet을 추가했다.

```text
Server → ESP32
  NODE_CORRECTION_COMMAND = 103

ESP32 → Server
  NODE_CORRECTION_REPORT = 202
```

Command는 다음 context를 가진다.

```cpp
struct NodeCorrectionCommandPayload
{
    uint32_t routeID;
    uint32_t nodeID;
    uint32_t commandID;
    NodeCorrectionAction action;
    float magnitude;
};
```

Action은 bounded primitive만 허용한다.

```text
DRIVE_FORWARD
TURN_CW
TURN_CCW
```

임의의 PWM 값을 network에서 직접 받지 않는다. Magnitude를 firmware의 calibration과 안전 범위 안에서 encoder target으로 변환한다.

Report에도 같은 context를 되돌려준다.

```cpp
struct NodeCorrectionReportPayload
{
    uint32_t routeID;
    uint32_t nodeID;
    uint32_t commandID;
    NodeCorrectionResult result;
    uint32_t detail;
};
```

이렇게 해야 늦게 도착한 이전 command의 결과가 현재 correction state를 완료시키지 않는다.

## 8. 중복 packet을 어떻게 다뤘는가

실제 네트워크에서 Server가 report를 받지 못해 command를 다시 보낼 수 있다.

같은 `commandID`를 받을 때 motor를 다시 움직이면 동일한 correction이 두 번 실행된다.

그래서 ESP32는 현재/완료 command context를 확인한다.

```text
새 commandID:
  조건 검증 후 실행

이미 완료한 commandID:
  motor 재실행 금지
  terminal report만 재전송 가능

다른 route/node context:
  reject
```

Idempotence는 cargo UI뿐 아니라 실제 motor command에서도 중요했다.

## 9. Motor fault detail을 확장했다

Server에는 `MOTOR_FAULT` 한 종류로 보이던 오류가 실제로는 여러 원인을 가질 수 있었다.

```text
한쪽 wheel progress 부족
target mismatch
잘못된 motion mode
stalled encoder
```

기존 ERROR packet layout을 바꾸지 않으면서 wheel mismatch snapshot을 추가로 전달하기 위해 tagged detail record를 사용했다.

```text
CONTEXT
LEFT_PROGRESS
RIGHT_PROGRESS
LEFT_TARGET
RIGHT_TARGET
```

이 값은 fault 순간에 freeze한다. 이후 encoder 값이 변해도 최초 오류 context를 잃지 않는다.

## 10. 초기 글과 현재 구조 비교

```text
초기 단계
  ROUTE_COMMAND node 목록
  임시 progress
  STATUS / ARRIVED protocol loop
  실제 motor 없음

현재 단계
  Capability handshake
  TRAJECTORY_COMMAND local waypoint
  LINE / ROTATE_IN_PLACE encoder 실행
  실제 encoder progress
  settling 후 ARRIVED
  NODE_CORRECTION command/report
  fault-latched motor diagnostic
```

처음 만든 protocol을 버린 것이 아니라, 실제 하드웨어에서 필요한 상태를 기존 책임 경계 안에 추가했다.

## 11. 검증

```text
ESP32 host tests: 5/5 PASS
PlatformIO profiles: 14개 build
Channel A/B mapping: 실제 encoder와 일치
90° CCW isolated test: L/R normalized count 일치
Physical route: STATUS / progress / ARRIVED 확인
Server: CTest 9/9 PASS
```

> [이미지 삽입] 초기 ROUTE_COMMAND와 현재 TRAJECTORY_COMMAND 비교

> [이미지 삽입] routeID/nodeID/commandID correction sequence

> [로그 삽입] TRAJECTORY 수신 → encoder progress → ARRIVED

## 12. 느낀 점

초기 RobotProtocol 글에서는 packet field를 정하는 일이 중심이었다.

실제 차체를 붙인 뒤에는 field보다 lifecycle이 더 중요해졌다.

```text
이 ID는 언제 생성되는가?
어떤 state에서 command를 받을 수 있는가?
중복 command가 오면 motor를 다시 움직일 것인가?
언제 terminal report를 재전송할 수 있는가?
fault 뒤 새 route를 받을 수 있는가?
```

Protocol은 struct 목록이 아니라 두 state machine 사이의 계약이라는 것을 실제로 체감했다.

다음 글에서는 이 protocol이 실제 motor를 움직이기 전에 통과해야 하는 BOOT 승인, countdown, fault latch와 ARRIVED gating을 정리한다.

> 다음 글: ESP32 Physical AGV의 안전 상태 머신

