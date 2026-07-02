using UnityEngine;
using UnityEngine.Splines;

public class MapLink : MonoBehaviour
{
    [Header("단방향 연결 (From -> To)")]
    public MapNode fromNode;
    public MapNode toNode;

    [Header("곡선(Curve) 설정")]
    public bool isCurve;
    public SplineContainer attachedSpline;

    
    public float GetLinkLength()
    {
        if (isCurve && attachedSpline != null)
        {
            return attachedSpline.CalculateLength();
        }
        else if (fromNode != null && toNode != null)
        {
            return Vector3.Distance(fromNode.transform.position, toNode.transform.position);
        }
        return 0f;
    }

    private void OnDrawGizmos()
    {
        if (fromNode == null || toNode == null) return;

        Vector3 startPos = fromNode.transform.position;
        Vector3 endPos = toNode.transform.position;

        Gizmos.color = isCurve ? Color.green : Color.yellow;

        // 1. 곡선 모드일 때
        if (isCurve && attachedSpline != null)
        {
            // 스플라인 선은 유니티가 그려주니, 우리는 끝점에 도착 방향 화살표만 그려줍니다!
            Vector3 localTangent = (Vector3)attachedSpline.EvaluateTangent(1f);
            Vector3 localDirection = localTangent.normalized;
            Vector3 worldDirection = attachedSpline.transform.TransformDirection(localDirection);

            Vector3 arrowPos = endPos - worldDirection * 0.6f;
            DrawArrow(arrowPos, worldDirection);
        }
        // 2. 직선 모드일 때
        else
        {
            Gizmos.DrawLine(startPos, endPos);
            Vector3 direction = (endPos - startPos).normalized;
            Vector3 arrowPos = endPos - direction * 0.6f;
            DrawArrow(arrowPos, direction);
        }
    }

    private void DrawArrow(Vector3 position, Vector3 direction)
    {
        if (direction == Vector3.zero) return;

        Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + 25, 0) * Vector3.forward;
        Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - 25, 0) * Vector3.forward;

        Gizmos.DrawRay(position, right * 0.5f);
        Gizmos.DrawRay(position, left * 0.5f);
    }
}