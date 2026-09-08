# [SmartFactory AGV] 엔코더 값은 같은데 왜 로봇은 대각선으로 갈까?

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: ESP32 Physical AGV의 안전 상태 머신  
> 핵심 키워드: `Encoder`, `Differential Drive`, `PWM`, `Slip`, `Calibration`

## 0. 들어가며

실물 AGV에서 가장 오래 붙잡고 있었던 문제 중 하나는 직진이었다.

처음에는 당연히 이렇게 생각했다.

```text
왼쪽 encoder count == 오른쪽 encoder count
  → 양쪽 바퀴가 같은 거리 이동
  → 로봇은 직진
```

하지만 실제 바닥에서는 좌우 count가 거의 같아도 로봇이 수평이 아니라 대각선으로 흘렀다. 특히 제자리 회전 뒤에 직진할 때 편차가 더 크게 보였다.

처음에는 software의 좌우 wheel mapping이나 목표 count가 틀렸다고 의심했다. 로그를 더 모으면서 이 문제는 **encoder control과 물리적인 lateral slip을 분리해서 봐야 하는 문제**라는 것을 알게 됐다.

## 1. 먼저 wheel과 encoder mapping부터 확인했다

한쪽만 덜 가는 것처럼 보일 때 동시에 여러 가지를 의심할 수 있다.

- TB6612 channel A/B가 어느 wheel인지
- encoder L/R label이 맞는지
- motor polarity가 맞는지
- encoder sign normalization이 맞는지
- 실제 PWM bias가 있는지

전체 경로를 움직이면 이 원인들이 섞인다.

그래서 network 없이 한 channel만 300 ms 구동하는 diagnostic firmware를 따로 만들었다.

실제 결과는 다음과 같았다.

```text
[DIAG_RESULT] channel=A expectedWheel=LEFT  L=31 R=0
[DIAG_RESULT] channel=B expectedWheel=RIGHT L=0  R=22
```

이를 통해 현재 mapping은 다음과 같이 고정할 수 있었다.

```text
TB6612 A → LEFT motor → LEFT encoder
TB6612 B → RIGHT motor → RIGHT encoder
```

Mapping이 확인된 뒤에야 직진 제어를 분석했다.

## 2. Differential Drive의 기본 계산

현재 하드웨어의 nominal 값은 다음과 같다.

```text
Wheel diameter: 48 mm
Track width: 130 mm
Encoder: 260 counts/revolution
```

이론적인 1 count당 이동 거리는 다음과 같다.

```text
wheel circumference = π × 48 mm
distance/count = wheel circumference / 260
```

하지만 nominal 계산만으로 실제 바닥 이동 거리를 맞출 수는 없었다.

- 타이어가 눌린다.
- 바닥에서 미끄러진다.
- encoder의 effective count와 nominal 값이 다를 수 있다.
- motor 정지 후 관성으로 더 이동한다.

그래서 실제 Vision endpoint로 350 mm 이동 결과를 측정해 counts/mm를 다시 맞췄다.

현재 값은 다음과 같다.

```cpp
static constexpr float kForwardCountsPerMm = 572.0f / 350.0f;
```

즉, 350 mm fleet edge에 좌우 공통 572 counts를 사용한다.

이전 607-count 요청에서 실제 평균 이동이 약 371.2 mm로 관찰됐기 때문에 공통 scale을 줄였다.

## 3. 좌우 최종 target을 다르게 주는 시도

로봇이 한쪽으로 흐르자 처음에는 한쪽 wheel target을 줄이거나 늘리는 방법을 생각했다.

```text
LEFT target = 1.00
RIGHT target = 0.97
```

특정 바닥과 배터리 상태에서는 좋아 보일 수 있다.

하지만 이 방법은 여러 문제가 있었다.

- 실제로는 양쪽 wheel이 다른 최종 거리를 이동한다.
- 하중과 배터리가 바뀌면 bias 방향이 달라질 수 있다.
- encoder가 정확히 target에 도달해도 의도적으로 회전한 결과가 된다.
- 물리적 slip을 고정 숫자로 숨길 수 있다.

그래서 최종 target은 다시 동일하게 맞췄다.

```cpp
kForwardLeftTargetScale = 1.0f;
kForwardRightTargetScale = 1.0f;
```

좌우 차이는 목표 거리에서 만들지 않고 주행 중 PWM synchronization으로 다루기로 했다.

## 4. 누적 count 차이만 보면 늦다

가장 단순한 동기화는 누적 count 차이를 보는 것이다.

```cpp
const int32_t error = leftCount - rightCount;

pwmLeft  -= kp * error;
pwmRight += kp * error;
```

한 wheel이 계속 앞서면 반대쪽을 따라잡게 만들 수 있다.

하지만 짧은 구간에서 속도가 달라도 누적 count가 우연히 비슷할 수 있다.

```text
왼쪽: 처음 빠르고 나중에 느림
오른쪽: 처음 느리고 나중에 빠름

현재 누적 count는 같음
하지만 순간 wheel speed는 다름
```

그래서 일정 interval의 count 변화량도 함께 봤다.

```cpp
const int32_t deltaLeft = leftCount - previousLeft;
const int32_t deltaRight = rightCount - previousRight;

const int32_t cumulativeError = leftCount - rightCount;
const int32_t rateError = deltaLeft - deltaRight;

const float correction =
    kp * cumulativeError + kd * rateError;
```

