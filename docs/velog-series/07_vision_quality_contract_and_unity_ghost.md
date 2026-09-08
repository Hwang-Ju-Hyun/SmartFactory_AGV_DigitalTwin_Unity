# [SmartFactory AGV] 마지막으로 본 위치를 현재 위치라고 믿지 않기 — Vision 품질 상태와 Unity Ghost

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: AprilTag Homography와 차체 원점 보정  
> 핵심 키워드: `MEASURED`, `HELD`, `LOST`, `Freshness`, `Contract ID`, `Unity Ghost`

## 0. 들어가며

AprilTag pose를 얻은 뒤 처음에는 검출이 잠깐 끊겨도 마지막 위치를 계속 보내면 화면이 덜 깜빡일 것이라고 생각했다.

UI 관점에서는 자연스럽다.

```text
Frame 100: Tag 검출, x=350
Frame 101: 검출 실패, x=350 유지
Frame 102: 검출 실패, x=350 유지
```

하지만 이 값을 실제 AGV correction에도 사용한다면 의미가 달라진다.

Frame 101의 `x=350`은 현재 측정이 아니다. 마지막으로 봤던 위치일 뿐이다.

> 화면을 안정적으로 보이게 하는 pose와 모터 보정에 사용할 수 있는 pose는 같은 기준을 가져서는 안 됐다.

그래서 Vision pose를 좌표값뿐 아니라 품질 상태와 함께 전달했다.

## 1. 세 가지 tracking state

현재 Vision pipeline은 pose를 세 상태로 나눈다.

### MEASURED

현재 frame에서 AprilTag를 새로 검출했고 quality filter를 통과했다.

```text
state = MEASURED
pose = valid
measurement age = fresh
```

### HELD

짧은 검출 공백 동안 마지막 측정을 화면에 유지한다.

```text
state = HELD
pose = last known pose
현재 frame에서 새로 측정한 값은 아님
```

### LOST

유지 가능 시간을 넘겼거나 calibration을 사용할 수 없다.

```text
state = LOST
pose = 없음
```

Python enum은 다음처럼 단순하다.

```python
class PoseState(Enum):
    MEASURED = "MEASURED"
    HELD = "HELD"
    LOST = "LOST"
```

중요한 것은 enum 자체보다 각 상태가 소비자에게 무엇을 허용하는가다.

## 2. Server와 Unity는 같은 packet을 다르게 사용한다

Vision은 Server로 observation을 보낸다.

```text
agvID
sequence
tracking state
pose valid
x / z / heading
measurement timestamp 또는 age 의미
calibration ID
```

Server에는 두 소비 경로가 있다.

```text
1. Physical correction input
2. Unity comparison display relay
```

Correction에는 조건이 엄격하다.

```text
MEASURED
+ VERIFIED calibration
+ fresh sequence/time
+ valid finite pose
+ expected AGV ID
+ map bounds 안쪽
```

HELD와 LOST는 새 correction 근거로 사용하지 않는다.

반면 Unity에는 상태 의미를 보존해 전달한다. HELD가 완전히 쓸모없는 값은 아니기 때문이다. 사용자는 tag가 잠깐 가려졌다는 사실과 마지막 위치를 화면에서 볼 수 있다.

## 3. Unity에서 계획 AGV와 Vision AGV를 분리했다

처음에는 Vision pose가 들어올 때 기존 AGV GameObject 위치를 덮어쓰는 방법을 생각할 수 있다.

```csharp
agv.transform.position = visionPose;
```

하지만 그러면 다음 두 값을 비교할 수 없다.

- Server가 계획·실행 상태로 알고 있는 위치
- 카메라가 실제로 측정한 위치

그래서 별도의 ghost를 만들었다.

```text
Authoritative AGV:
  Server world state

Vision Ghost:
  overhead camera observation
```

상태별 표현은 다음과 같다.

```text
MEASURED → Cyan
HELD     → Yellow
LOST     → Hidden
```

이렇게 하면 로봇이 경로에서 얼마나 벗어났는지, correction 뒤에 실측 위치가 어떻게 변했는지, tag가 언제 사라졌는지를 한 화면에서 볼 수 있다.

## 4. Material 공유 문제

Unity에서 ghost 색을 바꿀 때 renderer의 shared material을 그대로 수정하면 원본 AGV까지 같은 색으로 바뀔 수 있다.

```text
원본 AGV material
Vision Ghost material
두 renderer가 같은 asset 공유
```

그래서 ghost 전용 material instance를 만들었다.

