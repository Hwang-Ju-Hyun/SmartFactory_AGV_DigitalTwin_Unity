# [SmartFactory AGV] 실물 모터를 움직이기 전에 만든 것 — ESP32 안전 상태 머신

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: 28대 Virtual Fleet Dispatch와 Cargo State  
> 핵심 키워드: `ESP32`, `Arduino Framework`, `PlatformIO`, `TB6612FNG`, `Safety State Machine`

## 0. 들어가며

Unity에서 가상 AGV가 잘 움직인다고 해서 같은 경로를 그대로 실물 모터에 연결할 수는 없었다.

가상 AGV는 잘못된 명령을 받아도 화면 속 위치만 이상해진다. 실물 AGV는 잘못된 PWM, 통신 단절, encoder 배선 오류 하나로 책상에서 떨어지거나 모터를 계속 구동할 수 있다.

그래서 실제 주행 코드를 만들기 전에 먼저 다음 질문에 답해야 했다.

```text
언제 모터 출력을 허용할 것인가?
운전자가 준비되지 않았는데 경로가 오면 어떻게 할 것인가?
통신이 끊기면 어떤 출력 상태가 되어야 하는가?
한쪽 wheel만 움직이면 언제 fault로 볼 것인가?
완료했다고 보고하기 전에 무엇을 확인할 것인가?
```

이번 글에서는 ESP32 firmware의 motion algorithm보다 먼저 만든 안전 구조를 정리한다.

## 1. 개발 환경과 하드웨어

이 프로젝트에서 작성한 코드는 일반적인 의미의 "아두이노 스케치"라기보다, **ESP32용 C++ embedded firmware**다.

```text
MCU: ESP32-D0WD-V3 개발 보드
Framework: Arduino Framework
Build/Upload: PlatformIO
Motor Driver: TB6612FNG
Drive: DC Encoder Motor 2개, Differential Drive
Network: 2.4 GHz Wi-Fi + TCP
```

Arduino Framework의 `setup()`, `loop()`, GPIO, Wi-Fi API를 사용하지만, state machine, protocol parser, motion controller와 tests는 C++ 모듈로 분리했다.

## 2. 전원을 켠다고 바로 움직이지 않게 했다

처음 firmware가 boot된 상태는 `READY`가 아니라 `SAFE_LOCKED`다.

```text
전원 ON
  → PWM = 0
  → TB6612 STBY = LOW
  → TCP와 경로 실행 비활성
  → BOOT 승인 대기
```

실차 주행 profile에서는 운전자가 차체 위치와 방향을 확인한 뒤 BOOT 버튼을 눌러야 한다.

그 뒤에도 바로 모터가 켜지지 않는다.

```text
BOOT 누름
  → 5초 countdown
  → 운전자가 주변을 확인
  → countdown 완료
  → ARMED
```

countdown 중 BOOT를 다시 누르면 취소 또는 E-stop으로 처리한다.

이 5초는 단순한 delay가 아니다. 실제 장치가 곧 움직일 것임을 사용자에게 알려주는 상태 전이다.

## 3. 상태 머신을 명시했다

구조를 단순화하면 다음과 같다.

```text
SAFE_LOCKED
  └─ BOOT → COUNTDOWN

COUNTDOWN
  ├─ 5초 완료 → ARMED
  └─ 취소/E-stop → FAULT_LATCHED

ARMED
  └─ 유효한 trajectory → EXECUTING

EXECUTING
  ├─ target 도달 → SETTLING
  └─ fault/disconnect → FAULT_LATCHED

SETTLING
  ├─ 안정 정지 → NODE_WAIT
  └─ encoder 재움직임/timeout → FAULT_LATCHED

NODE_WAIT
  ├─ 다음 primitive → EXECUTING
  └─ 완료 조건 → ARRIVED

FAULT_LATCHED
  └─ reboot 전까지 출력 금지
```

코드에서도 상태를 enum으로 표현했다.

```cpp
enum class State : uint8_t
{
    SAFE_LOCKED,
    WAITING_FOR_ROUTE,
    EXECUTING,
    SETTLING,
    NODE_WAIT,
    ARRIVAL_PENDING,
    COMPLETE,
    FAULT_LATCHED
};
```

실제 enum 이름과 세부 상태는 모듈별로 조금씩 다르지만 핵심은 같다. 모터 출력, protocol report와 완료 조건을 하나의 bool로 대충 묶지 않고 명확한 상태 전이로 제한했다.

## 4. 왜 FAULT는 자동으로 풀지 않았는가

통신이 잠깐 끊겼다가 다시 연결되면 자동으로 주행을 재개하는 것이 편해 보일 수 있다.

하지만 그 사이에 사용자가 로봇을 들어 옮겼거나, 바퀴 배선이 빠졌거나, 장애물이 생겼을 수 있다.

그래서 fault가 발생하면 latch한다.

```cpp
void MotionController::LatchFault(Fault fault)
{
    SetPwm(0, 0);
    SetStandby(false);
    m_Fault = fault;
    m_State = State::FAULTED;
}
```

이후 packet이 다시 들어와도 reboot와 새로운 local approval 전에는 출력하지 않는다.

```text
Fault 발생
  → PWM 0/0
  → STBY LOW
  → Server에 ERROR 1회 보고
  → reboot required
```

