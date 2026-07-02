/*#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
public class LinkAutoAssignID:MonoBehaviour
{
    [MenuItem("AGV Tools/Auto Assign Link IDs")]
    public static void AssignLinkIDs()
    {
        MapLink[] allLinks = FindObjectsOfType<MapLink>();
        if (allLinks.Length == 0)
        {
            Debug.LogWarning(" 씬에 MapLink(LInk_Prefab)가 하나도 없습니다.");
            return;
        }

        foreach (MapLink link in allLinks)
        {
            link.from
        }
    }
};
#endif*/