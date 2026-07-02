#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
public class NodeValid : MonoBehaviour
{
    [MenuItem("AGV Tools/Node Valid")]
    public static void NodeValidation()
    {
        MapNode[] allNodes = FindObjectsOfType<MapNode>();
        if (allNodes.Length == 0)
        {
            Debug.LogError("노드가 존재하지 않습니다.");
            return;
        }
        List<GameObject> duplicatedObjects = new List<GameObject>();
        for (int i = 0; i < allNodes.Length; i++)
        {
            for (int j = i+1; j < allNodes.Length; j++)
            {                
                MapNode curNode = allNodes[i];
                MapNode nextNode=allNodes[j];

                float distance = Vector3.Distance(curNode.transform.position, nextNode.transform.position);
                
                if (distance < 0.5f)
                {                    
                    MeshRenderer renderer = nextNode.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        // GPU에 던질 프로퍼티 블록 생성
                        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(propBlock);

                        // 빨간색 지정
                        propBlock.SetColor("_Color", Color.red);

                        // 해당 렌더러에만 덮어쓰기
                        renderer.SetPropertyBlock(propBlock);

                        Debug.LogError($"겹친 노드 발견 ID: {curNode.nodeID} ...", nextNode.gameObject);
                    }                    
                    if (!nextNode.name.StartsWith("[DUP]"))
                    {
                        Undo.RecordObject(nextNode.gameObject, "Rename Duplicate Node");
                        nextNode.name = "[DUP] " + nextNode.name;
                    }

                    // 선택 리스트에 추가 (나중에 지우기 편하게)
                    if (!duplicatedObjects.Contains(nextNode.gameObject))
                    {
                        duplicatedObjects.Add(nextNode.gameObject);
                    }
                }
            }
        }
        // 4. 결과 처리
        if (duplicatedObjects.Count > 0)
        {
            // 하이에라키 창에서 겹친 노드들을 일제히 '선택 상태'로 만듦
            Selection.objects = duplicatedObjects.ToArray();

            EditorUtility.DisplayDialog("노드 검사 결과",
                $"총 {duplicatedObjects.Count}개의 중복(겹침) 노드를 발견\n","확인");
        }
        else
        {
            EditorUtility.DisplayDialog("노드 검사 결과", "겹친 노드가 하나도 없습니다.", "확인");
        }
    }
}

#endif