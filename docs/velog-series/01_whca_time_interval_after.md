# [SmartFactory AGV] TimeInterval Reservation을 만들고도 끝나지 않았던 WHCA* 구현

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: Cooperative A*와 Node/Link Reservation / TimeInterval Reservation과 RRA*  
> 핵심 키워드: `WHCA*`, `TimeInterval`, `Node Reservation`, `Edge Reservation`, `Goal Reservation`, `WAIT`

## 0. 들어가며

이전 글에서는 Cooperative A*의 `(Node, Time)` 개념에서 시작해 문자열 기반 Time Slot Reservation을 만들고, Node만 예약했을 때 발생한 정면 충돌을 막기 위해 Link Reservation을 추가한 과정을 정리했다.

그다음에는 실제 AGV의 이동 시간이 항상 정수 slot에 맞지 않는다는 문제 때문에 예약을 `TimeInterval`로 바꿨다.

```cpp
struct TimeInterval
{
    float start;
    float end;
    uint32_t agvID;
    ReservationType type;
};
```

처음에는 여기까지 하면 WHCA*의 핵심이 거의 끝났다고 생각했다.

하지만 가상 AGV 수를 늘리고 실제 상차·하차 작업을 붙이자 새로운 문제가 생겼다.

> 충돌 구간을 찾는 것과, 실제로 안전하게 예약을 확정하고 실행하는 것은 다른 문제였다.

이번 글에서는 TimeInterval Reservation 이후에 무엇이 더 필요했는지 정리하려고 한다.

## 1. 왜 고정 Time Slot이 불편했는가

논문이나 예제에서는 시간을 보통 다음처럼 정수 slot으로 표현한다.

```text
(Node 1, Time 0)
(Node 2, Time 1)
(Node 3, Time 2)
```

격자에서 모든 이동 시간이 동일하다면 이해하기 쉽다. 하지만 내 맵에서는 링크 길이가 모두 같지 않았고, 이동 시간도 소수점이 됐다.

```text
직선 Link A: 0.75초
긴 Link B: 1.40초
WAIT: 1.00초
상차 작업: 2.00초
```

이를 억지로 slot에 맞추면 두 가지 선택지가 생긴다.

1. 시간을 올림 처리해 필요 이상으로 통로를 오래 막는다.
2. 시간을 내림 처리해 실제로는 겹치는 이동을 안전하다고 판단할 수 있다.

그래서 예약을 `[start, end)` 구간으로 표현했다.

```cpp
bool Overlaps(float start, float end) const
{
    return !(end <= this->start || start >= this->end);
}
```

이제 `10.2초부터 11.6초까지`처럼 실제 이동 시간을 표현할 수 있었다.

## 2. Node, Edge, Goal은 서로 다른 예약이다

처음에는 모든 예약을 "어떤 공간을 누가 사용한다" 정도로 생각했다. 그러나 구현을 진행하면서 세 예약의 의미가 달랐다.

### Node Reservation

AGV가 노드에 머무르거나 통과하는 시간이다.

```text
Node 12
10.0s ~ 10.7s
AGV 3 사용
```

같은 시간에 두 AGV가 Node 12를 점유하지 못하게 한다.

### Edge Reservation

노드 사이 통로를 이동하는 시간이다.

```text
Node 12 ↔ Node 13
10.4s ~ 11.2s
AGV 3 사용
```

`12 → 13`과 `13 → 12`를 같은 물리 통로로 취급해야 정면 충돌을 막을 수 있다.

```cpp
uint64_t MakeEdgeKey(uint32_t from, uint32_t to)
{
    const uint32_t low = std::min(from, to);
    const uint32_t high = std::max(from, to);
    return (static_cast<uint64_t>(low) << 32) | high;
}
```

### Goal Reservation

목적지에 도착한 이후의 점유다.

AGV는 목표 노드에 도착하자마자 사라지지 않는다.

- 박스를 싣는다.
- 박스를 내린다.
- 다음 작업을 기다린다.
- HOME에서 idle 상태로 머문다.

즉, 목표점은 단순한 마지막 Node Reservation이 아니라 일정 시간 동안 보호해야 하는 공간이다.

```text
PICKUP / DROP: 작업 시간 + 안전 여유
HOME / NONE: 장기 점유
```

현재 프로젝트에서는 상·하차 목적지의 hold와 HOME 상태의 장기 hold를 다르게 관리한다.

## 3. WAIT는 실패가 아니라 행동이다

다중 AGV 경로 탐색에서 이동할 수 없는 순간이 생긴다.

이때 처음에는 "이웃 노드로 이동할 수 없으면 경로 탐색 실패"라고 생각하기 쉽다. 하지만 한 칸 기다린 뒤 이동하면 해결되는 경우가 많다.

그래서 PathFinder의 행동은 MOVE만이 아니다.

```text
MOVE: 이웃 노드로 이동
WAIT: 현재 노드에서 일정 시간 대기
```

