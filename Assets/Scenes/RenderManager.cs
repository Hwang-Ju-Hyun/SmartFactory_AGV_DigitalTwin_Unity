using System;
using System.Collections.Generic;
using UnityEngine;

public class RenderManager : MonoBehaviour
{
    private const float PositionLogIntervalSeconds = 1.0f;
    private const float PositionLogDistance = 0.01f;
    private const float VisionVerticalOffset = 0.08f;
    private const float VisionScaleMultiplier = 1.05f;

    private static readonly Color VisionMeasuredColor = Color.cyan;
    private static readonly Color VisionHeldColor = Color.yellow;

    private readonly Dictionary<UInt32, GameObject> m_NetworkObjects =
        new Dictionary<UInt32, GameObject>();
    private readonly Dictionary<UInt32, Vector2> m_LastLoggedPosition =
        new Dictionary<UInt32, Vector2>();
    private readonly Dictionary<UInt32, float> m_LastPositionLogTime =
        new Dictionary<UInt32, float>();
    private readonly Dictionary<UInt32, GameObject> m_VisionObjects =
        new Dictionary<UInt32, GameObject>();
    private readonly Dictionary<UInt32, List<Material>> m_VisionMaterials =
        new Dictionary<UInt32, List<Material>>();
    private readonly Dictionary<UInt32, VisionTrackingState> m_LastVisionState =
        new Dictionary<UInt32, VisionTrackingState>();
    private readonly Dictionary<UInt32, bool> m_LastVisionPoseValid =
        new Dictionary<UInt32, bool>();
    private readonly List<GameObject> m_MapObjects = new List<GameObject>();

    public static RenderManager Instance { get; private set; }

    public GameObject agvPrefab;
    public GameObject defaultPrefab;
    public GameObject nodePrefab;
    public GameObject linkPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        agvPrefab = agvPrefab != null ? agvPrefab : Resources.Load<GameObject>("AGV_Prefab");
        defaultPrefab = defaultPrefab != null ? defaultPrefab : Resources.Load<GameObject>("Default_Prefab");
        nodePrefab = nodePrefab != null ? nodePrefab : Resources.Load<GameObject>("Node_Prefab");
        linkPrefab = linkPrefab != null ? linkPrefab : Resources.Load<GameObject>("Link_Prefab");

