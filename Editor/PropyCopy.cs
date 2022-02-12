using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class CopyUtil
{
    private const string GuidField = "guid";
    private const string InstanceIDField = "instanceID";

    [InitializeOnLoadMethod]
    private static void InitialiseCopyUtil()
    {
        EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
    }

    private static SerializedProperty _cachedProp;

    private static void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
    {
        _cachedProp = property.Copy();
        menu.AddItem(new GUIContent("Copy"), false, OnCopyProperty);

        //only show the enabled menu option if the clipboard is valid json and,
        //if that json has multiple entries the property must be an object
        if (PasteComponentCheck(out var obj) && obj.Count > 1 == (property.hasChildren && property.propertyType != SerializedPropertyType.ObjectReference))
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

            var endProperty = _cachedProp.GetEndProperty().propertyPath;
            while (_cachedProp.NextVisible(true) && _cachedProp.propertyPath != endProperty)
            {
                //skip the parent prop (e.g. Vector3) as we only care about the raw values
                //However Object references are a special case as we need to pull non-visible info out
                if (_cachedProp.hasChildren && _cachedProp.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var path = _cachedProp.propertyPath.Substring(nameLength);
                obj[path] = GetPropValue(_cachedProp);
            }
        }
        else
            obj[_cachedProp.name] = GetPropValue(_cachedProp);

        if (obj.Count > 0) EditorGUIUtility.systemCopyBuffer = obj.ToString();
    }

    private static void OnPasteProperty()
    {
        var obj = JObject.Parse(EditorGUIUtility.systemCopyBuffer);
        var so = _cachedProp.serializedObject;
        if (_cachedProp.hasVisibleChildren)
        {
            foreach (var kvp in obj)
            {
                var childProp = so.FindProperty($"{_cachedProp.name}.{kvp.Key}");
                if (childProp == null) continue;
                SetPropValue(childProp, kvp.Value);
            }
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
        var obj = new JObject();
        using (var so = new SerializedObject(command.context))
        {
            var it = so.GetIterator();
            it.NextVisible(true); //skip script prop
            while (it.NextVisible(true))
            {
                //skip the parent prop (e.g. Vector3) as we only care about the raw values
                //However Object references are a special case as we need to pull non-visible info out
                if (it.hasChildren && it.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                obj[it.propertyPath] = GetPropValue(it);
            }
        }

        EditorGUIUtility.systemCopyBuffer = obj.ToString();
    }


    [MenuItem("CONTEXT/Component/Paste All Fields", true)]
    private static bool PasteComponentCheck(MenuCommand command) => PasteComponentCheck(out _);

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
            foreach (var kvp in obj)
            {
                //try to find a property with the same property path
                var prop = so.FindProperty(kvp.Key);
                if (prop != null)
                {
                    SetPropValue(prop, kvp.Value);
                }
            }

            so.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Gets the json representation out of a SerializedProperty
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
            default:
                throw new ArgumentException($"{property.propertyType} is not supported");
        }
    }

    /// <summary>
    /// Sets the SerializedProperty value from a Json Value
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
            case SerializedPropertyType.ObjectReference when token.Type == JTokenType.Object:
                if (token[GuidField] == null)
                {
                    prop.objectReferenceValue = null;
                }
                else
                {
                    var guid = token[GuidField].Value<string>();
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guid));
                    prop.objectReferenceValue = asset;
                    prop.objectReferenceInstanceIDValue = token[InstanceIDField]?.Value<int>() ?? 0;
                }

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