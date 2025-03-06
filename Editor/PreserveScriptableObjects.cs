using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using Object = UnityEngine.Object;

public class PreserveScriptableObjects : AssetModificationProcessor
{
    [System.Serializable]
    private struct CachedScriptableObjects
    {
        public List<LoadedScriptableObject> cachedObjects;
    }

    [System.Serializable]
    private struct LoadedScriptableObject
    {
        public string type;
        public string json;
        public string assetGuid;
    }

    [System.Serializable]
    private struct FieldsContainer
    {
        public List<object> fields;
    }

    private static string BasePath => Path.Combine(Application.dataPath, "../");
    private static string CachePath => Path.Combine(BasePath, "scriptable_objects.cache");


    [InitializeOnLoadMethod]
    static void Initialize()
    {
        //reset the SOs when the project starts, to handle when the project exits unexpectedly
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            RevertScriptableObjects(PlayModeStateChange.ExitingPlayMode);
        }
        EditorApplication.playModeStateChanged -= RevertScriptableObjects;
        EditorApplication.playModeStateChanged += RevertScriptableObjects;
    }

    static string[] OnWillSaveAssets(string[] paths)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return paths;

        CacheScriptableObjects();
        return paths;
    }

    private static async void RevertScriptableObjects(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            await Task.Delay(1000);

            var cachedObjs = LoadCachedScriptableObjects().cachedObjects;

            if (cachedObjs == null || cachedObjs.Count == 0) return;

            //List<FieldInfo> ignoredFields = new List<FieldInfo>();
            //FieldsContainer ignoredFieldsContainer;
            //ignoredFieldsContainer.fields = new List<object>();
            //string ignoredFieldsJson = "";

            foreach (var obj in cachedObjs)
            {
                var json = obj.json;
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(obj.assetGuid));
                if (!asset) continue;

                //Type type = Type.GetType(obj.type);
                //ignoredFields.Clear();
                //ignoredFieldsContainer.fields.Clear();

                //BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic |
                //                            BindingFlags.Static | BindingFlags.Instance |
                //                            BindingFlags.DeclaredOnly;

                //while (type != null && type.FullName != "UnityEngine.ScriptableObject")
                //{
                //    ignoredFields.AddRange(type.GetFields(bindingFlags).Where(f => Attribute.IsDefined(f, typeof(IgnorePreserveSOField))));
                //    type = type.BaseType;
                //}

                //foreach(var ignoredField in ignoredFields)
                //{
                //    ignoredFieldsContainer.fields.Add(ignoredField.GetValue(asset));
                //}

                //if (ignoredFieldsContainer.fields.Count > 0) ignoredFieldsJson = JsonUtility.ToJson(ignoredFieldsContainer);

                EditorJsonUtility.FromJsonOverwrite(json, asset);

                //if (ignoredFieldsContainer.fields.Count > 0)
                //{
                //    ignoredFieldsContainer = JsonUtility.FromJson<FieldsContainer>(ignoredFieldsJson);

                //    foreach (var ignoredField in ignoredFields)
                //    {
                //        //ignoredField.SetValue(asset, ignoredValueMap[ignoredField.Name]);
                //    }
                //}


                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.Refresh();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CacheScriptableObjects()
    {
        var cachedObjs = new List<LoadedScriptableObject>();

        string[] guids;
        var assets = FindAssets<ScriptableObject>(out guids);

        int index = 0;
        foreach(var asset in assets)
        {
            Type type = asset.GetType();
            if (asset && type.IsDefined(typeof(PreserveScriptableObjectAttribute), false))
            {
                string json = EditorJsonUtility.ToJson(asset, true);

                json = MarkIgnoredFieldsInJson(json, type);

                //Debug.Log(asset.name);
                //Debug.Log(json);

                cachedObjs.Add(new LoadedScriptableObject
                {
                    type = type.AssemblyQualifiedName,
                    json = json,
                    assetGuid = guids[index],
                });
            }
            index++;
        }

        SaveCachedScriptableObjects(cachedObjs);
    }

    private static string MarkIgnoredFieldsInJson(string json, Type type)
    {
        List<FieldInfo> ignoredFields = new List<FieldInfo>();

        BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Static | BindingFlags.Instance |
                                    BindingFlags.DeclaredOnly;

        Type t = type;
        while (t != null && t.FullName != "UnityEngine.ScriptableObject")
        {
            ignoredFields.AddRange(t.GetFields(bindingFlags).Where(f => Attribute.IsDefined(f, typeof(PersistScriptableObjectField))));
            t = t.BaseType;
        }

        foreach(var field in ignoredFields)
        {
            json = json.Replace($"\"{field.Name}\":", $"\"{field.Name}__IGNORED\":");
        }

        return json;
    }

    static T[] FindAssets<T>(out string[] guids) where T : Object
    {
        var types = TypeCache.GetTypesWithAttribute<PreserveScriptableObjectAttribute>();

        string search = "";

        foreach(var t in types)
        {
            search += $"t:{t.Name},";
        }

        guids = AssetDatabase.FindAssets(search);
        var assets = new T[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            assets[i] = AssetDatabase.LoadAssetAtPath<T>(path);
        }
        return assets;
    }

    private static void SaveCachedScriptableObjects(List<LoadedScriptableObject> objects)
    {
        var cache = new CachedScriptableObjects();
        cache.cachedObjects = objects;

        var json = JsonUtility.ToJson(cache, true);

        if (File.Exists(CachePath))
        {
            File.Delete(CachePath);
        }

        File.WriteAllText(CachePath, json);
    }

    private static CachedScriptableObjects LoadCachedScriptableObjects()
    {
        if (File.Exists(CachePath))
        {
            return JsonUtility.FromJson<CachedScriptableObjects>(File.ReadAllText(CachePath));
        }
        return default;
    }

    private static void DeleteCache()
    {
        if (File.Exists(CachePath))
        {
            Debug.Log($"ScriptableObjects Delete cache");
            //File.Delete(CachePath);
        }
    }
}
