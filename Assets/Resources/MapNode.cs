using Unity.VisualScripting;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    [Header("노드 고유 ID")]
    public int nodeID;
    
    private void OnDrawGizmos()
    {
        //Gizmos.color = Color.cyan;
        //Gizmos.DrawWireSphere(transform.position, 0.4f);

#if UNITY_EDITOR
        // 씬 뷰에서 노드 위에 파란색으로 ID를 띄워줌
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.6f, "[ " + nodeID + " ]");        
#endif
    }
}