# [SmartFactory AGV] WHCA* 시뮬레이션에서 실물 ESP32 AGV까지 — 프로젝트 회고

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: ESP32·WSL·Vision·Unity Cross-System Debugging  
> 핵심 키워드: `Digital Twin`, `WHCA*`, `ESP32`, `AprilTag`, `Unity`, `Retrospective`

## 0. 들어가며

이 프로젝트의 시작은 다중 AGV 경로 탐색이었다.

```text
여러 AGV가 같은 창고 안에서
서로 충돌하지 않고 작업하려면 어떻게 해야 할까?
```

Cooperative A*를 공부하고 Reservation Table을 만들었고, 이후 WHCA*와 RRA*, TimeInterval 기반 Node/Edge/Goal Reservation으로 확장했다.

여기까지만 해도 하나의 pathfinding simulation이 될 수 있었다.

하지만 최종 목표는 화면 속 AGV만 움직이는 것이 아니었다.

```text
C++ Server가 만든 경로를
실제 ESP32 AGV가 수행하고,
카메라가 측정한 실물 위치를
Unity Digital Twin에서 함께 보고 싶었다.
```

결국 프로젝트는 네 시스템으로 커졌다.

```text
ESP32 Physical AGV
        ↕
WSL C++ Server
        ↕
Python Vision
        ↕
Unity Digital Twin
```

이번 글에서는 전체 구조, 구현 결과, 어려웠던 점과 남은 한계를 한 번에 정리한다.

## 1. 프로젝트의 핵심 목표

한 줄로 정리하면 다음과 같다.

> 하나의 C++ Server가 가상 AGV와 ESP32 실물 AGV를 같은 작업·경로 모델로 관리하고, AprilTag로 측정한 물리 pose를 Unity Digital Twin에 연결한다.

여기서 중요한 것은 Unity와 ESP32가 똑같이 동작한다는 뜻이 아니다.

```text
Virtual AGV:
  Server 경로를 MovementSimulator가 실행

Physical AGV:
  Server 경로를 ESP32 motor/encoder가 실행
```

실행 방식은 다르지만 작업, 경로와 예약을 결정하는 source of truth는 Server다.

## 2. 전체 아키텍처

```text
TaskManager
  ↓
DispatchManager
  ↓
RoutePlanner
  ↓
PathFinder + RRA* + ReservationTable
  ↓
IRobotController
  ├─ UnityRobotController / Virtual AGV
  └─ ESP32RobotController / RobotSession / Physical AGV

Vision → Server → Unity Vision Ghost
Server Cargo State → Unity CargoMount
```

시스템별 책임은 다음처럼 나눴다.

### C++ Server

- world state
- 작업 배정
- WHCA* 기반 경로 계획
- Node/Edge/Goal 시간 예약
- 실행 점유 검사와 재계획
- 실차 node correction 정책
- Unity map/AGV/cargo replication

### ESP32

- trajectory와 correction packet 검증
- encoder 기반 LINE/ROTATE 실행
- 좌우 wheel synchronization
- BOOT 승인, E-stop과 fault latch
- STATUS, ARRIVED, ERROR 보고

### Vision

- AprilTag 검출
- pixel-to-map homography
- tag center-to-axle center 변환
- MEASURED/HELD/LOST 품질 상태
- calibration/map/pose contract

### Unity

- Server map과 authoritative AGV 표시
- 계획 AGV와 별도의 Vision ghost
- cargo 상차/하차 시각화
- reconnect snapshot과 sequence 처리

## 3. 가상 fleet에서 구현한 것

WHCA*는 `(Node, Time)` 상태에서 MOVE와 WAIT를 탐색한다.

프로젝트에서는 다음 구조로 확장했다.

```text
fixed Time Slot
  → float TimeInterval

Node Reservation
  + Edge Reservation
  + Goal Reservation

Planning Reservation
  + Execution Occupancy

Path candidate
  → 전체 검증
  → Transactional commit
```

