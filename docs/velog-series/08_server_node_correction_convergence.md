# [SmartFactory AGV] 위치를 맞추려 했는데 계속 돌기만 했다 — Node Correction 수렴 문제

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: Vision Quality Contract와 Unity Ghost  
> 핵심 키워드: `Node Correction`, `Hysteresis`, `Convergence`, `Point Turn`, `Physical Slip`

## 0. 들어가며

실물 AGV가 Server 경로의 한 edge를 끝내면 encoder 기준으로 `ARRIVED`를 보낸다.

하지만 wheel encoder만으로 계산한 도착점은 실제 node 중심과 다를 수 있다. 그래서 overhead Vision pose를 이용해 node 단위 보정을 추가했다.

처음 만든 정책은 단순했다.

```text
목표점과 멀다
  → 목표점을 바라보도록 회전
  → 앞으로 이동

위치는 맞지만 heading이 틀리다
  → 최종 heading으로 회전

모두 허용 범위다
  → 완료
```

논리적으로는 맞아 보였다.

하지만 실제 로봇은 노드 근처에서 몇 번씩 제자리 회전을 반복했고, 결국 correction primitive 한도를 소진해 멈췄다.

## 1. 첫 번째 정책

현재 pose를 `(x, z, heading)`이라고 하고 목표 node를 `(targetX, targetZ, targetHeading)`이라고 하자.

```cpp
const float dx = targetX - x;
const float dz = targetZ - z;
const float positionError = std::hypot(dx, dz);
const float targetBearing = std::atan2(dz, dx);
const float bearingError = Normalize(targetBearing - heading);
const float finalHeadingError = Normalize(targetHeading - heading);
```

초기 정책은 대략 다음과 같았다.

```text
positionError > 20 mm
  ├─ |bearingError| > 10° → point turn
  └─ 아니면 → forward drive

positionError <= 20 mm
  ├─ |finalHeadingError| > 10° → point turn
  └─ 아니면 → complete
```

## 2. 실제로 반복 회전한 상황

한 시험에서 Node 2의 목표는 대략 다음과 같았다.

```text
Target: (350 mm, 0 mm, 0°)
Vision: (310.4 mm, -18.5 mm, -4.5°)
```

계산하면 다음과 같다.

```text
Position error: 약 43.7 mm
Final heading error: 약 4.5°
Target bearing error: 약 29.5°
```

최종 heading은 이미 허용 범위에 가깝다. 하지만 위치 오차가 20 mm보다 크기 때문에 Server는 target bearing을 보고 약 29.5° CCW point turn을 보냈다.

문제는 실제 differential-drive의 제자리 회전 중심이 완벽하게 고정되지 않는다는 것이다.

```text
회전 전:
  목표점이 왼쪽 앞

point turn:
  바닥 slip으로 중심도 함께 이동

회전 후:
  목표점 방위각이 다시 달라짐
```

Server는 새 pose를 받고 다시 target bearing을 계산했다. 회전이 위치 오차를 줄이기는커녕 새로운 각도 오차를 만들면서 다음 loop가 발생했다.

```text
회전
  → 재측정
  → 목표 방위각 변경
  → 다시 회전
  → 재측정
  → 다시 회전
```

## 3. primitive 개수 제한만으로는 해결되지 않았다

처음에는 무한 루프를 막기 위해 최대 correction primitive 수를 두었다.

```text
Maximum correction primitives = 8
```

안전장치로는 필요하다. 하지만 정확도를 개선하지는 않는다.

```text
기존:
  8번 돌고 멈춤

limit를 16으로 증가:
  16번 돌고 멈출 가능성
```

문제는 횟수가 아니라 보정이 실제로 수렴하는지 확인하지 않은 것이었다.

## 4. 세 종류의 heading을 분리했다

이 문제에서 가장 중요한 수정은 heading 의미를 분리한 것이다.

### Target Bearing

현재 위치에서 목표 좌표를 바라보는 방향이다. 위치 접근 단계에서만 필요하다.

### Arrival Heading

현재 edge를 완료했을 때 node에서 요구하는 최종 자세다.

### Departure Heading

다음 edge를 시작하기 위해 정렬해야 하는 방향이다.

초기 정책은 이 값들을 같은 "heading error"처럼 다루는 순간이 있었다.

현재는 다음 단계로 나눴다.

```text
APPROACH_POSITION
  → 목표 좌표 접근

ALIGN_FINAL_HEADING
  → node 도착 자세 정렬

PRE_DEPARTURE
  → 다음 edge 방향 정렬
```

