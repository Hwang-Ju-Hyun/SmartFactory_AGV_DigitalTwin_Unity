# [SmartFactory AGV] BOOT를 눌러도 움직이지 않을 때 — ESP32, WSL, Vision, Unity를 함께 추적한 방법

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: 최초 routeID 0과 Protocol Lifecycle  
> 핵심 키워드: `Cross-System Debugging`, `WSL2`, `TCP`, `portproxy`, `Hardware Fault`, `Log Trace`

## 0. 들어가며

이 프로젝트에서 가장 자주 들었던 말은 아마 이것이었다.

> BOOT를 눌렀는데 왜 안 움직이지?

처음에는 ESP32 code부터 의심했다. 하지만 같은 증상의 원인이 매번 달랐다.

```text
한 번은 Wi-Fi였다.
한 번은 WSL portproxy였다.
한 번은 Server가 내려가 있었다.
한 번은 TB6612 VCC가 빠져 있었다.
한 번은 배터리가 방전됐다.
한 번은 encoder mismatch fault였다.
한 번은 routeID lifecycle 버그였다.
```

Unity 화면에서 AGV가 멈췄다는 사실만으로 Unity bug라고 판단할 수도 없었다.

그래서 프로젝트 후반에는 "어느 repository가 문제인가"보다 **데이터가 처음 틀린 지점이 어디인가**를 기준으로 디버깅했다.

## 1. 전체 데이터 흐름

실차 주행은 네 프로그램과 실제 배선이 연결된다.

```text
Vision
  AprilTag pose 측정
        ↓
WSL C++ Server
  작업 / 경로 / 보정 명령
        ↓
Windows portproxy + Wi-Fi TCP
        ↓
ESP32
  packet 검증 / motor / encoder
        ↓
WSL C++ Server
  STATUS / ARRIVED / ERROR 반영
        ↓
Unity
  authoritative AGV / Vision ghost 표시
```

어느 한 계층이 멈추면 최종 증상은 모두 비슷하다.

```text
Unity AGV가 움직이지 않음
실물 AGV가 움직이지 않음
다음 edge가 dispatch되지 않음
```

## 2. 디버깅 순서를 고정했다

무작정 설정을 바꾸지 않고 다음 순서를 사용했다.

```text
Bug symptom
  ↓
Reproduce
  ↓
Trace data flow
  ↓
Find first incorrect state/data
  ↓
Identify responsible subsystem
  ↓
Minimal fix
  ↓
Build/test
  ↓
Regression check
```

예를 들어 `ESP32는 ARRIVED를 보냈는데 Unity가 멈췄다`면 다음을 확인한다.

```text
ESP32 ARRIVED packet 생성
  → TCP send 성공?
  → Server receive?
  → parser routeID/nodeID 일치?
  → Server state transition?
  → next command 또는 Unity update 생성?
  → Unity parser?
  → Render state 반영?
```

처음 잘못된 값이 나온 지점부터 수정한다.

## 3. 사례 1 — Windows에서는 port가 열렸는데 ESP32는 connection reset

Server는 WSL2 Ubuntu에서 실행했다.

```text
WSL Server listen: 0.0.0.0:6666
Windows LAN IP: 192.168.45.194
ESP32 target: 192.168.45.194:16666
```

Windows `portproxy`가 LAN의 16666 port를 WSL의 6666으로 전달했다.

```text
ESP32
  → 192.168.45.194:16666
  → Windows portproxy
  → WSL_IP:6666
  → C++ Server
```

노트북을 재부팅하면 WSL internal IP가 바뀔 수 있다. 기존 portproxy가 예전 WSL IP를 바라보면 Windows 쪽 listener는 있어 보여도 실제 target 연결은 실패한다.

실제 ESP32 로그는 다음과 비슷했다.

```text
[TCP] Connecting to 192.168.45.194:16666
connect(): errno 104, "Connection reset by peer"
[TCP] Connect failed
```

확인 순서는 다음과 같다.

```powershell
wsl.exe hostname -I
netsh interface portproxy show all
netstat -ano | findstr :16666
Test-NetConnection 192.168.45.194 -Port 16666
```

그리고 WSL 안에서는 실제 Server listener를 확인한다.

```bash
ss -ltnp | grep 6666
```

여기서 배운 것은 `Test-NetConnection` 성공이 application server의 정상 동작까지 의미하지는 않는다는 것이다. portproxy listener만 열려 있어도 TCP 연결 검사는 성공할 수 있다.

## 4. 사례 2 — Unity connection refused

Unity 로그에는 다음 한 줄만 보였다.

```text
[Viewer] Connection failed:
No connection could be made because the target machine actively refused it.
```

이 메시지만 보면 Unity networking code를 수정하고 싶어진다.

하지만 `actively refused`는 보통 목적지에서 listen 중인 process가 없다는 뜻이다.

확인할 것은 다음이었다.

```text
1. WSL Server process가 실행 중인가?
2. 0.0.0.0:6666에 listen 중인가?
3. Unity가 WSL localhost 또는 올바른 endpoint를 보는가?
4. Windows→WSL forwarding이 필요한 실행 방식인가?
```

실제 여러 경우에서 Unity parser가 아니라 Server가 꺼졌거나 forwarding target이 오래된 것이 원인이었다.

## 5. 사례 3 — Network가 정상인데 모터가 전혀 돌지 않음

Server와 ESP32가 연결되고 trajectory log도 나왔지만 wheel은 움직이지 않았다.

