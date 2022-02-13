using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PropyCopy
{
    private const string GuidField = "guid";
    private const string InstanceIDField = "instanceID";
    private const string RedComponent = "r";
    private const string GreenComponent = "g";
    private const string BlueComponent = "b";
    private const string AlphaComponent = "a";

    private static SerializedProperty _cachedProp;

    [InitializeOnLoadMethod]
    private static void InitialiseCopyUtil()
    {
        EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
    }

    private static void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
    {
        _cachedProp = property.Copy();
        menu.AddItem(new GUIContent("Copy"), false, OnCopyProperty);

        //only show the enabled menu option if the clipboard is valid json and,
        //if that json has multiple entries the property must be an object
        if (PasteComponentCheck(out var obj) && obj.Count > 1 == !ShouldProcessProp(property))
            menu.AddItem(new GUIContent("Paste"), false, OnPasteProperty);
        else
            menu.AddDisabledItem(new GUIContent("Paste"), false);
    }

    private static void OnCopyProperty()
    {
        var obj = new JObject();
        if (_cachedProp.hasVisibleChildren)
        {
            var nameLength = _cachedProp.name.Length + 1;

            obj = PropertyToJson(_cachedProp, _cachedProp.GetEndProperty(), nameLength);
        }
        else
        {
            obj[_cachedProp.name] = GetPropValue(_cachedProp);
        }

        if (obj.Count > 0) EditorGUIUtility.systemCopyBuffer = obj.ToString();
    }

    private static void OnPasteProperty()
    {
        var obj = JObject.Parse(EditorGUIUtility.systemCopyBuffer);
        var so = _cachedProp.serializedObject;
        if (_cachedProp.hasVisibleChildren)
        {
            JsonToProperty(obj, so, _cachedProp.name);
        }
        else
        {
            //Take the first value out of the first json field
            var tokenValue = obj.First?.First;
            SetPropValue(_cachedProp, tokenValue);
        }

        so.ApplyModifiedProperties();
    }


    [MenuItem("CONTEXT/Component/Copy All Fields", false, 900)]
    private static void OnCopyComponent(MenuCommand command)
    {
        JObject obj;
        using (var so = new SerializedObject(command.context))
        {
            var it = so.GetIterator();
            it.NextVisible(true); //skip script prop
            obj = PropertyToJson(it);
        }

        EditorGUIUtility.systemCopyBuffer = obj.ToString();
    }


    [MenuItem("CONTEXT/Component/Paste All Fields", true)]
    private static bool PasteComponentCheck(MenuCommand command)
    {
        return PasteComponentCheck(out _);
    }

    private static bool PasteComponentCheck(out JObject obj)
    {
        try
        {
            obj = JObject.Parse(EditorGUIUtility.systemCopyBuffer);
        }
        catch (Exception)
        {
            obj = null;
            return false;
        }

        return true;
    }

    [MenuItem("CONTEXT/Component/Paste All Fields", false, 900)]
    private static void OnPasteComponent(MenuCommand command)
    {
        var obj = JObject.Parse(EditorGUIUtility.systemCopyBuffer);
        using (var so = new SerializedObject(command.context))
        {
            JsonToProperty(obj, so);

            so.ApplyModifiedProperties();
        }
    }

    private static void JsonToProperty(JObject obj, SerializedObject so, string parentPropertyName = null)
    {
        foreach (var kvp in obj)
        {
            var path = kvp.Key;
            if (parentPropertyName != null)
                path = $"{parentPropertyName}.{path}";

            //try to find a property with the same property path
            var prop = so.FindProperty(path);
            if (prop == null)
                continue;
            SetPropValue(prop, kvp.Value);
        }
    }

    private static JObject PropertyToJson(SerializedProperty property, SerializedProperty endProperty = null, int pathStart = 0)
    {
        var obj = new JObject();
        while (property.NextVisible(true) && !SerializedProperty.EqualContents(property, endProperty))
        {
            if (!ShouldProcessProp(property))
                continue;

            var path = property.propertyPath.Substring(pathStart);
            obj[path] = GetPropValue(property);
        }

        return obj;
    }

    private static bool ShouldProcessProp(SerializedProperty property)
    {
        //skip the parent prop (e.g. Vector3) as we only care about the raw values (x,y,z)
        return !property.hasVisibleChildren;
    }

    /// <summary>
    ///     Gets the json representation out of a SerializedProperty
    /// </summary>
    /// <param name="property">The property to get the value from</param>
    /// <returns>The json token for the property's value</returns>
    /// <exception cref="ArgumentException">Throw when an unsupported PropertyType tries to get copied</exception>
    private static JToken GetPropValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Boolean:
                return property.boolValue;
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.Enum:
            case SerializedPropertyType.ArraySize:
                return property.intValue;
            case SerializedPropertyType.Float:
                return property.floatValue;
            case SerializedPropertyType.String:
                return property.stringValue;
            case SerializedPropertyType.ObjectReference:
                var objRef = property.objectReferenceValue;
                if (objRef == null)
                {
                    return null;
                }
                else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(objRef, out var guid, out long _))
                {
                    var objID = new JObject
                    {
                        [GuidField] = guid,
                        [InstanceIDField] = property.objectReferenceInstanceIDValue
                    };
                    return objID;
                }
                else
                {
                    return property.objectReferenceInstanceIDValue;
                }
            case SerializedPropertyType.Color:
                var col = property.colorValue;
                var objCol = new JObject
                {
                    [RedComponent] = col.r,
                    [GreenComponent] = col.g,
                    [BlueComponent] = col.b,
                    [AlphaComponent] = col.a
                };
                return objCol;
            default:
                throw new ArgumentException($"{property.propertyType} is not supported");
        }
    }

    /// <summary>
    ///     Sets the SerializedProperty value from a Json Value
    /// </summary>
    /// <param name="prop">The property to set</param>
    /// <param name="token">The value to set the property to</param>
    private static void SetPropValue(SerializedProperty prop, JToken token)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.Boolean when token.Type == JTokenType.Boolean:
                prop.boolValue = token.Value<bool>();
                break;
            case SerializedPropertyType.Integer when token.Type == JTokenType.Integer:
            case SerializedPropertyType.Enum when token.Type == JTokenType.Integer:
            case SerializedPropertyType.ArraySize when token.Type == JTokenType.Integer:
                prop.intValue = token.Value<int>();
                break;
            case SerializedPropertyType.Float when token.Type == JTokenType.Float:
                prop.floatValue = token.Value<float>();
                break;
            case SerializedPropertyType.String when token.Type == JTokenType.String:
                prop.stringValue = token.Value<string>();
                break;
            case SerializedPropertyType.ObjectReference when token.Type == JTokenType.Object:
                if (token[GuidField] == null)
                {
                    prop.objectReferenceValue = null;
                }
                else
                {
                    var guid = token[GuidField].Value<string>();
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
                    prop.objectReferenceValue = asset;
                    prop.objectReferenceInstanceIDValue = token[InstanceIDField]?.Value<int>() ?? 0;
                }
                break;
            case SerializedPropertyType.Color when token.Type == JTokenType.Object:
                var col = new Color(token[RedComponent].Value<float>(), token[GreenComponent].Value<float>(), token[BlueComponent].Value<float>(), token[AlphaComponent].Value<float>());
                prop.colorValue = col;
                break;
            case SerializedPropertyType.ObjectReference when token.Type == JTokenType.Integer:
                prop.objectReferenceInstanceIDValue = token.Value<int>();
                break;
            case SerializedPropertyType.ObjectReference when token.Type == JTokenType.Null:
                prop.objectReferenceValue = null;
                break;
        }
    }
}