이 방식으로 장기적인 거리 차이와 순간 속도 차이를 함께 보정했다.

## 5. 그런데 encoder가 같아도 대각선으로 갔다

이 부분이 핵심이었다.

Encoder는 motor shaft 또는 wheel의 회전을 측정한다. 로봇 중심이 바닥에서 실제로 어디로 이동했는지는 직접 측정하지 않는다.

다음 상황에서도 encoder count는 정상일 수 있다.

- 한쪽 wheel이 조금 미끄러짐
- caster가 회전 직후 비스듬히 놓임
- 차체 하중이 오른쪽에 더 집중됨
- 바닥 마찰이 좌우에서 다름
- wheel과 축의 기계적 정렬이 다름

내 차체에서는 ESP32와 부품 무게가 우측에 더 집중돼 있었다.

즉, 실제 데이터는 다음처럼 될 수 있다.

```text
Encoder:
  L = 572
  R = 572

Ground displacement:
  왼쪽 wheel은 거의 구름
  오른쪽 wheel은 일부 slip 또는 다른 effective radius

Robot path:
  대각선
```

이것은 encoder controller가 count를 못 맞춘 문제와 다르다.

> 엔코더가 같은 것은 두 wheel의 회전량이 같다는 뜻이지, 차체가 세계 좌표에서 완벽한 직선을 그렸다는 뜻은 아니다.

## 6. 하드웨어 조건도 수정했다

Software만 계속 조정하면 물리 문제를 수치로 덮을 수 있다.

그래서 먼저 차체의 하중을 재배치했다.

```text
Before:
  ESP32와 부품이 우측에 집중

After:
  배터리와 보드를 가능한 한 중앙/좌우 균형 배치
```

그리고 다음을 함께 적용했다.

- 좌우 공통 target 사용
- 좌우 baseline PWM 정렬
- cumulative + interval encoder synchronization
- 실제 바닥 endpoint 기반 counts/mm 재보정
- 회전 뒤 안전 pause와 settling 유지

이후 주행은 이전보다 안정적으로 보였다.

## 7. CW와 CCW도 같은 숫자를 쓰지 않았다

처음에는 90° 회전에 좌우 방향 모두 176 counts를 사용했다.

하지만 실제 Vision 측정에서는 방향별 over-rotation이 달랐다.

그래서 현재는 다음처럼 분리했다.

```cpp
static constexpr int32_t kTurn90CwCount = 163;
static constexpr int32_t kTurn90CcwCount = 159;
```

또한 작은 correction turn에서는 encoder target에서 PWM을 끈 뒤에도 관성으로 더 회전했다.

관찰된 coast는 대략 다음과 같았다.

```text
CW: 14 counts
CCW: 12 counts
```

이를 correction turn에만 적용하고, 작은 명령 전체가 사라지지 않도록 최대 60%까지만 차감했다.

경로의 큰 회전과 작은 Vision correction을 같은 profile로 취급하지 않은 것이다.

## 8. Before와 After

개선 전 영상의 대표 구간에서는 목표점까지 약 74 mm 오차가 남았고, Server가 약 90° 회전 후 추가 correction을 반복했다.

개선 후보 영상에서는 Node 6이 correction 0회로 승인됐고, 다른 대표 구간의 endpoint 오차는 약 12 mm로 관찰됐다.

다만 두 값은 동일한 시작 위치·배터리·경로로 반복한 정식 A/B 실험이 아니다.

따라서 다음처럼 표현하는 것이 정확하다.

> 서로 다른 주행 영상의 대표 frame을 비교했을 때 약 74 mm에서 약 12 mm 수준으로 개선된 구간을 관찰했다.

> [영상 삽입] 개선 전 `어려웠던거1.mp4`

> [영상 삽입] 개선 대표 `(후보2)녹화_2026_09_04_13_19_54_459.mp4`

> [이미지 삽입] 좌우 encoder delta와 PWM correction graph

## 9. 남아 있는 한계

현재 구조는 encoder 기반 local motion과 node-level Vision correction을 사용한다.

따라서 다음 상황을 완전히 해결하지는 못한다.

- 주행 중 lateral slip 실시간 관측
- 바닥 재질이 달라질 때 자동 feed-forward 보정
- caster 방향에 의한 순간 heading 변화
- 배터리 전압 변화에 따른 motor 응답 차이

더 높은 정확도가 필요하다면 다음 단계가 필요하다.

```text
IMU heading
wheel별 feed-forward characterization
Vision/odometry fusion
주행 중 저주기 heading correction
기구 정렬과 weight distribution 개선
```

## 10. 느낀 점

이 문제를 통해 sensor가 무엇을 측정하고 무엇을 측정하지 못하는지 구분해야 한다는 것을 배웠다.

처음에는 모든 직진 오차를 encoder controller의 문제로 봤다. 실제로는 다음 세 가지가 섞여 있었다.

```text
Software mapping
Closed-loop wheel synchronization
Physical lateral slip
```

각 원인을 격리하지 않고 PWM 숫자만 계속 바꿨다면 특정 시험 한 번만 잘 되는 코드가 되었을 것이다.

다음 글에서는 바퀴가 아니라 overhead camera를 다룬다. AprilTag의 중심 좌표가 왜 로봇의 실제 위치가 아니었는지, homography와 body-frame offset을 어떻게 적용했는지 정리한다.

> 다음 글: AprilTag 중심에서 바퀴축 중심 pose 구하기