Software 상태만 보면 다음까지 정상일 수 있다.

```text
HELLO accepted
TRAJECTORY received
BOOT countdown complete
ARMED
```

그런데 TB6612의 logic power인 VCC가 빠져 있거나 buck converter output wiring이 느슨하면 motor driver가 동작하지 않는다.

실제 현장에서 확인한 항목은 다음과 같다.

```text
Battery + / -
Buck VIN+ / VIN-
Buck VOUT+ / VOUT-
ESP32 5V / GND
TB6612 VCC / VM / GND
STBY pin
공통 GND
```

주의할 점은 `강하 모듈 LED가 켜졌다`는 사실만으로 모든 출력 배선이 정상이라고 볼 수 없다는 것이다.

또한 USB를 꽂으면 ESP32 LED가 켜지고 battery만 연결하면 꺼지는 경우, firmware보다 전원 경로를 먼저 확인해야 한다.

## 6. 사례 4 — 반쯤 회전하고 ESP32 ERROR

Server에서는 다음과 같은 로그가 나왔다.

```text
[RobotProtocol] AGV 1 reported ERROR code=100 detail=65539
[RoutePlanner] STRICT ROUTE SAFE STOP
```

Server가 멈춘 것처럼 보였지만 첫 오류는 ESP32의 motor fault였다.

Vision observation은 계속 accepted되고 있었다.

```text
[Vision] Observation accepted ...
```

따라서 Vision 연결 문제로 Server가 멈춘 것이 아니었다.

이 경우 다음을 분리했다.

```text
Vision:
  pose packet은 계속 들어옴

ESP32:
  motion 중 fault 발생

Server:
  ESP32 ERROR를 받은 뒤 의도적으로 strict safe stop
```

그 뒤 channel A/B diagnostic과 wheel mismatch detail을 사용해 motor output과 encoder mapping을 확인했다.

## 7. 사례 5 — Vision이 계속 REJECT/LOST

Vision preview에서 `REJECT`, `HELD`, `LOST`가 반복될 때 로봇이 멈출 수 있다.

하지만 두 경우를 나눠야 한다.

```text
주행 중 ESP32 fault로 즉시 정지
  → Vision과 무관할 수 있음

Node correction 대기 중 fresh measurement timeout
  → Vision LOST가 직접 원인
```

Server log에서 다음을 확인한다.

- 마지막 trajectory/ARRIVED가 있었는가
- correction measurement를 기다리는 상태였는가
- `fresh Vision measurement timeout`이 발생했는가
- ESP32 ERROR가 먼저였는가

Candidate 3 주행에서는 사람의 몸이 camera view를 가린 뒤 tracking state가 LOST가 되었고, Server가 fresh measurement timeout으로 정지한 흐름을 확인했다.

## 8. 로그를 한곳에서 비교했으면 더 좋았을 것이다

실제 디버깅에서는 Server, Vision, ESP32 serial, Unity Console의 시간이 서로 달랐다.

```text
Server: WSL terminal
Vision: Windows Python log
ESP32: PlatformIO serial monitor
Unity: Editor Console
```

일부 실패 로그는 terminal scrollback에서 밀려 정확한 문구를 복구하기 어려웠다.

다시 만든다면 모든 실행에 공통 run ID를 붙일 것이다.

```text
run_id = 20260907-physical-004
```

그리고 각 log에 다음을 공통으로 남긴다.

```text
timestamp
run_id
agvID
routeID
commandID
nodeID
state
source subsystem
```

이렇게 하면 네 log를 자동으로 하나의 timeline으로 정렬할 수 있다.

## 9. 내가 사용한 최소 진단표

### BOOT를 눌러도 출발하지 않을 때

```text
[ESP32]
BOOT countdown이 시작됐는가?
ARMED가 출력됐는가?
Wi-Fi IP를 받았는가?
TCP connect가 성공했는가?
trajectory를 받았는가?
fault가 latch됐는가?

[Server]
process가 살아 있는가?
ESP32 HELLO가 accepted됐는가?
Vision handshake가 accepted됐는가?
trajectory를 보냈는가?
SAFE STOP 이유가 무엇인가?

[Vision]
VERIFIED인가?
MEASURED가 새로 들어오는가?
calibration/map/pose contract가 맞는가?

[Hardware]
battery voltage가 있는가?
VCC/VM/5V/GND가 연결됐는가?
STBY와 motor channel mapping이 맞는가?
```

## 10. 느낀 점

이 프로젝트 전에는 화면에 보이는 증상과 버그 위치를 거의 같은 것으로 생각했다.

```text
Unity가 멈춤 → Unity 문제
Motor가 멈춤 → ESP32 code 문제
Vision LOST → Camera 문제
```

실제로는 한 subsystem이 다른 subsystem의 상태를 반영한 결과인 경우가 더 많았다.

가장 효과적이었던 질문은 이것이었다.

> 실제로 송신한 데이터가 다음 시스템에서 같은 의미로 해석됐는가? 그리고 처음 잘못된 값은 어디에서 생겼는가?

다음 글에서는 전체 프로젝트를 마무리하며, 28대 가상 AGV와 실물 AGV 한 대를 왜 하나의 시연에 억지로 섞지 않았는지, 무엇을 구현했고 무엇은 아직 검증하지 못했는지 정리한다.

> 다음 글: SmartFactory AGV Digital Twin 프로젝트 회고