WAIT도 하나의 정상적인 successor로 Open List에 넣었다.

```cpp
const float nextTime = current.departureTime + waitDuration;

if (reservationTable.IsNodeFree(
        current.nodeID,
        current.departureTime,
        nextTime + clearance,
        agvID))
{
    PushWaitSuccessor(current, nextTime);
}
```

여기서 한 번 더 문제가 생겼다.

WAIT 비용이 항상 1초라면, 혼잡한 상황에서 우회 경로보다 계속 기다리는 경로가 지나치게 싸질 수 있었다.

그렇다고 WAIT를 제거하면 우회가 없는 좁은 통로에서 경로 자체가 사라진다.

그래서 다음처럼 타협했다.

```text
짧은 WAIT:
  실제 대기 시간만 비용으로 사용

긴 연속 WAIT:
  추가 soft penalty 적용

WAIT 행동 자체:
  삭제하지 않음
```

현재 정책에서는 2초까지는 실제 시간 비용을 유지하고, 그 이후의 연속 WAIT에 더 큰 비용을 준다.

핵심은 기다림을 금지하는 것이 아니라, **가능한 우회가 있는데도 한곳에서 너무 오래 기다리는 경로를 덜 선호하게 하는 것**이다.

## 4. RRA*는 왜 따로 두었는가

WHCA*는 `(Node, Time)` 공간을 탐색한다. 여기에 목적지까지의 거리 계산까지 매번 처음부터 수행하면 AGV 수가 늘수록 같은 계산을 반복하게 된다.

그래서 목적지에서 역방향으로 탐색하는 RRA*를 별도로 두었다.

```text
일반 A*:
  현재 위치 → 목적지

RRA*:
  목적지 → 필요한 노드까지 역방향 탐색
  계산된 거리를 cache
```

PathFinder는 시간과 예약을 다루고, RRA*는 정적인 맵 거리 휴리스틱을 제공한다.

```cpp
neighbor.h = rraEngine.GetAbstractDistance(neighborID) / agvSpeed;
neighbor.f = neighbor.g + neighbor.h;
```

동일한 목적지로 여러 AGV가 이동할 때 이미 계산한 거리를 재사용할 수 있다.

## 5. 프로젝트의 WHCA*를 어떻게 설명해야 할까

이 부분은 포트폴리오와 기술 면접에서 정확하게 말해야 한다.

내 구현은 논문의 discrete time-slot WHCA*를 그대로 복제한 것은 아니다.

```text
WHCA*의 아이디어
  + (Node, Time) 탐색
  + 제한된 planning window
  + Reservation Table
  + WAIT 행동

프로젝트 환경에 맞춘 확장
  + float TimeInterval
  + Node / Edge / Goal Reservation
  + 실제 실행 점유 재검사
```

따라서 다음과 같이 설명하는 것이 가장 정확하다.

> WHCA*를 기반으로 하되 고정 time slot 대신 TimeInterval을 사용하고, Node·Edge·Goal Reservation과 실행 점유를 결합한 프로젝트 맞춤형 다중 AGV 경로 계획을 구현했다.

## 6. 결과

최종 AutomaticFleet 테스트에서는 다음 규모의 맵을 사용했다.

```text
Map: 204 Nodes / 644 Links
Virtual AGV: 28대
```

30초 headless smoke에서 관찰한 결과는 다음과 같다.

```text
28대 dispatch
Cargo LOADED 24회
Cargo UNLOADED 8회
Safe stop 0회
평균 계획 WAIT 2.11초
최대 계획 WAIT 9초
```

다만 이 결과는 특정 초기 배치와 scheduler 조건에서 수행한 smoke test다. 이것만으로 장시간 deadlock이나 livelock이 절대 발생하지 않는다고 증명할 수는 없다.

> [이미지 삽입] FactoryMap 전체에서 28대 AGV가 이동하는 장면

> [이미지 삽입] Node/Edge/Goal Reservation 비교 다이어그램

> [영상 링크] AutomaticFleet + Cargo demo

## 7. 느낀 점

TimeInterval Reservation을 처음 만들었을 때는 더 정밀한 시간 표현이 핵심이라고 생각했다.

하지만 실제로 AGV 수를 늘려 보니 더 중요한 질문이 생겼다.

```text
이 경로가 지금 계산 가능한가?
```

에서 끝나는 것이 아니라,

```text
이 경로 전체를 원자적으로 예약할 수 있는가?
계획 시점과 실행 시점의 점유가 달라지면 어떻게 할 것인가?
```

까지 생각해야 했다.

다음 글에서는 PathFinder가 찾은 후보를 바로 예약하면 왜 위험한지, 그리고 `ReservationTable`과 실제 `OccupancyProvider`를 왜 분리했는지 정리하려고 한다.

> 다음 글: Transactional Reservation과 실행 시점 Occupancy

