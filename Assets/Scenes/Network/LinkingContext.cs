using System;
using System.Collections.Generic;
using UnityEditor.PackageManager;
using UnityEngine;

public class LinkingContext
{
    private Dictionary<UInt32, Object> m_NewtorkIDToObjectDictionaty;
    public LinkingContext()
    {
        m_NewtorkIDToObjectDictionaty= new Dictionary<UInt32, Object>();
    }
    public Object GetObject(UInt32 _networkID)
    {
        if(!m_NewtorkIDToObjectDictionaty.ContainsKey(_networkID))
        {            
            return null;
        }
        return m_NewtorkIDToObjectDictionaty[_networkID];
    }
    public void AddObject(UInt32 _networkID,Object _obj)
    {
        if (m_NewtorkIDToObjectDictionaty.ContainsKey(_networkID))
        {
            Debug.LogError($"[LinkingContext] 에 해당 ({_networkID})는 이미 존재합니다 삽입할 수 없습니다!");
            return;
        }
        m_NewtorkIDToObjectDictionaty.Add(_networkID, _obj);
    }
}