        if (agvPrefab == null || defaultPrefab == null || nodePrefab == null || linkPrefab == null)
        {
            Debug.LogError("[Viewer] One or more Resources prefabs could not be loaded.");
        }
    }

    public bool OnNetworkObjectCreated(
        UInt32 networkID,
        UInt32 classID,
        Vector2 position,
        float headingRadians)
    {
        if (m_NetworkObjects.ContainsKey(networkID))
        {
            Debug.LogError($"[Viewer] NetworkID {networkID} already exists.");
            return false;
        }

        GameObject targetPrefab = classID == (UInt32)CLASS_ID.OBJ_AGV ? agvPrefab : defaultPrefab;
        if (targetPrefab == null)
        {
            Debug.LogError($"[Viewer] No prefab is registered for ClassID {classID}.");
            return false;
        }

        GameObject representation = Instantiate(targetPrefab, Vector3.zero, Quaternion.identity);
        representation.name = $"3D_NetObj_[{networkID}]";
        m_NetworkObjects.Add(networkID, representation);
        ApplyPose(representation, position, headingRadians);

        Renderer renderer = representation.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = GetColorByID(networkID);
        }

        m_LastLoggedPosition[networkID] = position;
        m_LastPositionLogTime[networkID] = Time.unscaledTime;
        Debug.Log($"[Viewer] AGV {networkID} created at x={position.x:F2}, z={position.y:F2}.");
        return true;
    }

    public void UpdateObjectPosition(UInt32 networkID, Vector2 position, float headingRadians)
    {
        if (!m_NetworkObjects.TryGetValue(networkID, out GameObject representation))
        {
            Debug.LogWarning($"[Viewer] RT_UPDATE ignored for unknown NetworkID {networkID}.");
            return;
        }

        ApplyPose(representation, position, headingRadians);
        MaybeLogPosition(networkID, position);
    }

    public void RemoveNetworkObject(UInt32 networkID)
    {
        if (!m_NetworkObjects.TryGetValue(networkID, out GameObject representation))
        {
            return;
        }

        Destroy(representation);
        m_NetworkObjects.Remove(networkID);
        m_LastLoggedPosition.Remove(networkID);
        m_LastPositionLogTime.Remove(networkID);
        Debug.Log($"[Viewer] Network object {networkID} destroyed.");
    }

    public void UpdateVisionObservation(VisionObservationPacket packet)
    {
        bool hadState = m_LastVisionState.TryGetValue(
            packet.AgvID,
            out VisionTrackingState previousState);
        bool hadPoseValid = m_LastVisionPoseValid.TryGetValue(
            packet.AgvID,
            out bool previousPoseValid);
        bool displayStateChanged = !hadState || !hadPoseValid ||
                                   previousState != packet.TrackingState ||
                                   previousPoseValid != packet.PoseValid;

        m_LastVisionState[packet.AgvID] = packet.TrackingState;
        m_LastVisionPoseValid[packet.AgvID] = packet.PoseValid;

        bool shouldShow = packet.PoseValid &&
                          packet.TrackingState != VisionTrackingState.Lost;
        if (!shouldShow)
        {
            if (m_VisionObjects.TryGetValue(packet.AgvID, out GameObject hiddenObject))
            {
                hiddenObject.SetActive(false);
            }

            if (displayStateChanged)
            {
                Debug.Log(
                    $"[Viewer] Vision AGV {packet.AgvID} hidden " +
                    $"state={packet.TrackingState}, valid={packet.PoseValid}, " +
                    $"sequence={packet.TransportSequence}, ageMs={packet.ServerReceiveAgeMs}.");
            }

            return;
        }

        bool created = false;
        if (!m_VisionObjects.TryGetValue(packet.AgvID, out GameObject representation))
        {
            representation = CreateVisionObject(packet.AgvID);
            created = true;
        }

        if (!representation.activeSelf)
        {
            representation.SetActive(true);
        }

        ApplyPose(
            representation,
            new Vector2(packet.ServerX, packet.ServerZ),
            packet.HeadingRadians,
            VisionVerticalOffset);

        if (created || displayStateChanged)
        {
            Color color = packet.TrackingState == VisionTrackingState.Measured
                ? VisionMeasuredColor
                : VisionHeldColor;
            ApplyVisionColor(packet.AgvID, color);
            Debug.Log(
                $"[Viewer] Vision AGV {packet.AgvID} visible " +
                $"state={packet.TrackingState}, sequence={packet.TransportSequence}, " +
                $"ageMs={packet.ServerReceiveAgeMs}.");
        }
    }

    public void MapBuild(Dictionary<UInt32, Node> nodes, List<Link> links)
    {
        if (nodes == null || links == null)
        {
            throw new ArgumentNullException(nodes == null ? nameof(nodes) : nameof(links));
        }

        ClearMapObjects();

        foreach (Node node in nodes.Values)
        {
            Vector3 nodePosition = new Vector3(node.m_PosX, 0.05f, node.m_PosY);
            GameObject nodeObject = Instantiate(nodePrefab, nodePosition, Quaternion.identity);
            nodeObject.name = $"Node_[{node.m_Id}]";
            m_MapObjects.Add(nodeObject);

            MapNode mapNode = nodeObject.GetComponent<MapNode>();
            if (mapNode != null)
            {
                mapNode.nodeID = (int)node.m_Id;
            }
        }

        foreach (Link link in links)
        {
            if (!nodes.TryGetValue(link.m_FromNodeID, out Node fromNode) ||
                !nodes.TryGetValue(link.m_ToNodeID, out Node toNode))
            {
                Debug.LogWarning($"[Viewer] Link {link.m_Id} references an unknown node.");
                continue;
            }

            Vector3 startPosition = new Vector3(fromNode.m_PosX, 0.05f, fromNode.m_PosY);
            Vector3 endPosition = new Vector3(toNode.m_PosX, 0.05f, toNode.m_PosY);
            GameObject linkObject = Instantiate(linkPrefab, startPosition, Quaternion.identity);
            linkObject.name = $"Link_[{link.m_Id}]";
            m_MapObjects.Add(linkObject);

            LineRenderer lineRenderer = linkObject.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                Debug.LogWarning($"[Viewer] Link prefab has no LineRenderer for link {link.m_Id}.");
                continue;
            }

            lineRenderer.useWorldSpace = true;
            if (link.m_Type == 1)
            {
                DrawBezierLink(lineRenderer, startPosition, endPosition, link);
            }
            else
            {
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, startPosition);
                lineRenderer.SetPosition(1, endPosition);
            }
        }
    }

    public Color GetColorByID(UInt32 networkID)
    {
        UnityEngine.Random.State oldState = UnityEngine.Random.state;
        UnityEngine.Random.InitState((int)networkID);
        Color color = UnityEngine.Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);
        UnityEngine.Random.state = oldState;
        return color;
    }

    private GameObject CreateVisionObject(UInt32 agvID)
    {
        if (agvPrefab == null)
        {
            throw new InvalidOperationException("AGV prefab is unavailable for the Vision overlay.");
        }

        GameObject representation = Instantiate(agvPrefab, Vector3.zero, Quaternion.identity);
        representation.name = $"Vision_AGV_[{agvID}]";
        representation.transform.localScale *= VisionScaleMultiplier;

        foreach (Collider collider in representation.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (Rigidbody rigidbody in representation.GetComponentsInChildren<Rigidbody>(true))
        {
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = false;
        }

        List<Material> instanceMaterials = new List<Material>();
        foreach (Renderer renderer in representation.GetComponentsInChildren<Renderer>(true))
        {
            // renderer.materials creates per-renderer instances. Changing the
            // Vision colors therefore cannot modify the prefab or planned AGV.
            foreach (Material material in renderer.materials)
            {
                if (material != null)
                {
                    instanceMaterials.Add(material);
                }
            }
        }

        m_VisionObjects.Add(agvID, representation);
        m_VisionMaterials.Add(agvID, instanceMaterials);
        return representation;
    }

    private void ApplyVisionColor(UInt32 agvID, Color color)
    {
        if (!m_VisionMaterials.TryGetValue(agvID, out List<Material> materials))
        {
            return;
        }

        foreach (Material material in materials)
        {
            if (material != null)
            {
                material.color = color;
            }
        }
    }

    private static void ApplyPose(
        GameObject representation,
        Vector2 position,
        float headingRadians,
        float verticalOffset = 0.0f)
    {
        representation.transform.position = new Vector3(position.x, verticalOffset, position.y);
        float angleDegrees = -(headingRadians * Mathf.Rad2Deg) + 90f;
        representation.transform.rotation = Quaternion.Euler(0f, angleDegrees, 0f);
    }

    private void MaybeLogPosition(UInt32 networkID, Vector2 position)
    {
        if (networkID != 1)
        {
            return;
        }

        float now = Time.unscaledTime;
        bool intervalElapsed = !m_LastPositionLogTime.TryGetValue(networkID, out float lastLogTime) ||
                               now - lastLogTime >= PositionLogIntervalSeconds;
        bool moved = !m_LastLoggedPosition.TryGetValue(networkID, out Vector2 lastPosition) ||
                     Vector2.Distance(lastPosition, position) >= PositionLogDistance;
        if (!intervalElapsed || !moved)
        {
            return;
        }

        m_LastPositionLogTime[networkID] = now;
        m_LastLoggedPosition[networkID] = position;
        Debug.Log($"[Viewer] AGV 1 position x={position.x:F2}, z={position.y:F2}.");
    }

    private static void DrawBezierLink(
        LineRenderer lineRenderer,
        Vector3 startPosition,
        Vector3 endPosition,
        Link link)
    {
        const int resolution = 20;
        lineRenderer.positionCount = resolution + 1;

        Vector3 control1 = new Vector3(link.m_CX1, 0.05f, link.m_CZ1);
        Vector3 control2 = new Vector3(link.m_CX2, 0.05f, link.m_CZ2);
        for (int i = 0; i <= resolution; i++)
        {
            float t = i / (float)resolution;
            float u = 1.0f - t;
            float tt = t * t;
            float uu = u * u;
            Vector3 position =
                (uu * u * startPosition) +
                (3f * uu * t * control1) +
                (3f * u * tt * control2) +
                (tt * t * endPosition);
            lineRenderer.SetPosition(i, position);
        }
    }

    private void ClearMapObjects()
    {
        foreach (GameObject mapObject in m_MapObjects)
        {
            if (mapObject != null)
            {
                Destroy(mapObject);
            }
        }

        m_MapObjects.Clear();
    }

    private void OnDestroy()
    {
        foreach (List<Material> materials in m_VisionMaterials.Values)
        {
            foreach (Material material in materials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
        }

        m_VisionMaterials.Clear();
        m_VisionObjects.Clear();
        m_LastVisionState.Clear();
        m_LastVisionPoseValid.Clear();

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