최종 AutomaticFleet는 204-node / 644-link FactoryMap에서 28대 virtual AGV를 사용한다.

30초 headless smoke에서는 다음을 관찰했다.

```text
28대 dispatch
Cargo LOADED 24회
Cargo UNLOADED 8회
Safe stop 0회
평균 planned WAIT 2.11초
최대 planned WAIT 9초
```

AGV가 단순히 이동하는 데서 끝나지 않고 실제 pickup/drop event를 cargo state로 만들었다. Unity에서는 상차 시 박스가 AGV의 `CargoMount`에 붙고, 하차 시 해당 박스가 제거된다.

## 4. 실물 AGV에서 구현한 것

실물 차체의 확인된 구성은 다음과 같다.

```text
ESP32-D0WD-V3 개발 보드
TB6612FNG motor driver
DC encoder motor 2개
48 mm wheel
130 mm track width
260 counts/revolution nominal encoder
80 mm AprilTag
2.4 GHz Wi-Fi TCP
```

Firmware는 C++와 Arduino Framework를 사용하고 PlatformIO로 build했다.

실차에서는 성능보다 먼저 안전 조건을 넣었다.

```text
BOOT authorization
5-second countdown
E-stop latch
PWM zero / STBY LOW
TCP disconnect stop
wrong-direction
stall / timeout / overrun
wheel mismatch
settling
ARRIVED gating
```

그리고 위험도와 목적에 따라 14개 PlatformIO profile을 분리했다.

```text
locked
preview/trace
raised-wheel
physical-fleet
straight calibration
turn CW/CCW calibration
channel A/B diagnostic
```

실제 바닥 결과를 반영한 주요 값은 다음과 같다.

```text
350 mm forward: 572 counts
90° CW: 163 counts
90° CCW: 159 counts
Correction coast: CW 14 / CCW 12 counts
```

## 5. Vision과 Digital Twin에서 구현한 것

Overhead camera는 reference AprilTag로 homography를 계산해 pixel을 Server mm 좌표로 바꾼다.

Robot tag 중심은 wheel axle center와 다르기 때문에 body-frame offset을 적용했다.

```text
Robot Tag: 80 mm
Tag → Robot Origin:
  forward 65.77 mm
  left 13.10 mm
```

Pose는 세 상태로 구분한다.

```text
MEASURED: 현재 frame의 새 측정
HELD: 짧은 검출 공백의 마지막 pose
LOST: 사용 불가능
```

Server correction은 fresh `MEASURED + VERIFIED`만 사용한다. Unity는 authoritative AGV와 Vision ghost를 분리하고, MEASURED는 cyan, HELD는 yellow로 표시한다.

이 구조 덕분에 계획 위치와 실제 측정 위치를 한 화면에서 비교할 수 있었다.

## 6. 가장 어려웠던 문제 1 — 반복 회전

Node에 도착한 AGV가 Vision correction 중 제자리 회전을 반복하다 멈췄다.

처음에는 correction 횟수가 부족하다고 생각할 수 있었다. 하지만 실제 원인은 point turn이 위치 slip을 만들고 Server가 바뀐 target bearing을 다시 추격하는 비수렴 loop였다.

해결 과정은 다음과 같다.

```text
Position 접근
Arrival heading
Departure heading
  → 서로 분리

Threshold
  → hysteresis 적용

Primitive count만 검사
  → 실제 오차 감소, 반복 방향, 누적 회전량 검사

공통 turn count
  → CW/CCW와 correction coast 분리
```

개선 후보 영상에서는 어떤 node가 correction 0회로 승인되고 필요한 departure turn만 수행하는 흐름을 확인했다.

## 7. 가장 어려웠던 문제 2 — 대각선 주행

좌우 encoder count가 비슷해도 차체가 대각선으로 흘렀다.

원인은 하나가 아니었다.

