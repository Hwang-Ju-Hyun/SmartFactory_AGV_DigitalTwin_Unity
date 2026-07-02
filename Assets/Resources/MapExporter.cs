#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UnityEngine.Splines; // 스플라인 사용을 위해 추가

[System.Serializable]
public class NodeData { public int id; public float x; public float z; }

[System.Serializable]
public class LinkData
{
    public int from;
    public int to;
    public byte type; //0 = straight 1 = curve
    public float dist;
    public float cx1;
    public float cz1;
    public float cx2;
    public float cz2;
}

[System.Serializable]
public class MapData
{
    public List<NodeData> nodes = new List<NodeData>();
    public List<LinkData> links = new List<LinkData>();
}

public class MapExporter : MonoBehaviour
{
    [MenuItem("AGV Tools/Export Map to JSON")]
    public static void ExportJSON()
    {
        MapData mapData = new MapData();

        // 1. 노드 수집        
        MapNode[] allNodes = FindObjectsOfType<MapNode>();
        foreach (MapNode node in allNodes)
        {
            NodeData nData = new NodeData();
            // 참고: 가지고 계신 MapNode 스크립트의 변수명에 맞게 조절하세요 (nodeID -> m_Id 등)
            nData.id = node.nodeID;
            nData.x = Mathf.Round(node.transform.position.x * 100f) / 100f;
            nData.z = Mathf.Round(node.transform.position.z * 100f) / 100f;
            mapData.nodes.Add(nData);
        }

        // 2. 링크 수집 (직선/곡선 데이터 분리)
        MapLink[] allLinks = FindObjectsOfType<MapLink>();
        foreach (MapLink link in allLinks)
        {
            if (link.fromNode == null || link.toNode == null)
            {
                Debug.LogWarning($"[경고] 비어있는 링크가 있습니다: {link.gameObject.name}");
                continue;
            }

            LinkData lData = new LinkData();
            lData.from = link.fromNode.nodeID;
            lData.to = link.toNode.nodeID;
            lData.dist = Mathf.Round(link.GetLinkLength() * 100f) / 100f; // 소수점 둘째 자리까지

            if (link.isCurve && link.attachedSpline != null)
            {
                lData.type =1;

                var spline = link.attachedSpline.Spline;
                var knot0 = spline[0];
                var knot1 = spline[1];

                //핵심: 유니티의 로컬 Tangent 값에 자신의 Position을 더한 뒤, 월드 좌표로 변환!
                Vector3 p1World = link.attachedSpline.transform.TransformPoint((Vector3)(knot0.Position + knot0.TangentOut));
                Vector3 p2World = link.attachedSpline.transform.TransformPoint((Vector3)(knot1.Position + knot1.TangentIn));

                lData.cx1 = Mathf.Round(p1World.x * 100f) / 100f;
                lData.cz1 = Mathf.Round(p1World.z * 100f) / 100f;
                lData.cx2 = Mathf.Round(p2World.x * 100f) / 100f;
                lData.cz2 = Mathf.Round(p2World.z * 100f) / 100f;
            }
            else
            {
                lData.type = 0;
                lData.cx1 = 0; lData.cz1 = 0;
                lData.cx2 = 0; lData.cz2 = 0;
            }

            mapData.links.Add(lData);
        }

        // 3. JSON 변환 및 저장
        string jsonOutput = JsonUtility.ToJson(mapData, true);
        string filePath = Application.dataPath + "/MapData.json";
        File.WriteAllText(filePath, jsonOutput);

        Debug.Log($"맵 JSON 파일 생성 완료! 경로: {filePath}");
        AssetDatabase.Refresh();
    }
}
#endif