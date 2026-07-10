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
        m.color = GetColorByID(_networkID);
    }
    public void UpdateObjectPosition(UInt32 _networkID, Vector2 _position,Quaternion _rot)
    { 
        if(m_NetworkIDToGameObjectMap.ContainsKey(_networkID))
        {
            m_NetworkIDToGameObjectMap[_networkID].transform.position = new Vector3(_position.x, 0.0f, _position.y);
            m_NetworkIDToGameObjectMap[_networkID].transform.rotation = _rot;
        }
    }
    public void UpdateObjectPosition(UInt32 _networkID, Vector2 _position, float _rot)
    {
        if (m_NetworkIDToGameObjectMap.ContainsKey(_networkID))
        {
            m_NetworkIDToGameObjectMap[_networkID].transform.position = new Vector3(_position.x, 0.0f, _position.y);
            float angleDeg = _rot * Mathf.Rad2Deg;
            angleDeg = -(angleDeg) + 90f;
            Quaternion targetRot = Quaternion.Euler(0f, angleDeg, 0f);
            m_NetworkIDToGameObjectMap[_networkID].transform.rotation = targetRot;
        }
    }

    public void MapBuild(Dictionary<UInt32, Node> _nodes,List<Link> _links)
    {        
        foreach (Node node in _nodes.Values)
        {
            Vector3 nodePos = new Vector3(node.m_PosX, 0.05f, node.m_PosY);
            GameObject nodeObj = Instantiate(nodePrefab, nodePos, Quaternion.identity);
            nodeObj.name = $"Node_[{node.m_Id}]_Type_{node.type}";
            MapNode mapNode = nodeObj.GetComponent<MapNode>();
            if (mapNode != null)
            {
                mapNode.nodeID = (int)node.m_Id; 
            }
            Material m = nodeObj.GetComponent<MeshRenderer>().material;           
        }

        // 2. 링크 생성 (직선과 곡선 분기 처리 완벽 적용!)
        foreach (Link link in _links)
        {
            Node fromNode = _nodes[link.m_FromNodeID];
            Node toNode = _nodes[link.m_ToNodeID];

            // Y축을 0.05f로 띄워서 바닥에 파묻히지 않게 함
            Vector3 startPos = new Vector3(fromNode.m_PosX, 0.05f, fromNode.m_PosY);
            Vector3 endPos = new Vector3(toNode.m_PosX, 0.05f, toNode.m_PosY);

            GameObject linkObj = Instantiate(linkPrefab, startPos, Quaternion.identity);
            LineRenderer lr = linkObj.GetComponent<LineRenderer>();

            //월드 좌표계 사용 강제 설정 (곡선 그리기 훨씬 편해집니다)
            lr.useWorldSpace = true;

            //곡선일 때와 직선일 때를 나눠서 선을 그립니다.
            if (link.m_Type == 1)
            {
                int resolution = 20; // 선을 20조각으로 쪼개서 부드럽게 만듦
                lr.positionCount = resolution + 1;

                // C++ 서버에서 받은 제어점 데이터 
                // (주의: 서버의 Z값이 유니티 2D 탑뷰상 Y로 들어오고 있다면 m_CZ1을 Y자리에 넣으세요)
                Vector3 p0 = startPos;
                Vector3 p1 = new Vector3(link.m_CX1, 0.05f, link.m_CZ1);
                Vector3 p2 = new Vector3(link.m_CX2, 0.05f, link.m_CZ2);
                Vector3 p3 = endPos;

                Debug.DrawLine(p0, p1, Color.red, 10f);  // 출발점 -> 제어점1 (빨간선)
                Debug.DrawLine(p3, p2, Color.blue, 10f); // 도착점 -> 제어점2 (파란선)

                for (int i = 0; i <= resolution; i++)
                {
                    float t = i / (float)resolution;
                    float u = 1.0f - t;

                    float tt = t * t;
                    float uu = u * u;
                    float uuu = uu * u;
                    float ttt = tt * t;

                    // 3차 베지어 위치 계산 (C++ 서버와 100% 동일한 공식)
                    Vector3 pos = (uuu * p0) +
                                  (3f * uu * t * p1) +
                                  (3f * u * tt * p2) +
                                  (ttt * p3);

                    lr.SetPosition(i, pos);
                }
            }
            else // 직선 모드
            {
                lr.positionCount = 2;
                lr.SetPosition(0, startPos);
                lr.SetPosition(1, endPos);
            }
        }
    }
    public Color GetColorByID(UInt32 networkID)
    {
        UnityEngine.Random.State oldState = UnityEngine.Random.state;

        // 2. 네트워크 ID를 시드 값으로 설정합니다. 
        // (이렇게 하면 이 ID는 항상 같은 난수 패턴을 가집니다)
        UnityEngine.Random.InitState((int)networkID);

        Color randomColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);

        UnityEngine.Random.state = oldState;

        return randomColor;
    }
}


