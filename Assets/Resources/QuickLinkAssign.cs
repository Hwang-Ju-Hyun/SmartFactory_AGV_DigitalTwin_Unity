#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class QuickLinker : MonoBehaviour
{
    // 단축키: Ctrl + L
    [MenuItem("AGV Tools/단축키 연결/단방향 링크 만들기 (From -> To) %l")]
    public static void CreateOneWayLink()
    {
        if (Selection.gameObjects.Length != 2)
        {
            Debug.LogWarning(" 링크를 연결할 노드를 '딱 2개'만 선택해주세요!");
            return;
        }

        // 유니티에서 Selection.activeGameObject는 '가장 마지막'에 클릭한 오브젝트입니다.
        // 즉 먼저 누른 게 From, 나중에 누른 게 To가 됨
        GameObject toObj = Selection.activeGameObject;
        GameObject fromObj = (Selection.gameObjects[0] == toObj) ? Selection.gameObjects[1] : Selection.gameObjects[0];

        MapNode fromNode = fromObj.GetComponent<MapNode>();
        MapNode toNode = toObj.GetComponent<MapNode>();

        if (fromNode == null || toNode == null)
        {
            Debug.LogWarning("선택한 오브젝트가 MapNode가 아닙니다!");
            return;
        }

        CreateLink(fromNode, toNode);
    }

    // 단축키: Ctrl + Shift + L (양방향을 한 번에 만들고 싶을 때)
    [MenuItem("AGV Tools/단축키 연결/양방향 링크 만들기 %q")]
    public static void CreateTwoWayLink()
    {
        if (Selection.gameObjects.Length != 2) return;

        GameObject toObj = Selection.activeGameObject;
        GameObject fromObj = (Selection.gameObjects[0] == toObj) ? Selection.gameObjects[1] : Selection.gameObjects[0];

        MapNode fromNode = fromObj.GetComponent<MapNode>();
        MapNode toNode = toObj.GetComponent<MapNode>();

        if (fromNode != null && toNode != null)
        {
            CreateLink(fromNode, toNode);
            CreateLink(toNode, fromNode); // 반대 방향도 추가
        }
    }

    // 단축키: Ctrl + R (방향을 실수로 반대로 이었을 때 휙 뒤집기)
    [MenuItem("AGV Tools/단축키 연결/선택한 링크 방향 뒤집기 %r")]
    public static void ReverseLink()
    {
        bool changed = false;
        foreach (GameObject obj in Selection.gameObjects)
        {
            MapLink link = obj.GetComponent<MapLink>();
            if (link != null)
            {
                Undo.RecordObject(link, "Reverse Link Direction");
                MapNode temp = link.fromNode;
                link.fromNode = link.toNode;
                link.toNode = temp;
                changed = true;
            }
        }

        if (changed) Debug.Log(" 링크 방향을 뒤집었습니다!");
    }

    // 실제 링크 오브젝트를 생성하는 내부 함수
    private static void CreateLink(MapNode from, MapNode to)
    {
        string linkName = $"Link_{from.nodeID}_to_{to.nodeID}";

        if (GameObject.Find(linkName) != null)
        {
            Debug.Log($"[알림] {linkName} 은 이미 존재합니다.");
            return;
        }

        GameObject linkRoot = GameObject.Find("Links_Manual");
        if (linkRoot == null)
        {
            linkRoot = new GameObject("Links_Manual");
        }

        GameObject linkObj = new GameObject(linkName);
        linkObj.transform.SetParent(linkRoot.transform);
        linkObj.transform.position = (from.transform.position + to.transform.position) / 2.0f;

        MapLink linkData = linkObj.AddComponent<MapLink>();
        linkData.fromNode = from;
        linkData.toNode = to;

        Undo.RegisterCreatedObjectUndo(linkObj, "Create Quick Link");
        Debug.Log($" {linkName} 생성 완료!");
    }
}
#endif