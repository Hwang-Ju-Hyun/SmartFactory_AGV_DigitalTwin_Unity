# [SmartFactory AGV] AprilTag를 찾았는데 로봇 위치가 틀렸다 — Homography와 차체 원점 보정

> 시리즈: SmartFactory AGV Digital Twin  
> 이전 글: 엔코더 값은 같은데 왜 로봇은 대각선으로 갈까?  
> 핵심 키워드: `OpenCV`, `AprilTag`, `Homography`, `Coordinate System`, `Body Offset`

## 0. 들어가며

실물 AGV가 Server 경로를 따라 움직이기 시작한 뒤에는 "어디까지 갔는가"를 외부에서 확인할 방법이 필요했다.

Encoder로 이동량을 알 수 있지만 바닥 slip이 있으면 세계 좌표에서의 실제 위치와 달라진다. 그래서 천장 방향에서 카메라로 AprilTag를 보고 AGV의 pose를 측정했다.

처음 목표는 단순했다.

```text
카메라에서 Tag 0 검출
  → pixel 중심 좌표
  → 실제 맵 x/z 좌표
```

그런데 tag 중심을 정확하게 찾고도 Unity의 로봇 위치와 실제 바퀴축 중심이 맞지 않았다.

원인은 AprilTag의 위치와 로봇 제어에서 사용하는 원점이 서로 달랐기 때문이다.

## 1. Pixel 좌표는 Server 좌표가 아니다

카메라에서 검출한 값은 pixel이다.

```text
Tag center = (u, v)
```

Server의 맵은 mm 단위다.

```text
Robot pose = (x_mm, z_mm, heading_rad)
```

카메라가 정확히 수직이 아니고 원근이 있기 때문에 단순 scale만 곱할 수 없다.

```text
x_mm = u × scale
z_mm = v × scale
```

이 방식은 화면 위치에 따라 오차가 달라진다.

그래서 고정된 reference AprilTag의 pixel 중심과 실제 Server 좌표를 대응시켜 planar homography를 계산했다.

```text
Reference Tag 1: pixel (u1, v1) ↔ map (x1, z1)
Reference Tag 2: pixel (u2, v2) ↔ map (x2, z2)
...
```

OpenCV에서는 다음 흐름을 사용했다.

```python
homography, mask = cv2.findHomography(
    source_pixel_points,
    target_map_points,
    method=cv2.RANSAC,
)
```

검출된 tag 중심은 3×3 matrix를 통해 map 좌표로 투영한다.

```python
pixel = np.array([u, v, 1.0])
mapped = homography @ pixel
x_mm = mapped[0] / mapped[2]
z_mm = mapped[1] / mapped[2]
```

## 2. Homography가 해결하는 것과 못 하는 것

Homography는 같은 평면 위의 projective transform을 표현한다.

따라서 다음 문제를 다룰 수 있다.

- 카메라가 약간 기울어짐
- 원근에 따라 pixel/mm scale이 달라짐
- 맵의 x/z 축과 camera u/v 축 방향이 다름

하지만 lens distortion 자체를 완전히 모델링하지는 않는다.

Vision 화면에 표시한 다음 문구는 실패가 아니라 이 한계를 알리는 경고다.

```text
LENS: NO INTRINSICS
VERIFY KNOWN-NODE ERROR ACROSS MAP
```

Camera intrinsic과 distortion coefficient가 없기 때문에 calibration anchor의 RMS가 작아도 화면 중앙과 모서리의 실제 오차가 같다고 보장할 수 없다.

## 3. Calibration RMS가 작으면 정확한 것 아닌가?

Calibration에서는 다음 값을 확인했다.

```text
inlier tag count
RMS residual
maximum residual
```

예를 들어 reference tag 5/5와 RMS 0.5 mm가 나왔다고 하자.

이것은 **사용한 reference tag의 중심이 계산된 homography에 얼마나 잘 맞는가**를 보여준다.

다음까지 자동으로 보장하는 값은 아니다.

- reference 사이의 모든 위치 정확도
- 렌즈 왜곡이 큰 화면 가장자리
- 바닥보다 높이 있는 robot tag plane
- tag 중심에서 실제 robot origin으로의 offset

실제로 calibration RMS는 작았지만 Node 1에 놓은 AGV origin이 약 16 mm 벗어나 보인 적이 있었다.

그때는 calibration fitting과 robot origin transform을 분리해서 봐야 했다.

## 4. Tag 중심은 로봇 중심이 아니다

로봇의 제자리 회전 중심은 좌우 wheel axle의 중간점이다.

하지만 AprilTag는 차체 공간 때문에 그보다 뒤쪽과 옆쪽에 붙어 있었다.

```text
        진행 방향
            ↑

LEFT wheel O-----+-----O RIGHT wheel
                 |
          axle center = robot origin
                 |
          AprilTag center
```

따라서 raw tag 중심을 그대로 pose로 보내면 로봇이 회전할 때 중심이 원을 그리게 된다.

현재 측정한 offset은 다음과 같다.

```text
Tag center → robot origin
forward = 65.77 mm
left    = 13.10 mm
```

## 5. Body-frame offset을 world 좌표로 회전했다