```text
우측으로 치우친 하중
wheel traction 차이
caster 방향
바닥 slip
초기 PWM feed-forward 차이
encoder가 lateral motion을 측정하지 못하는 한계
```

하중을 재배치하고 좌우 최종 target과 baseline을 동일하게 맞췄다. 누적 count와 interval rate를 함께 사용해 PWM을 동기화하고, Vision endpoint로 counts/mm와 방향별 turn count를 다시 산정했다.

개선 전과 후 영상의 서로 다른 대표 frame에서는 약 74 mm와 약 12 mm 수준의 endpoint 차이를 관찰했다. 동일 조건의 정식 A/B는 아니므로 참고값으로만 기록했다.

## 8. 가장 어려웠던 문제 3 — 같은 증상의 다른 원인

`BOOT를 눌러도 움직이지 않는다`는 같은 증상에 다음 원인이 모두 있었다.

- 노트북 재부팅 후 WSL internal IP 변경
- Windows portproxy가 예전 WSL 주소를 가리킴
- Server listener 부재
- ESP32가 5 GHz Wi-Fi에 연결될 수 없음
- TB6612 VCC 또는 5V wiring 이탈
- battery 방전 또는 battery lead 단선
- wheel/encoder mismatch fault
- 최초 PRE_DEPARTURE의 routeID 0 lifecycle bug

그래서 특정 repository를 먼저 고치는 대신 다음 흐름을 사용했다.

```text
증상
  → 실제 재현
  → Vision/Server/ESP32/Unity log 비교
  → 첫 incorrect data/state 찾기
  → 담당 subsystem 격리
  → 최소 수정
  → build/test
  → physical verification
```

## 9. 검증

현재 기록된 검증은 다음과 같다.

```text
Server:
  CMake build
  CTest 9/9 PASS

ESP32:
  Host tests 5/5 PASS
  14개 PlatformIO profile build
  Channel A/B와 90° CCW isolated test

Vision:
  Offline suite 92/92 PASS
  실제 calibration reference 5/5 반복 확인

Physical E2E:
  Server trajectory 수신
  BOOT/countdown
  encoder motion
  STATUS/progress/ARRIVED
  Unity authoritative/ghost 표시
```

Test가 통과해도 바닥 마찰, battery, camera occlusion과 장시간 운행까지 보장하지는 않는다. Software test와 physical evidence를 같은 의미로 쓰지 않으려고 했다.

## 10. 잘한 점

### 책임 경계를 끝까지 유지했다

Server가 PWM을 결정하지 않고 ESP32가 전역 경로를 만들지 않게 했다. Unity도 Server state를 바꾸는 controller가 아니라 viewer로 유지했다.

### 안전장치를 디버깅 편의로 제거하지 않았다

움직이지 않는 이유를 찾기 위해 E-stop, countdown, mismatch와 disconnect stop을 끄지 않았다. 대신 diagnostic firmware를 따로 만들었다.

### 첫 오류를 찾으려고 했다

Unity가 멈췄다고 Unity code부터 수정하지 않고, ESP32 packet 생성부터 Server state와 Unity update까지 흐름을 확인했다.

### 확인한 사실과 기대를 구분했다

Calibration RMS와 전체 맵 정확도, unit test와 실차 성능, 가상 28대와 실물 1대의 결과를 구분해 기록했다.

## 11. 아쉬운 점

### 실물 AGV가 한 대뿐이다

여러 실물 AGV의 충돌 회피를 검증하지 못했다. WHCA* 다중 fleet는 Unity에서, 실차 통합은 ESP32 한 대에서 확인했다.

### Vision이 한 대의 overhead camera에 의존한다

사람이 가리거나 tag가 FOV 경계로 이동하면 LOST가 발생한다. Intrinsic/lens distortion도 전체 production pipeline에서 완전히 검증하지 못했다.

### Encoder만으로 lateral slip을 알 수 없다

