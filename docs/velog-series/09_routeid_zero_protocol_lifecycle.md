# [SmartFactory AGV] 경로는 있는데 routeID는 0이었다 — 첫 출발 Protocol Lifecycle 버그

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: Node Correction 수렴 문제  
> 핵심 키워드: `routeID`, `PRE_DEPARTURE`, `Protocol Invariant`, `Lifecycle`, `Safe Stop`

## 0. 들어가며

어느 날 실물 AGV를 Node 1에 정확히 배치하고 Vision도 `VERIFIED`인 상태에서 BOOT를 눌렀는데 로봇이 움직이지 않았다.

Server는 첫 경로 `1 → 6`을 정상적으로 계획했다. 시작 heading이 동쪽이고 다음 edge는 북쪽이므로 약 90° CCW 회전이 먼저 필요한 상황이었다.

그런데 회전 명령을 보내기 전에 Server가 SAFE STOP했다.

처음에는 Vision heading이나 위치 threshold 문제라고 생각했다. 로그를 따라가 보니 원인은 전혀 다른 곳에 있었다.

```text
PRE_DEPARTURE correction routeID = 0
```

경로는 존재했지만 그 경로의 ID는 아직 만들어지지 않은 상태였다.

## 1. routeID는 왜 필요한가

Server와 ESP32 사이에서 routeID는 단순 로그 번호가 아니다.

```text
TRAJECTORY_COMMAND routeID=12
STATUS routeID=12
ARRIVED routeID=12
NODE_CORRECTION routeID=12
```

이를 통해 다음을 확인한다.

- 현재 보고가 어떤 경로에 대한 것인지
- 이전 경로의 늦은 ARRIVED인지
- correction이 방금 완료한 trajectory와 연결되는지
- reconnect 뒤 stale command인지

따라서 `routeID=0`은 serializer에서 금지했고, Server와 ESP32도 유효한 경로로 인정하지 않았다.

## 2. 실제 lifecycle을 따라갔다

버그가 발생한 순서는 다음과 같았다.

```text
1. FollowRoute()가 계획 경로 저장
2. 다음 edge 출발 전 departure hold 생성
3. PRE_DEPARTURE 상태 시작
4. Vision으로 heading alignment 필요 판단
5. NODE_CORRECTION을 보내려 함
6. active routeID 조회
7. 아직 TRAJECTORY_COMMAND를 보내지 않아 routeID = 0
8. protocol safety check에서 SAFE STOP
```

routeID가 경로 계획 시점이 아니라 실제 `TRAJECTORY_COMMAND` 전송 시점에 생성되는 것이 핵심이었다.

```text
계획은 존재함
trajectory packet은 아직 없음
따라서 nonzero routeID도 아직 없음
```

## 3. 왜 correction이 trajectory보다 먼저였는가

첫 edge `1 → 6`은 현재 heading과 이동 방향이 달랐다.

따라서 Server는 출발 전에 방향을 맞추려고 했다.

```text
현재 heading: East, 0°
다음 edge: North, 약 90°

PRE_DEPARTURE:
  90° CCW correction 필요
```

하지만 기존 correction protocol은 기본적으로 **완료한 trajectory의 node에서 수행하는 correction**을 전제로 했다.

```text
TRAJECTORY 완료
  → ARRIVED
  → NODE_WAIT
  → same routeID로 correction
```

최초 출발 전에는 완료한 trajectory가 없다. 기존 correction 수명주기에 존재하지 않는 상태를 넣은 셈이다.

## 4. routeID 검사만 제거하면 되지 않을까

가장 쉬운 수정은 다음 검사 제거다.

```cpp
if (routeID == 0)
{
    SafeStop();
}
```

하지만 그렇게 하면 반대쪽 계약이 깨진다.

- Serializer가 routeID 0을 거부한다.
- Server RobotSession은 correction route가 마지막 trajectory와 같은지 검사한다.
- ESP32는 completed route의 `NODE_WAIT`에서만 correction을 받는다.
- 늦은 correction과 새로운 trajectory를 구분할 수 없다.

즉, 검사를 지우면 증상만 사라지고 protocol 의미가 모호해진다.

## 5. 양쪽 protocol을 바꾸는 방법도 검토했다

처음 분석에서는 다음 coordinated fix를 생각했다.

