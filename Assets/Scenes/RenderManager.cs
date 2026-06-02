using UnityEngine;
using System.Collections.Generic;
using System;
public class RenderManager:MonoBehaviour
{
    private Dictionary<UInt32, GameObject> m_NetworkIDToGameObjectMap;
    public static RenderManager Instance { get; private set; }
    
    public GameObject agvPrefab;
    public GameObject defaultPrefab;
    public GameObject nodePrefab;

    public void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            m_NetworkIDToGameObjectMap = new Dictionary<UInt32, GameObject>();

            agvPrefab = Resources.Load<GameObject>("AGV_Prefab");
            defaultPrefab= Resources.Load<GameObject>("Default_Prefab");
            nodePrefab = Resources.Load<GameObject>("Node_Prefab");


            if (agvPrefab == null || defaultPrefab == null|| nodePrefab==null)
            {
                Debug.LogError("[RenderManager] Resources 폴더에서 프리팹을 로드하는 데 실패했습니다!");
            }
        }
    }
    public void OnNetworkObjectCreated(UInt32 _networkID, UInt32 _classIDObject)
    {
        if (m_NetworkIDToGameObjectMap.ContainsKey(_networkID))
        {
            Debug.LogError($"[RenderManager] 이미 존재하는 NetworkID 입니다: {_networkID}");
            return;
        }
        GameObject targetPrefab = defaultPrefab;
        if (_classIDObject == (UInt32)CLASS_ID.OBJ_AGV)
        {
            targetPrefab = agvPrefab;
        }
        if (targetPrefab == null)
        {
            Debug.LogError($"[RenderManager] ClassID({_classIDObject})에 대응하는 프리팹이 등록되지 않았습니다.");
            return;
        }

        GameObject Representaion_3D = Instantiate(targetPrefab, Vector3.zero, Quaternion.identity);
        Representaion_3D.name = $"3D_NetObj_[{_networkID}]";
        m_NetworkIDToGameObjectMap.Add(_networkID, Representaion_3D);
    }
    public void UpdateObjectPosition(UInt32 _networkID, Vector2 _position,Quaternion _rot)
    { 
        if(m_NetworkIDToGameObjectMap.ContainsKey(_networkID))
        {
            m_NetworkIDToGameObjectMap[_networkID].transform.position = new Vector3(_position.x, 0.0f, _position.y);
            m_NetworkIDToGameObjectMap[_networkID].transform.rotation = _rot;
        }
    }

    public void MapBuild(List<Node> _nodes,List<Link> _links)
    {        
        foreach(Node node in _nodes)
        {
            Vector3 nodePos = new Vector3(node.m_PosX, 0.05f, node.m_PosY);
            GameObject nodeObj = Instantiate(nodePrefab, nodePos, Quaternion.identity);
            nodeObj.name = $"Node_[{node.m_Id}]_Type_{node.type}";
        }
    }
}