# [SmartFactory AGV] 28대가 움직이는 것에서 끝내지 않기 — 작업 배정과 Cargo State 복제

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: Transactional Reservation과 실행 점유  
> 핵심 키워드: `TaskManager`, `DispatchManager`, `Cargo`, `Snapshot`, `Unity`

## 0. 들어가며

WHCA* 경로 계획이 동작하면 여러 AGV가 충돌을 피하며 움직이는 화면을 만들 수 있다.

하지만 AGV가 목적 없이 맵을 계속 순환하면 물류 시스템이라기보다 pathfinding demo에 가깝다.

그래서 다음 상태를 실제 event로 연결하고 싶었다.

```text
작업 배정
  → Pickup 지점 이동
  → 상차 완료
  → Drop 지점 이동
  → 하차 완료
  → 다음 작업
```

Unity에서도 단순히 로그에 `PICKUP_COMPLETED`라고 표시하는 것이 아니라, AGV 위에 박스가 올라가고 하차 시 사라지게 만들었다.

이번 글은 28대 가상 AGV를 물류 작업처럼 보이게 만들기 위해 Server와 Unity의 상태를 어떻게 연결했는지에 대한 기록이다.

## 1. 처음에는 무작위 목적지를 선택했다

가장 단순한 dispatch는 다음과 같다.

```cpp
uint32_t pickupNode = RandomPickupNode();
uint32_t dropNode = RandomDropNode();
AssignTask(agvID, pickupNode, dropNode);
```

AGV 수가 적을 때는 잘 움직였다.

하지만 20대 이상을 동시에 시작하자 많은 AGV가 비슷한 순간에 긴 경로를 요청하고, 같은 상차 구역으로 몰렸다.

```text
동일 frame:
  AGV 1 IDLE_READY
  AGV 2 IDLE_READY
  AGV 3 IDLE_READY
  ...
  AGV 28 IDLE_READY
```

이 상태에서 무작위 목적지를 고르면 다음 문제가 생겼다.

- 시작 직후 PathFinder 요청이 한꺼번에 몰린다.
- 가까운 빈 pickup이 있어도 먼 목적지가 선택된다.
- 예상 도착 시각에 이미 다른 AGV가 점유할 장소로 향한다.
- 많은 AGV가 같은 병목을 통과하는 경로를 만든다.

## 2. 최초 dispatch를 시간차로 분산했다

첫 번째 개선은 단순했다.

28대의 첫 작업을 같은 순간에 요청하지 않고 0.2초 간격으로 분산했다.

```text
AGV 1: 0.0s
AGV 2: 0.2s
AGV 3: 0.4s
...
```

이 방식만으로 알고리즘 자체가 바뀌는 것은 아니다. 하지만 초기 reservation state가 조금씩 확정되면서 다음 AGV가 앞선 계획을 보고 경로를 만들 수 있게 됐다.

동시에 발생한 burst를 줄여 Server 부하와 시작 구간 병목도 완화했다.

## 3. 가장 가까운 목적지만 봐도 부족했다

다음에는 현재 위치에서 가까운 pickup을 선택했다.

```text
distance(current, pickup)
```

그러나 현재 비어 있다는 이유만으로 그 목적지가 좋은 것은 아니었다.

AGV가 도착하는 시각에는 다른 AGV가 상차 중일 수 있다.

그래서 후보를 다음 두 기준으로 봤다.

1. 현재 위치에서의 거리
2. 예상 도착 시각에 goal reservation이 가능한가

```cpp
for (const auto nodeID : pickupCandidates)
{
    const float eta = EstimateArrivalTime(agvID, nodeID);

    if (reservationTable.IsGoalAvailable(nodeID, eta, pickupHold))
    {
        available.push_back({nodeID, DistanceTo(nodeID)});
    }
}

SortByDistance(available);
return SelectFromNearCandidates(available);
```

완전히 하나의 최단 후보만 고정하면 다시 같은 장소로 몰릴 수 있어, 가까운 후보 집합 안에서 분산 선택할 수 있게 했다.

## 4. Cargo는 Unity 효과가 아니라 Server 상태다

AGV 위에 박스를 보이게 만드는 가장 쉬운 방법은 Unity에서 특정 노드 도착을 감지해 prefab을 생성하는 것이다.

하지만 그렇게 하면 Unity가 작업 상태를 추론하게 된다.

```text
Unity 추론:
  이 노드에 도착했으니 아마 상차했을 것
```

이 구조는 재접속이나 packet 순서가 달라지면 쉽게 틀어진다.

Server가 실제 `PICKUP_COMPLETED` 또는 `DROP_COMPLETED` event를 확인한 뒤 cargo 상태를 결정하도록 했다.

```text
PICKUP_COMPLETED
  → Server cargo = LOADED
  → Unity에 상태 전송

DROP_COMPLETED
  → Server cargo = UNLOADED
  → Unity에 상태 전송
```

## 5. Cargo packet에 무엇을 넣었는가