```text
Server:
  첫 edge routeID 미리 예약
  PRE_DEPARTURE correction과 trajectory가 같은 ID 사용

ESP32:
  BOOT 승인 + 정지 + current Node 일치 상태에서
  최초 pending routeID latch
  correction 후 같은 routeID trajectory만 수용
```

안전하게 구현할 수는 있지만 Server와 ESP32 양쪽의 route 의미를 바꿔야 한다. 실패·timeout·disconnect에서 pending context를 정리하는 상태도 새로 필요하다.

Packet layout은 그대로여도 protocol state machine 변화가 크다.

## 6. Server-only bootstrap으로 해결했다

더 작은 변경으로 기존 protocol invariant를 유지하는 방법을 선택했다.

최초 출발에서만 correction packet을 보내지 않고 fresh Vision pose로 시작 조건을 검증한다.

```text
적용 조건:
  routeID = 0
  current Node = 1
  아직 최초 출발 미확정
  fresh MEASURED + VERIFIED Vision pose 존재
```

그다음 다음을 검사한다.

```text
Node 1 중심 오차 <= 20 mm
공식 시작 heading 0° 오차 <= 10°
```

조건이 맞으면 departure hold를 해제하고, 실제 Vision heading을 trajectory의 시작 heading으로 사용한다.

```text
1 → 2:
  이미 East 방향
  rotate 없이 forward waypoint

1 → 6:
  trajectory 내부에 90° CCW ROTATE_IN_PLACE
  그 뒤 forward waypoint
```

실제 trajectory를 보낼 때 기존 방식으로 nonzero routeID를 발급한다.

```text
최초 출발:
  Vision pose 검증
  → trajectory 전송
  → routeID 발급

첫 edge 완료 후:
  기존 completed-route 기반 correction 사용
```

ESP32 protocol을 바꾸지 않고도 첫 출발과 이후 correction의 의미를 분리했다.

## 7. 테스트한 경계

다음 경우를 각각 검사했다.

- 실측 pose `(1.02 mm, 1.19 mm, 0.43°)` 승인
- 시작 위치 20 mm 초과 거부
- 시작 heading 10° 초과 거부
- `1 → 2`에서 불필요한 회전 없음
- `1 → 6`에서 trajectory 내부 90° CCW 후 직진
- nonzero routeID와 start/final node 보존
- departure release 중복 실행 차단
- 첫 edge 이후에는 기존 correction lifecycle 사용

관련 Server 전체 CTest는 9/9를 통과했다.

> [이미지 삽입] FollowRoute부터 routeID 0 SAFE STOP까지 sequence diagram

> [이미지 삽입] 수정 후 `Vision bootstrap → trajectory routeID 발급` 흐름

> [로그 삽입] PRE_DEPARTURE 실패 전/후 비교

## 8. 이 버그에서 중요했던 것

겉으로 보이는 증상은 단순했다.

```text
BOOT를 눌렀는데 안 움직임
```

하지만 원인은 다음 중 어느 것도 아니었다.

- 모터 전원
- Wi-Fi 연결
- Vision calibration
- 회전 angle 계산

실제 원인은 `routeID가 언제 유효해지는가`라는 시간 순서였다.

하나의 변수 값만 보면 `0이니까 이상하다`로 끝난다. 경로 계획, hold, correction, packet 전송의 순서를 따라가야 왜 0인지 알 수 있었다.

## 9. 느낀 점

Protocol field는 packet 안에서의 자료형만 정한다고 끝나지 않는다.

```text
누가 생성하는가?
언제 유효해지는가?
어떤 상태에서 재사용할 수 있는가?
disconnect 시 언제 폐기하는가?
```

이 lifecycle이 protocol의 일부다.

이번에는 validation을 약하게 만드는 대신 최초 출발이라는 별도 상태를 명시하면서 기존 invariant를 유지했다.

다음 글에서는 조금 더 현실적인 디버깅 이야기를 정리한다. BOOT를 눌러도 안 움직일 때 실제 원인은 Server code가 아니라 WSL portproxy, Wi-Fi, VCC와 끊어진 배터리 선이었던 사례들이다.

> 다음 글: ESP32 ↔ WSL ↔ Vision ↔ Unity Cross-System Debugging