Offset은 map x/z에 고정된 값이 아니라 로봇 body 기준 값이다.

로봇 heading이 바뀌면 forward와 left 방향도 함께 회전해야 한다.

개념적으로 다음과 같이 계산한다.

```text
robot_x = tag_x
        + forward × cos(heading)
        - left    × sin(heading)

robot_z = tag_z
        + forward × sin(heading)
        + left    × cos(heading)
```

Python에서는 이 변환을 별도 함수로 분리했다.

```python
def tag_center_to_robot_origin(
    tag_x_mm,
    tag_z_mm,
    heading_rad,
    forward_mm,
    left_mm,
):
    c = math.cos(heading_rad)
    s = math.sin(heading_rad)

    robot_x = tag_x_mm + forward_mm * c - left_mm * s
    robot_z = tag_z_mm + forward_mm * s + left_mm * c
    return robot_x, robot_z
```

이제 Unity와 Server가 보는 pose는 tag 종이의 중심이 아니라 실제 바퀴축 중심에 가까워졌다.

## 6. 회전 시험으로 offset을 진단했다

Offset이 정확하다면 로봇을 제자리에서 여러 방향으로 회전시켜도 계산된 robot origin은 거의 같은 위치에 머물러야 한다.

```text
0° pose
90° pose
180° pose
270° pose

raw tag center:
  원을 그릴 수 있음

offset 적용 robot origin:
  한 점 근처에 모여야 함
```

그래서 Vision log에 다음 중간값을 남겼다.

- raw tag center
- heading
- 적용한 forward/left offset
- 최종 robot origin
- calibration ID
- tracking state

그리고 rotation diagnostic에서는 fresh `MEASURED` sample만 사용했다. `HELD`와 `LOST`는 실제 새 측정이 아니므로 offset 계산에서 제외했다.

## 7. 카메라를 건드리면 왜 다시 calibration 해야 할까

Homography는 특정 camera pose에서 pixel과 map의 관계를 저장한 값이다.

카메라를 조금만 움직여도 같은 실제 좌표가 다른 pixel에 보인다.

```text
카메라 이동 전:
  pixel P → map Node 1

카메라 이동 후:
  pixel P' → map Node 1
```

이전 homography를 그대로 사용하면 전체 map pose가 이동하거나 회전한다.

실제 시험 중 카메라를 건드린 뒤 reference residual이 커지고 robot origin이 어긋난 일이 여러 번 있었다. 이때 offset 숫자를 먼저 바꾸지 않고 새 calibration을 만들었다.

```text
카메라 pose 변경
  → reference tag 5개 재검출
  → homography 재계산
  → inlier/RMS/max residual 검사
  → 새 calibration ID 생성
```

카메라 문제와 차체 offset 문제를 한 숫자로 섞지 않기 위해서다.

## 8. Tag 크기를 60 mm에서 80 mm로 바꾼 이유

회전 중 tag가 작게 보이거나 일부 가려지면 AprilTag 검출이 `MEASURED → HELD → LOST`로 변했다.

Robot tag를 80 mm로 키우면 image에서 차지하는 pixel 수가 늘어 검출에 유리하다.

하지만 config의 tag size와 실제 출력 크기가 반드시 일치해야 한다. 크기를 바꾸면 pose contract도 달라지므로 기존 설정을 그대로 사용할 수 없다.

현재 tracked 설정은 다음과 같다.

```text
Robot tag size: 80.0 mm
Body offset: forward 65.77 mm / left 13.10 mm
```

## 9. 검증과 한계

Vision offline suite에서는 calibration, geometry, pose hold, map contract, rotation diagnostic과 Server client를 포함해 92 tests를 통과했다.

실제 calibration에서도 reference tag 5/5와 RMS/max residual을 반복 확인했다.

그러나 다음 표현은 아직 사용할 수 없다.

```text
전체 맵에서 mm 정확도 보장
모든 조명과 가림에서 pose 유지
고속 주행 중 연속 steering sensor로 검증
```

더 높은 정확도가 필요하면 checkerboard로 intrinsic과 lens distortion을 구하고, 맵 중앙과 네 모서리의 known-node error를 따로 측정해야 한다.

> [이미지 삽입] reference tag 5개와 robot tag가 보이는 Vision preview

> [이미지 삽입] raw tag center → axle center offset 그림

> [이미지 삽입] 회전 전후 raw center와 corrected origin scatter plot

## 10. 느낀 점

Vision에서 가장 헷갈렸던 것은 "검출에 성공했다"와 "제어에 쓸 수 있는 위치가 정확하다"가 같은 말이 아니라는 점이었다.

```text
Tag detection 성공
  ≠ map 좌표가 정확함

Calibration RMS가 작음
  ≠ robot origin이 정확함

마지막 pose가 화면에 남아 있음
  ≠ 지금 새로 측정됨
```

이 세 가지를 분리하면서 Vision을 단순 좌표 생성기가 아니라 품질과 좌표 계약을 가진 subsystem으로 보게 됐다.

다음 글에서는 `MEASURED`, `HELD`, `LOST`를 왜 나눴는지, Server와 Unity가 같은 pose를 서로 다르게 사용하는 이유를 정리한다.

> 다음 글: Vision quality contract와 Unity ghost