## 5. 어떤 fault를 검사했는가

### Wrong Direction

forward 명령인데 encoder가 반대 방향으로 증가하면 motor polarity 또는 encoder normalization이 잘못된 것이다.

### Stall

PWM이 출력되고 있는데 일정 시간 encoder 변화가 없으면 바퀴가 걸렸거나 전원이 부족한 상태일 수 있다.

### Wheel Mismatch

좌우 진행률 차이가 허용 범위를 크게 벗어나면 한쪽 motor, encoder 또는 배선 문제일 수 있다.

### Overrun

target을 지나서 count가 계속 증가하면 정지 제어가 실패한 것이다.

### Timeout

정해진 시간 안에 primitive가 끝나지 않으면 계속 모터를 구동하지 않고 정지한다.

### TCP Disconnect

Server와 연결이 끊기면 마지막 명령을 계속 수행하지 않는다.

### Output Invariant

안전 상태에서는 반드시 `PWM=0/0`, `STBY=LOW`여야 한다. state와 실제 output이 맞지 않으면 fault로 본다.

## 6. target 도달과 ARRIVED는 같은 것이 아니다

Encoder count가 target에 닿는 순간 바로 `ARRIVED`를 보내면 관성으로 바퀴가 더 움직일 수 있다.

그래서 완료 흐름을 분리했다.

```text
Target count 도달
  → 해당 wheel PWM 0
  → 양쪽 target 확인
  → settling 구간
  → 일정 시간 encoder가 안정적으로 멈춤
  → 현재 node/route/command 확인
  → ARRIVED 전송
```

현재 settling은 encoder가 목표 이상이며 최소 150 ms 동안 안정적으로 정지했는지 확인한다. settling이 2초 안에 끝나지 않으면 완료로 속이지 않고 fault로 처리한다.

즉, `움직임이 대충 끝났다`와 `Server에 도착을 확정해도 된다`를 분리했다.

## 7. Build Profile로 위험도를 분리했다

실물 장치에서는 잘못된 environment를 upload하는 것 자체가 위험할 수 있다.

그래서 `platformio.ini`에 14개 profile을 두었다.

```text
default / locked
trajectory preview / trace
raised-wheel
physical-fleet locked / live
straight calibration locked / live
turn calibration locked / CW / CCW
channel diagnostic locked / A / B
```

예를 들어 channel diagnostic은 network와 전체 route state machine을 제거하고, BOOT + 5초 뒤 선택한 TB6612 channel 하나만 300 ms 구동한다.

```text
Diagnostic A:
  Channel A만 300 ms
  기대 encoder = LEFT

Diagnostic B:
  Channel B만 300 ms
  기대 encoder = RIGHT
```

실제 시험에서 다음 mapping을 확인했다.

```text
Channel A → LEFT wheel / LEFT encoder
Channel B → RIGHT wheel / RIGHT encoder
```

이렇게 하면 전체 Server 경로를 실행하지 않고도 배선과 software mapping을 분리해서 볼 수 있다.

## 8. 실제 시험은 단계적으로 올렸다

실물 주행은 다음 순서로 진행했다.

```text
1. Motor locked build
2. Host state test
3. Network 없이 encoder 손회전 확인
4. 바퀴를 바닥에서 띄운 channel pulse
5. Raised-wheel route
6. 짧은 low-speed floor run
7. Server + Vision + Unity integration
```

중간 단계에서 문제가 나면 그보다 높은 단계로 가지 않았다.

예를 들어 channel mapping이 불확실한 상태에서 자동 route를 돌리는 대신 A/B 300 ms profile로 원인을 좁혔다.

## 9. 검증 결과

현재 firmware 저장소에서 확인한 결과는 다음과 같다.

```text
Host tests: 5/5 PASS
PlatformIO profiles: 14개 build
Channel A/B isolated test: expected mapping 확인
CCW 90° one-shot: normalized L/R count 일치, safe output 종료
Physical route: STATUS / progress / ARRIVED 흐름 확인
```

> [이미지 삽입] ESP32, TB6612, encoder와 battery wiring top view

> [이미지 삽입] PlatformIO profile 목록

> [이미지 삽입] BOOT countdown과 `[SAFE] PWM=0/0 STBY=LOW` 로그

## 10. 느낀 점

처음에는 실물 AGV 구현의 핵심이 PID나 PWM이라고 생각했다.

하지만 실제로 가장 먼저 필요했던 것은 "언제 움직이면 안 되는가"를 코드로 만드는 일이었다.

```text
경로가 와도 승인 전에는 움직이지 않는다.
fault가 나면 자동 복구하지 않는다.
encoder target에 닿아도 바로 ARRIVED를 보내지 않는다.
통신이 끊기면 마지막 명령을 계속 수행하지 않는다.
```

이 구조가 있어야 이후 직진 보정이나 회전 calibration을 시도해도 실패가 통제 가능한 범위에 머문다.

다음 글에서는 안전 구조 위에서 실제 직진과 회전을 맞추며 겪은 문제를 다룬다. 특히 좌우 encoder count가 비슷한데도 로봇이 대각선으로 흐른 이유를 정리하려고 한다.

> 다음 글: 엔코더가 같은데 왜 대각선으로 갈까?

