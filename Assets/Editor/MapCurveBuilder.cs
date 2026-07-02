#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class MapCurveBuilder : Editor
{
    // 단축키: Ctrl + Shift + C (Curve)
    [MenuItem("MapEditor/Create Curve Link %w")]
    public static void CreateCurveLink()
    {
        // 1. 선택한 오브젝트 검사
        GameObject[] selected = Selection.gameObjects;
        if (selected.Length != 2)
        {
            Debug.LogWarning("곡선 링크를 만들려면 MapNode 2개를 선택해야 합니다!");
            return;
        }

        MapNode nodeA = selected[0].GetComponent<MapNode>();
        MapNode nodeB = selected[1].GetComponent<MapNode>();

        if (nodeA == null || nodeB == null)
        {
            Debug.LogWarning("선택한 오브젝트에 MapNode 컴포넌트가 없습니다!");
            return;
        }

        //2. 양방향 링크(A->B, B->A) 2개를 동시에 생성!
        GameObject link1 = CreateSingleCurve(nodeA, nodeB);
        GameObject link2 = CreateSingleCurve(nodeB, nodeA);

        // 3. 에디터 편의: 방금 생성된 2개의 양방향 링크를 한 번에 선택 상태로 만듦
        Selection.objects = new UnityEngine.Object[] { link1, link2 };

        Debug.Log($"양방향 곡선 링크 2개 생성 완료: {nodeA.nodeID} -> {nodeB.nodeID}");
    }

    //링크 1개를 생성하고 스플라인을 세팅해주는 전용 함수
    private static GameObject CreateSingleCurve(MapNode from, MapNode to)
    {
        // 1. 오브젝트 생성 및 컴포넌트 부착
        GameObject linkGO = new GameObject($"CurveLink_{from.name}_to_{to.name}");
        linkGO.transform.position = from.transform.position; // 시작 위치를 From 노드에 맞춤

        MapLink mapLink = linkGO.AddComponent<MapLink>();
        mapLink.fromNode = from;
        mapLink.toNode = to;
        mapLink.isCurve = true;

        SplineContainer splineContainer = linkGO.AddComponent<SplineContainer>();
        mapLink.attachedSpline = splineContainer;

        // 2. 스플라인 데이터 설정
        Spline spline = splineContainer.Spline;
        spline.Clear();

        Vector3 localStart = Vector3.zero;
        Vector3 localEnd = linkGO.transform.InverseTransformPoint(to.transform.position);

        Vector3 dir = (localEnd - localStart).normalized;
        float dist = Vector3.Distance(localStart, localEnd);

        // 3. BezierKnot 구조체 생성 및 자석(Tangent) 세팅
        BezierKnot startKnot = new BezierKnot();
        startKnot.Position = new float3(localStart.x, localStart.y, localStart.z);
        startKnot.TangentOut = new float3(dir.x * dist * 0.4f, 0f, dir.z * dist * 0.4f);
        startKnot.TangentIn = float3.zero;

        BezierKnot endKnot = new BezierKnot();
        endKnot.Position = new float3(localEnd.x, localEnd.y, localEnd.z);
        endKnot.TangentIn = new float3(-dir.x * dist * 0.4f, 0f, -dir.z * dist * 0.4f);
        endKnot.TangentOut = float3.zero;

        // 4. 모드는 반드시 Broken으로 추가
        spline.Add(startKnot, TangentMode.Broken);
        spline.Add(endKnot, TangentMode.Broken);

        // 실행 취소(Ctrl+Z) 기록 남기기
        Undo.RegisterCreatedObjectUndo(linkGO, "Create Curve Link");

        return linkGO;
    }
}
#endif