```csharp
var instance = new Material(renderer.sharedMaterial);
renderer.material = instance;
```

이제 ghost 상태 색 변경이 authoritative AGV prefab에 전파되지 않는다.

작은 렌더링 문제처럼 보이지만, 계획 상태와 측정 상태를 시각적으로 구분하는 기능 자체를 깨뜨릴 수 있는 부분이었다.

## 5. 좌표만 맞아도 계약이 다르면 거부했다

카메라를 이동했는데 이전 homography가 남아 있거나, tag size와 offset을 바꿨는데 Server가 예전 의미로 해석하면 숫자는 정상적으로 보일 수 있다.

예를 들어 둘 다 `(350, 0)`을 보내더라도 다음 조건이 다를 수 있다.

```text
Calibration A:
  Camera pose A
  Tag size 60 mm
  Offset [60, 0]

Calibration B:
  Camera pose B
  Tag size 80 mm
  Offset [65.77, 13.10]
```

그래서 다음 ID를 분리했다.

### Calibration ID

특정 camera pose에서 생성한 homography와 calibration 결과의 식별자다.

### Map Contract ID

Reference tag ID와 Server map 좌표 정의의 식별자다.

### Pose Contract ID

Robot tag size, heading 의미와 tag-center-to-origin transform의 식별자다.

Vision handshake에서 이 값들을 보내고 Server가 기대값과 비교한다.

```text
숫자가 finite하더라도
contract가 다르면 observation-only 연결 거부
```

오래된 calibration을 조용히 사용하는 것보다 실행 초기에 명확히 실패하는 편이 안전하다.

## 6. Sequence와 freshness가 필요한 이유

TCP는 byte 순서를 보장하지만 "이 pose가 지금 제어에 충분히 새 값인지"까지 알려주지 않는다.

Detector 처리 지연이나 reconnect가 있다면 오래된 frame의 pose가 늦게 도착할 수 있다.

그래서 observation sequence와 measurement age를 검사했다.

```text
sequence <= last sequence
  → duplicate 또는 stale packet

measurement age > correction limit
  → 화면 relay는 가능할 수 있어도 correction 금지
```

Vision capture도 latest-frame 방식으로 구성해 detector가 느릴 때 쌓인 frame을 차례로 처리하기보다 가능한 한 최신 frame을 사용하게 했다.

## 7. 실제 LOST는 어떻게 보였는가

실차가 회전하거나 사람이 카메라 아래를 지나갈 때 다음 상태가 반복됐다.

```text
MEASURED → HELD → LOST → MEASURED
```

이때 Server가 HELD pose를 계속 새 측정처럼 사용했다면 이미 이동한 로봇을 이전 위치로 다시 보정하려 했을 것이다.

실제 시험에서는 fresh measurement가 일정 시간 오지 않으면 `fresh Vision measurement timeout`으로 안전 정지했다.

주행이 멈춘 것은 아쉬운 결과였지만, 오래된 pose를 기반으로 계속 움직이는 것보다 의도한 동작에 가깝다.

후보 3 영상은 이 상황을 보여주는 자료로 사용할 수 있다.

> [영상 삽입] Candidate 3의 MEASURED → HELD → LOST와 safe stop

> [이미지 삽입] Unity Cyan/Yellow ghost 비교

> [이미지 삽입] Vision observation packet field 표

## 8. 검증

Vision offline suite는 다음을 포함해 92 tests를 통과했다.

- pose hold transition
- calibration guard
- homography geometry
- map/pose contract
- preview transformation
- rotation diagnostic
- Server client serialization

Unity parser에서는 다음을 검사한다.

- payload size
- AGV ID
- sequence
- state와 pose-valid 조합
- finite x/z/heading
- trailing byte
- create 전 update와 pending state

## 9. 느낀 점

처음에는 Vision output을 `x, z, heading` 세 숫자로 생각했다.

하지만 실제 제어 시스템에서 중요한 것은 숫자와 함께 붙는 의미였다.

```text
언제 측정했는가?
현재 frame의 값인가?
어떤 calibration으로 만들었는가?
어떤 tag size와 origin을 사용했는가?
누가 제어에 사용해도 되는가?
```

결국 pose는 단순한 좌표가 아니라 품질과 수명주기를 가진 데이터였다.

다음 글에서는 이 Vision pose를 Server가 도착 보정에 사용하면서 생긴 반복 회전 문제를 정리한다.

> 다음 글: Node Correction이 끝없이 회전한 이유

