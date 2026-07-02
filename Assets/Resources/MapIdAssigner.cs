#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class MapIdAssigner : MonoBehaviour
{
    // 유니티 상단 메뉴에 자동으로 ID를 먹여주는 버튼을 만듭니다.
    [MenuItem("AGV Tools/Auto Assign Node IDs")]
    public static void AssignIDs()
    {
        // 1. 씬에 배치된 모든 MapNode 컴포넌트를 싹 긁어옵니다.
        MapNode[] allNodes = FindObjectsOfType<MapNode>();
        if (allNodes.Length == 0)
        {
            Debug.LogWarning(" 씬에 MapNode(Node_Prefab)가 하나도 없습니다.");
            return;
        }

        // 2. 리스트로 옮긴 뒤, 맵 안에서의 '물리적 위치'를 기준으로 정렬합니다.
        List<MapNode> nodeList = new List<MapNode>(allNodes);

        nodeList.Sort((a, b) =>
        {
            // 디버깅하기 가장 좋게 [아래쪽(Z 마이너스) -> 위쪽(Z 플러스)] 순서로 정렬하되,
            // 같은 라인에 있다면 [왼쪽(X 마이너스) -> 오른쪽(X 플러스)] 순서로 1번부터 번호를 매깁니다.
            if (Mathf.Abs(a.transform.position.z - b.transform.position.z) > 0.1f)
            {
                return a.transform.position.z.CompareTo(b.transform.position.z);
            }
            return a.transform.position.x.CompareTo(b.transform.position.x);
        });                

        // 3. 정렬된 순서대로 ID를 1번부터 쭈욱 부여합니다.
        int currentID = 1;
        foreach (MapNode node in nodeList)
        {
            // 매우 중요: 유니티 Undo 시스템에 등록합니다. 
            // 혹시라도 번호 매기기가 맘에 안 들면 Ctrl + Z로 한 방에 되돌릴 수 있게 해줍니다.
            Undo.RecordObject(node, "Auto Assign Node ID");
            Undo.RecordObject(node.gameObject, "Auto Rename Node");

            // 고유 ID 입력
            node.nodeID = currentID;

            // 계층 구조 창(Hierarchy)에서 보기 좋게 이름도 세 자릿수로 정렬 (Node_001, Node_015 등)
            node.gameObject.name = $"Node_{currentID:D3}";

            currentID++;
        }

        MapLink[] allLinks= FindObjectsByType<MapLink>();

        foreach (MapLink link in allLinks)
        {
            // 인스펙터에서 from/to 노드가 연결되어 있는지 확인
            if (link.fromNode == null || link.toNode == null)
            {
                Debug.LogWarning($"{link.gameObject.name}에 연결된 Node 레퍼런스가 누락되었습니다. 인스펙터를 확인해주세요.");
                continue;
            }

            Undo.RecordObject(link.gameObject, "Auto Rename Link");            
            link.gameObject.name = $"Link_{link.fromNode.nodeID:D3}_to_{link.toNode.nodeID:D3}";
        }

        // 4. 씬에 변경 사항이 생겼음을 유니티 엔진에 알려서 저장 상태로 만듭니다.
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($" 작업 완료! 총 {nodeList.Count}개의 노드에 1번부터 ID가 순차적으로 자동 부여되었습니다.");
    }
}
#endif