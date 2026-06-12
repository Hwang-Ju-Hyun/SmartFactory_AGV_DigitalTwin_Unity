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
    public GameObject linkPrefab;

    public void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            m_NetworkIDToGameObjectMap = new Dictionary<UInt32, GameObject>();

            agvPrefab = Resources.Load<GameObject>("AGV_Prefab");
            defaultPrefab= Resources.Load<GameObject>("Default_Prefab");
            nodePrefab = Resources.Load<GameObject>("Node_Prefab");            
            linkPrefab = Resources.Load<GameObject>("Link_Prefab");
            if (agvPrefab == null || defaultPrefab == null|| nodePrefab==null||linkPrefab==null)
            {
                Debug.LogError("[RenderManager] Resources 폴더에서 프리팹을 로드하는 데 실패");
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

        Material m = Representaion_3D.GetComponent<MeshRenderer>().material;
        if (_networkID == 15)
        {
            m.color = Color.oldLace;
        }
        if (_networkID == 14)
        {
            m.color = Color.lightSalmon;
        }
        if (_networkID == 13)
        {
            m.color = Color.darkGoldenRod;
        }
        if (_networkID == 12)
        {
            m.color = Color.indigo;
        }
        if (_networkID == 11)
        {
            m.color = Color.violet;
        }
        if (_networkID == 10)
        {
            m.color = Color.plum;
        }
        if (_networkID == 9)
        {
            m.color = Color.chartreuse;
        }
        if (_networkID == 8)
        {
            m.color = Color.maroon;
        }
        if (_networkID == 7)
        {
            m.color = Color.ghostWhite;
        }
        if (_networkID == 6)
        {
            m.color = Color.coral;
        }
        if (_networkID == 5)
        {
            m.color = Color.darkTurquoise;
        }
        if (_networkID == 4)
        {
            m.color = Color.purple;
        }
        if (_networkID == 3)
        {
            m.color = Color.red;
        }
        if (_networkID == 2)
        {
            m.color = Color.green;            
        }
        if (_networkID == 1)
        {
            m.color = Color.black;
        }
    }
    public void UpdateObjectPosition(UInt32 _networkID, Vector2 _position,Quaternion _rot)
    { 
        if(m_NetworkIDToGameObjectMap.ContainsKey(_networkID))
        {
            m_NetworkIDToGameObjectMap[_networkID].transform.position = new Vector3(_position.x, 0.0f, _position.y);
            m_NetworkIDToGameObjectMap[_networkID].transform.rotation = _rot;
        }
    }

    public void MapBuild(Dictionary<UInt32, Node> _nodes,List<Link> _links)
    {        
        foreach (Node node in _nodes.Values)
        {
            Vector3 nodePos = new Vector3(node.m_PosX, 0.05f, node.m_PosY);
            GameObject nodeObj = Instantiate(nodePrefab, nodePos, Quaternion.identity);
            nodeObj.name = $"Node_[{node.m_Id}]_Type_{node.type}";
            Material m = nodeObj.GetComponent<MeshRenderer>().material;
            if (node.m_Id == 11|| node.m_Id == 7)
            {
                m.color = Color.black;
            }
            if (node.m_Id == 15 || node.m_Id == 16)
            {
                m.color = Color.maroon;
            }
            if (node.m_Id == 5 || node.m_Id == 1)
            {
                m.color = Color.green;
            }
            if (node.m_Id == 2 || node.m_Id == 49)
            {
                m.color = Color.red;
            }
            if (node.m_Id == 9 || node.m_Id == 10)
            {
                m.color = Color.purple;
            }
            if (node.m_Id == 21 || node.m_Id == 4)
            {
                m.color = Color.darkTurquoise;
            }
            if (node.m_Id == 6 || node.m_Id == 23)
            {
                m.color = Color.coral;
            }
            if (node.m_Id == 12 || node.m_Id == 24)
            {
                m.color = Color.ghostWhite;
            }            
            if (node.m_Id == 43 || node.m_Id == 17)
            {
                m.color = Color.chartreuse;
            }
            if (node.m_Id == 47 || node.m_Id == 45)
            {
                m.color = Color.plum;
            }
            if (node.m_Id == 22 || node.m_Id == 42)
            {
                m.color = Color.violet;
            }
            if (node.m_Id == 46 || node.m_Id == 43)
            {
                m.color = Color.indigo;
            }
            if (node.m_Id == 50 || node.m_Id == 41)
            {
                m.color = Color.darkGoldenRod;
            }
            if (node.m_Id == 13 || node.m_Id == 39)
            {
                m.color = Color.lightSalmon;
            }
            if (node.m_Id == 48 || node.m_Id == 44)
            {
                m.color = Color.oldLace;
            }
        }        

        foreach (Link link in _links)
        {
            Node fromNode = _nodes[link.m_FromNodeID];
            Node toNode = _nodes[link.m_ToNodeID];            
            
            Vector3 startPos= new Vector3(fromNode.m_PosX,0.05f,fromNode.m_PosY);
            Vector3 endPos = new Vector3(toNode.m_PosX, 0.05f, toNode.m_PosY);

            GameObject linkObj = Instantiate(linkPrefab, startPos, Quaternion.identity);            

            LineRenderer lr =linkObj.GetComponent<LineRenderer>();

            //LineRenderer는 현재 로컬좌표기준 
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, linkObj.transform.InverseTransformPoint(endPos));
        }       
    }
}