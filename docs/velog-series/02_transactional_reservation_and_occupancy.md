# [SmartFactory AGV] 계획 경로를 바로 예약하면 안 되는 이유 — Transactional Reservation과 실제 점유

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: TimeInterval Reservation 이후의 WHCA* 구현  
> 핵심 키워드: `RoutePlanner`, `ReservationTable`, `Transaction`, `OccupancyProvider`, `Replan`

## 0. 들어가며

이전 글에서는 TimeInterval 기반 Node, Edge, Goal Reservation을 만들고 WAIT와 RRA*를 결합한 과정을 정리했다.

그런데 Reservation 자료구조가 있다고 해서 경로 전체가 안전해지는 것은 아니었다.

처음에는 PathFinder가 경로를 찾는 동안 사용할 구간을 바로 예약하는 방식도 생각했다.

```text
Node 1 예약 성공
Node 2 예약 성공
Edge 2-3 예약 성공
Node 3 예약 실패
```

여기서 탐색이 실패하면 앞에서 성공한 예약은 어떻게 해야 할까?

정확하게 rollback하지 않으면 실제로 아무 AGV도 사용하지 않는 구간이 예약된 채 남는다. 다른 AGV는 그 예약을 장애물처럼 보고 쓸데없이 기다리게 된다.

그래서 경로 탐색과 예약 확정을 분리했다.

## 1. 후보 생성과 상태 변경을 분리했다

현재 구조에서 PathFinder는 **경로 후보를 계산하는 역할**만 맡는다.

```text
PathFinder
  입력: start, goal, time, current reservation
  출력: PathStep 후보 목록
```

그리고 RoutePlanner가 후보 전체를 다시 검사한다.

```text
RoutePlanner
  1. 후보의 모든 Node interval 검사
  2. 모든 Edge interval 검사
  3. Goal hold 검사
  4. 전부 가능할 때만 예약 확정
```

코드 개념을 간략화하면 다음과 같다.

```cpp
auto candidate = pathFinder.FindPath(request);

if (!ValidateWholePath(candidate, reservationTable))
{
    return RouteResult::WAIT_REPLAN;
}

CommitReservations(candidate, reservationTable);
return RouteResult::SUCCESS;
```

이 구조에서 중요한 점은 `ValidateWholePath()`가 끝나기 전에는 shared reservation state를 바꾸지 않는다는 것이다.

DB transaction처럼 보자면 다음과 같다.

```text
BEGIN
  경로 후보 전체 검사
  문제가 있으면 ABORT
  모두 유효하면 COMMIT
END
```

## 2. 부분 예약이 남으면 왜 위험한가

부분 예약 문제는 단순히 메모리를 조금 더 사용하는 문제가 아니다.

AGV A가 목표까지 못 갔는데 앞부분 예약만 남았다고 가정해보자.

```text
AGV A 실제 상태:
  경로 없음

Reservation Table:
  Node 10과 Edge 10-11을 AGV A가 사용할 예정이라고 기록
```

이제 AGV B는 실제로 비어 있는 통로를 사용할 수 없다고 판단한다.

AGV B가 WAIT하고, 그 뒤 AGV C도 WAIT한다. 작은 stale reservation 하나가 전체 병목으로 퍼질 수 있다.

따라서 Reservation은 다음 조건을 가져야 했다.

- 경로 전체와 함께 생성된다.
- 경로 취소·재계획 시 미래 예약이 함께 정리된다.
- AGV 소유권이 명확하다.
- 일부만 성공한 상태가 외부에 보이지 않는다.

## 3. 미래 예약과 현재 점유는 다르다

Transactional Reservation을 적용한 뒤에도 문제가 하나 더 남았다.

Reservation Table은 계획 당시의 미래를 표현한다.

```text
10.0s: AGV 1이 Node 5에 도착 예정
11.0s: AGV 1이 Edge 5-6 통과 예정
```

하지만 실제 실행은 계획과 완전히 같지 않다.

- Unity frame이 잠깐 느려질 수 있다.
- 실제 AGV가 바닥 마찰로 늦을 수 있다.
- Vision correction 때문에 다음 edge 출발이 늦어질 수 있다.
- 다른 AGV가 아직 노드에서 떠나지 않았을 수 있다.

즉, `예약상 비어 있음`과 `지금 실제로 비어 있음`은 같은 의미가 아니다.

그래서 두 상태를 분리했다.

```text
ReservationTable
  미래 계획상 누가 언제 사용할 것인가

OccupancyProvider
  실행 시점에 실제로 누가 어디를 점유하고 있는가
```

