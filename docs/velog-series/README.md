# SmartFactory AGV Digital Twin Velog 시리즈

이 폴더는 기존 Velog 글 이후의 개발 과정을 주제별로 나눈 게시용 초안이다. 각 파일은 한 편의 글로 독립 게시할 수 있도록 작성했다.

## 기존 글과의 연결

사용자가 이미 작성한 글에서 다음 내용을 다뤘다.

1. Cooperative A*와 `(Node, Time)` Reservation Table의 첫 구현
2. Node Reservation만으로 막을 수 없었던 head-on collision과 Link Reservation
3. Time Slot의 한계를 보완한 TimeInterval Reservation, RRA*와 WHCA* 경로 탐색
4. Server world state를 Unity에 복제하는 Replication 구조
5. Unity protocol과 분리한 ESP32 RobotProtocol의 초기 설계

아래 글은 그 다음 단계다. 초기 설계 설명을 반복하기보다, 실제 가상 fleet와 실물 AGV를 붙이면서 드러난 문제와 현재 구조를 중심으로 썼다.

## 권장 게시 순서

| 순서 | 파일 | 중심 질문 |
|---:|---|---|
| 1 | [01_whca_time_interval_after.md](01_whca_time_interval_after.md) | TimeInterval 예약만 만들면 WHCA*가 끝나는가? |
| 2 | [02_transactional_reservation_and_occupancy.md](02_transactional_reservation_and_occupancy.md) | 계획 예약과 실제 점유는 왜 분리해야 하는가? |
| 3 | [03_virtual_fleet_dispatch_and_cargo.md](03_virtual_fleet_dispatch_and_cargo.md) | 28대 AGV를 실제 물류 작업처럼 보이게 하려면? |
| 4 | [03a_robot_protocol_after_hardware.md](03a_robot_protocol_after_hardware.md) | 초기 RobotProtocol은 실차 연결 뒤 어떻게 달라졌는가? |
| 5 | [04_esp32_safety_state_machine.md](04_esp32_safety_state_machine.md) | 실물 모터를 다룰 때 안전 상태 머신은 어떻게 설계했는가? |
| 6 | [05_encoder_control_and_diagonal_drift.md](05_encoder_control_and_diagonal_drift.md) | 엔코더가 같은데 왜 로봇은 대각선으로 가는가? |
| 7 | [06_apriltag_homography_and_robot_origin.md](06_apriltag_homography_and_robot_origin.md) | AprilTag 중심과 로봇 위치는 왜 다른가? |
| 8 | [07_vision_quality_contract_and_unity_ghost.md](07_vision_quality_contract_and_unity_ghost.md) | 마지막 pose와 현재 측정을 어떻게 구분할 것인가? |
| 9 | [08_server_node_correction_convergence.md](08_server_node_correction_convergence.md) | 위치 보정이 왜 반복 회전으로 바뀌었는가? |
| 10 | [09_routeid_zero_protocol_lifecycle.md](09_routeid_zero_protocol_lifecycle.md) | 첫 출발 전에 routeID가 0이 된 이유는? |
| 11 | [10_cross_system_debugging.md](10_cross_system_debugging.md) | BOOT를 눌러도 안 움직일 때 어디부터 볼 것인가? |
| 12 | [11_project_retrospective.md](11_project_retrospective.md) | 가상 알고리즘을 실물 로봇으로 옮기며 무엇을 배웠는가? |

## 공통 편집 규칙

- 코드 블록은 이해에 필요한 부분만 남긴 간략화 예시다. 실제 저장소 링크를 함께 단다.
- `[이미지 삽입]`, `[영상 링크]`, `[이전 글]`, `[다음 글]`은 게시 전 교체한다.
- `28대`, `30초`, `24회/8회`, `74 mm/12 mm` 같은 수치에는 시험 조건을 반드시 붙인다.
- `산업용 완성품`, `전체 맵 cm 이하 정확도`, `여러 실물 AGV 검증`처럼 확인하지 않은 표현은 사용하지 않는다.
- 첫 글부터 모두 올리기보다, 코드·영상 증거가 준비된 글부터 게시해도 된다.

## 시리즈 대표 소개

> WHCA* 기반 다중 AGV 경로 계획으로 시작한 프로젝트를 C++ Server, Unity Digital Twin, ESP32 실물 AGV와 AprilTag Vision까지 확장했다. 알고리즘 구현보다 더 오래 걸렸던 protocol, 물리 오차, 보정 수렴과 cross-system debugging 과정을 기록한다.

## 게시물에 사용할 수 있는 현재 링크

- Server: <https://github.com/Hwang-Ju-Hyun/SmartFactory_AGV_DigitalTwin_Server>
- ESP32: <https://github.com/Hwang-Ju-Hyun/AGV_DigitalTwin_ESP32>
- Vision: <https://github.com/Hwang-Ju-Hyun/SmartFactory_AGV_DigitalTwin_VisionTracker>
- Unity: <https://github.com/Hwang-Ju-Hyun/SmartFactory_AGV_DigitalTwin_Unity>
- 기존 WHCA* 결과 영상: <https://youtu.be/oEkSK-EmCSg>

Velog 게시 후에는 각 글의 `[이전 글]`, `[다음 글]`을 실제 URL로 교체하고, 포트폴리오의 해당 기술 설명 옆에 개별 글 링크를 건다. 예를 들어 WHCA* 페이지에는 01·02번 글, ESP32 페이지에는 03a·04·05번 글, Vision 페이지에는 06·07번 글, 트러블슈팅 페이지에는 08·09·10번 글을 연결한다.