Cargo packet에는 화면에 박스를 생성하라는 단순 명령만 넣지 않았다.

```text
agvID
taskID
cargoID
nodeID
sequence
state = LOADED / UNLOADED
```

각 값이 필요한 이유는 다음과 같다.

- `agvID`: 어느 AGV 위의 cargo인가
- `taskID`: 이전 작업의 늦은 packet과 구분
- `cargoID`: 어떤 박스인가
- `nodeID`: 어느 작업 위치에서 상태가 바뀌었는가
- `sequence`: 중복·역순 packet 거부
- `state`: 현재 상태 자체

Unity의 GameObject 이름을 Server packet에 넣지는 않았다. Server는 물류 의미만 전달하고 어떤 prefab을 사용할지는 Unity 설정이 결정한다.

## 6. Event만 보내면 재접속에서 사라진다

처음에는 상태가 바뀔 때만 event를 보내면 충분해 보였다.

```text
10:00 LOADED packet 전송
10:01 Unity 접속
```

Unity가 10:01에 접속했다면 10:00의 event를 받지 못한다. 실제 Server 상태에서는 AGV가 박스를 싣고 있지만 Unity에는 아무것도 보이지 않는다.

그래서 Server가 AGV별 현재 cargo snapshot을 유지하게 했다.

```text
실시간 상태 변경:
  event 전송
  snapshot 갱신

Unity 신규 연결/재연결:
  현재 snapshot 전송
```

이제 늦게 접속해도 현재 상태를 복원할 수 있다.

## 7. Unity에서도 packet 순서를 믿지 않았다

TCP는 연결 안에서 byte 순서를 보장하지만, application lifecycle까지 자동으로 보장하지는 않는다.

예를 들어 Unity에서 AGV GameObject가 만들어지기 전에 cargo snapshot을 처리할 수 있다.

```text
Cargo LOADED packet 도착
AGV CREATE 처리 아직 안 됨
```

이때 packet을 버리면 박스는 끝까지 나타나지 않는다.

그래서 pending cargo state를 저장했다.

```csharp
if (!agvObjects.TryGetValue(packet.AgvId, out var agv))
{
    pendingCargo[packet.AgvId] = packet;
    return;
}

ApplyCargoState(agv, packet);
```

AGV가 생성되면 pending 상태를 적용한다.

또한 AGV별 마지막 sequence와 현재 task/cargo를 확인해 중복 또는 오래된 packet이 새 상태를 되돌리지 않게 했다.

## 8. Unity에서 상차와 하차를 표현했다

상차 애니메이션을 복잡하게 만들지는 않았다.

이번 프로젝트에서 보여주고 싶은 핵심은 animation이 아니라 Server 상태가 Unity object와 일치하는지였다.

```text
LOADED:
  Cargo prefab 생성
  AGV의 CargoMount 자식으로 이동
  local position/rotation 적용

UNLOADED:
  taskID와 cargoID가 일치하는 instance만 제거
```

즉 박스는 부드럽게 날아오르지 않고 바로 AGV 위에 올라간다. 하지만 AGV가 이동할 때 함께 움직이고, 하차 event가 오면 정확한 박스만 사라진다.

## 9. 테스트 결과

현재 AutomaticFleet의 맵과 smoke 조건은 다음과 같다.

```text
Map: 204 Nodes / 644 Links
Virtual AGV: 28대
Headless smoke: 30초
```

해당 실행에서 다음을 관찰했다.

```text
28대 dispatch
LOADED 24회
UNLOADED 8회
Safe stop 0회
WAIT path 75개
평균 계획 WAIT 2.11초
최대 계획 WAIT 9초
5초 초과 WAIT 경로 3개
```

이 결과는 특정 실행의 smoke 지표다. 장시간 교착 부재를 증명하는 benchmark는 아니다.

> [이미지 삽입] 28대 AGV가 보이는 FactoryMap 전체 화면

> [이미지 삽입] 상차 전 / AGV 위 상차 / 하차 후 3단 비교

> [이미지 삽입] Server Cargo LOADED/UNLOADED 로그와 Unity 화면

## 10. 느낀 점

처음에는 "AGV가 목적지에 도착하면 Unity에서 박스를 하나 만들면 된다"고 생각했다.

하지만 네트워크 시스템에서는 화면 효과 하나에도 상태 소유권을 정해야 했다.

```text
누가 상차 완료를 결정하는가?
중복 packet은 어떻게 처리하는가?
Unity가 늦게 접속하면 현재 상태를 어떻게 복원하는가?
이전 task의 packet이 늦게 오면 어떻게 막는가?
```

결국 cargo는 prefab 문제가 아니라 distributed state 문제였다.

다음 글부터는 가상 fleet를 벗어나 실제 ESP32 AGV를 다룬다. 모터를 한 번 움직이는 것보다 먼저 만들었던 BOOT 승인, countdown, E-stop과 fault latch 구조를 정리하려고 한다.

> 다음 글: ESP32 Physical AGV의 안전 상태 머신