## 4. 실행 직전에 다시 확인한다

경로가 예약되어 있어도 AGV가 다음 구간으로 진입하기 직전에 실제 점유를 확인한다.

```cpp
if (!occupancyProvider.CanEnter(agvID, nextNode, nextEdge))
{
    EmitExecutionBlocked(agvID);
    ReleaseFutureReservations(agvID);
    QueueReplan(agvID);
    return;
}

controller.Execute(nextStep);
```

이것은 planning을 믿지 않는다는 뜻이 아니다.

Planning은 최선의 미래 일정을 만든다. Occupancy 검사는 현실이 그 일정에서 벗어났을 때 마지막으로 충돌을 막는 경계다.

```text
Planning safety:
  미래 충돌을 가능한 한 미리 피함

Execution safety:
  실제 상태가 달라졌을 때 진입을 보류함
```

## 5. EXECUTION_BLOCKED는 실패가 아니라 상태 전이다

처음에는 경로대로 못 움직이면 에러로 처리하고 싶었다.

하지만 다중 AGV에서는 일시적인 점유 충돌이 항상 치명적인 오류는 아니다. 조금 기다리거나 새 경로를 찾으면 된다.

그래서 `EXECUTION_BLOCKED`를 별도 이벤트로 두었다.

```text
EXECUTION_BLOCKED
  → 남아 있는 미래 예약 정리
  → WAIT_REPLAN queue 진입
  → 일정 시점 뒤 다시 경로 요청
```

여기서 주의할 점은 현재 AGV가 실제로 점유 중인 위치까지 지우면 안 된다는 것이다. **미래 계획은 해제하되 현재 점유는 유지**해야 다른 AGV가 그 위치로 들어오지 않는다.

## 6. 가상 AGV와 실물 AGV에 같은 RoutePlanner를 사용했다

이 분리가 중요했던 또 다른 이유는 실제 ESP32를 연결했기 때문이다.

```text
RoutePlanner
  ↓ IRobotController
  ├─ UnityRobotController + MovementSimulator
  └─ ESP32RobotController + RobotSession
```

가상 AGV는 frame마다 위치를 부드럽게 갱신할 수 있다. 반면 실물 AGV는 엔코더 이동, settling, Vision correction과 TCP 상태에 영향을 받는다.

두 실행체의 시간 특성은 다르지만 RoutePlanner가 특정 실행체를 직접 알 필요는 없다.

```cpp
class IRobotController
{
public:
    virtual void FollowRoute(const Route& route) = 0;
    virtual RobotExecutionState GetState() const = 0;
    virtual ~IRobotController() = default;
};
```

구현 세부는 controller 뒤에 두고, 예약과 실행 이벤트만 공통 계약으로 연결했다.

## 7. 검증에서 확인한 것

Server에서는 다음 종류의 테스트를 분리했다.

- 경로 후보 전체가 유효할 때만 예약되는지
- Node/Edge/Goal interval 충돌을 거부하는지
- 실행 점유 실패 시 `EXECUTION_BLOCKED`가 발생하는지
- 미래 예약이 정리되고 재계획 queue로 들어가는지
- WAIT 정책이 우회와 안전 정지 사이에서 동작하는지

현재 Server의 전체 CTest는 9/9 통과했다.

다만 unit test가 실제 장시간 fleet의 deadlock 부재를 증명하는 것은 아니다. 그래서 별도로 28대 headless smoke와 Unity 화면을 함께 확인했다.

> [이미지 삽입] ReservationTable과 OccupancyProvider를 분리한 구조도

> [이미지 삽입] EXECUTION_BLOCKED 이후 재계획되는 Server 로그

## 8. 느낀 점

처음에는 Reservation Table을 하나의 자료구조 문제로 봤다.

하지만 실제로는 상태 수명주기의 문제였다.

```text
언제 예약을 만들 것인가?
언제 다른 AGV에게 보이게 할 것인가?
실행이 늦어지면 무엇을 취소할 것인가?
현재 점유와 미래 계획을 어떻게 구분할 것인가?
```

이 질문에 답하지 않으면 충돌 검사 함수가 아무리 정확해도 전체 시스템은 불안정해진다.

다음 글에서는 이 예약 구조 위에서 28대 AGV의 작업 목적지를 어떻게 분산하고, 상차·하차 상태를 Unity에 어떻게 복제했는지 정리한다.

> 다음 글: 28대 Virtual Fleet Dispatch와 Cargo State