현재 단계에서 필요한 heading만 사용한다.

## 5. Hysteresis를 추가했다

측정값이 threshold 주변에서 흔들리면 상태가 매 frame 바뀔 수 있다.

```text
19.8 mm → complete
20.3 mm → correction
19.7 mm → complete
20.1 mm → correction
```

그래서 진입 threshold와 유지/완료 threshold를 다르게 두었다.

```text
Correction 시작 기준 > 큰 threshold
Correction 종료 기준 < 작은 threshold
```

이렇게 하면 noise 때문에 상태가 경계에서 왕복하는 것을 줄일 수 있다.

## 6. 수렴을 숫자로 검사했다

이전 pose와 현재 pose를 저장해 실제 오차가 줄었는지 비교했다.

```cpp
struct CorrectionHistory
{
    float previousPositionError;
    float previousHeadingError;
    TurnDirection previousDirection;
    uint32_t repeatedSameDirection;
    float cumulativeTurnRad;
};
```

다음 상황은 비수렴 후보로 본다.

- 같은 방향 회전이 연속 반복됨
- position error가 의미 있게 줄지 않음
- heading error가 줄지 않음
- 누적 회전량이 안전 한도를 넘음
- primitive 수가 한도를 넘음

```text
명령을 수행했다
```

가 아니라,

```text
명령 뒤 실제 측정 오차가 감소했다
```

를 성공 판단에 포함했다.

## 7. ESP32의 작은 회전도 따로 다뤘다

Server 정책만 수정해도 실제 한 번의 correction turn이 과도하면 다시 오차가 커진다.

ESP32에서는 경로의 90° 회전과 작은 correction turn을 구분했다.

```text
90° CW: 163 counts
90° CCW: 159 counts

Correction coast:
  CW 14 counts
  CCW 12 counts
```

작은 회전에서는 정지 마찰과 coast 비율이 커지므로 correction 전용 저속 profile과 제한된 coast compensation을 적용했다.

## 8. 개선 결과

개선 전 영상에서는 목표점 근처에서 큰 회전 뒤 추가 회전이 반복되는 장면이 뚜렷했다.

개선 후보 2 영상에서는 다음 흐름을 확인했다.

```text
Node 6 도착
  → correction primitive 0회로 승인
  → 다음 edge departure heading 정렬 1회
  → 경로 계속 수행
```

대표 frame의 위치 오차도 개선 전 약 74 mm, 개선 후보 약 12 mm로 관찰됐다. 다만 동일 조건의 정식 A/B는 아니므로 참고 관찰값으로만 사용한다.

> [영상 삽입] 개선 전 반복 point turn

> [영상 삽입] Candidate 2의 Node 6 zero-correction 승인

> [이미지 삽입] position/bearing/final/departure heading 비교 그림

## 9. 왜 Vision으로 주행 중 계속 조향하지 않았는가

이 문제를 겪고 나면 Vision pose로 매 frame steering하면 더 정확하지 않을까 생각할 수 있다.

하지만 현재 카메라는 다음 영향을 받는다.

- 사람이나 차체에 의한 tag 가림
- 회전 중 motion blur
- FOV 가장자리의 검출 불안정
- intrinsic 미적용 상태의 lens distortion
- heading noise와 처리 지연

이 값을 바로 PWM에 연결하면 HELD pose나 순간 noise를 추격하는 진동이 생길 수 있다.

그래서 현재 구조는 다음 절충을 사용한다.

```text
주행 중:
  ESP32 encoder local control

Node 경계:
  fresh Vision measurement로 correction
```

향후 더 안정적인 frame rate, intrinsic calibration과 filtering이 확보되면 low-rate heading fusion을 추가할 수 있다.

## 10. 느낀 점

보정 기능은 항상 오차를 줄일 것이라고 생각하기 쉽다.

하지만 실제 시스템에서는 보정 동작 자체가 새로운 오차를 만든다.

```text
측정 오차
+ 회전 slip
+ 정지 coast
+ 다음 측정의 지연
```

따라서 중요한 것은 correction command를 많이 보내는 것이 아니라, **보정 전후의 실제 상태가 수렴하는지 확인하는 것**이었다.

다음 글에서는 첫 edge가 시작되기도 전에 Server가 SAFE STOP한 또 다른 사례를 다룬다. 원인은 좌표나 모터가 아니라 `routeID`가 유효해지는 시점이었다.

> 다음 글: 최초 routeID 0과 Protocol Lifecycle