주행 중 heading을 더 정밀하게 제어하려면 IMU 또는 안정적인 Vision/odometry fusion이 필요하다.

### 로그 수집이 수동이었다

Server, ESP32, Vision, Unity에 공통 run ID와 timestamp가 없어 일부 실패 원인 분석에 시간이 많이 들었다.

## 12. 왜 가상 28대와 실물 1대를 분리해서 보여주는가

원래는 실물 AGV 한 대와 여러 가상 AGV를 동시에 같은 WHCA* fleet에 넣는 hybrid demo도 생각했다.

기술적으로 연결 지점은 존재한다. 둘 다 `IRobotController` 뒤에서 Server 경로를 수행한다.

하지만 실제 시연에서 다음을 모두 검증할 시간이 부족했다.

- 실차 correction 지연을 reservation time에 반영
- physical occupancy와 virtual occupancy 동기화
- camera LOST 중 hybrid fleet 처리
- 실차 한 대와 virtual fleet의 장시간 안정성

그래서 포트폴리오에서는 두 증거를 정직하게 분리한다.

```text
AutomaticFleet + FactoryMap:
  WHCA*, reservation, 28대, cargo

PhysicalFleet + Vision:
  동일 Server와 ESP32 실차 integration
```

한 화면에 억지로 섞는 것보다 각 기능이 실제로 검증된 범위를 명확히 보여주는 편이 낫다고 판단했다.

## 13. 최종 결과

처음 목표였던 `여러 AGV의 충돌 없는 경로 계획`에서 다음 단계까지 확장했다.

```text
Pathfinding
  → Time Reservation
  → Task/Cargo State
  → Robot Protocol
  → ESP32 Motion and Safety
  → Vision Metric Pose
  → Unity Digital Twin
  → Cross-System Correction
```

완벽한 산업용 AGV 시스템은 아니다. 하지만 알고리즘 한 개를 구현하는 데서 끝나지 않고, 그 경로가 network packet이 되고 motor motion이 되고 camera measurement와 화면 state로 돌아오는 전체 loop를 구현했다.

> [이미지 삽입] 전체 architecture

> [이미지 삽입] FactoryMap 28대 AGV와 Cargo

> [영상 삽입] Candidate 2 실차 주행

> [이미지 삽입] Unity authoritative AGV + Vision ghost

## 14. 마무리

이번 프로젝트에서 가장 크게 배운 것은 이것이다.

> 알고리즘이 만든 경로와 실제 로봇이 수행할 수 있는 경로는 같지 않다.

가상 환경에서는 정확한 시간에 노드에 도착하지만, 현실에서는 battery, 하중, 마찰, encoder, Wi-Fi와 camera가 모두 상태에 영향을 준다.

그래서 단순히 "이 코드가 맞아 보인다"가 아니라 다음을 계속 확인해야 했다.

> 실제로 송신한 데이터가 다음 시스템에서 같은 의미로 해석되고 있는가?

WHCA*를 공부하면서 시작한 프로젝트였지만, 마지막에는 protocol lifecycle, control limitation과 시스템 책임 경계를 더 많이 배운 것 같다.

이 시리즈에서는 각 문제를 별도 글로 더 자세히 정리했다. 이후에는 장시간 deterministic fleet test, camera intrinsic calibration과 synchronized logging을 추가해 보고 싶다.

> GitHub: [Server](https://github.com/Hwang-Ju-Hyun/SmartFactory_AGV_DigitalTwin_Server) / [ESP32](https://github.com/Hwang-Ju-Hyun/AGV_DigitalTwin_ESP32) / [Vision](https://github.com/Hwang-Ju-Hyun/SmartFactory_AGV_DigitalTwin_VisionTracker) / [Unity](https://github.com/Hwang-Ju-Hyun/SmartFactory_AGV_DigitalTwin_Unity)

> Demo Video: [링크 삽입]
