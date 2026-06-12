using UnityEngine;
using System;
using System.Collections.Generic;

public class ObjectRegistry
{
    public static ObjectRegistry Instance { get; private set; }

    public static void StaticInit()
    {
        Instance = new ObjectRegistry();
    }
    private Dictionary<UInt32, Func<Object>> m_NameToObjectCreationFuncMap;
    private ObjectRegistry()
    {
        m_NameToObjectCreationFuncMap = new Dictionary<UInt32, Func<Object>>();
    }
    
    public void RegistCreateFunction(UInt32 _objClassName, Func<Object> _createFunc)
    {
        if (m_NameToObjectCreationFuncMap.ContainsKey(_objClassName))
        {
            Debug.LogError($"[ObjectRegistry] 이미 등록된 ClassID 입니다: {_objClassName}");
            return;
        }
        m_NameToObjectCreationFuncMap.Add(_objClassName, _createFunc);
    }
    public Object CreateObject(UInt32 _objClassName)
    {        
        if (!m_NameToObjectCreationFuncMap.ContainsKey(_objClassName))
        {
            Debug.LogError($"[ObjectRegistry] 등록되지 않은 ClassID({_objClassName})를 생성하려고 합니다.");
            return null;
        }
        Func<Object> createFunc = m_NameToObjectCreationFuncMap[_objClassName];

        return createFunc.Invoke();
    }
}
