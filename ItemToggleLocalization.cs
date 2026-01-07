using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ItemToggleLocalization
{
    public const string LANG_EN = "en";
    public const string LANG_ZH = "zh";
    public const string LANG_JA = "ja";

    private static string _currentLang = LANG_EN;
    private static Dictionary<string, string> _dict = new Dictionary<string, string>();

    public static string CurrentLang
    {
        get => _currentLang;
        set
        {
            if (_currentLang != value)
            {
                _currentLang = value;
                EditorPrefs.SetString("ItemToggleBuilder_Lang", value);
                LoadLanguage(value);
            }
        }
    }

    public static void Initialize()
    {
        _currentLang = EditorPrefs.GetString("ItemToggleBuilder_Lang", LANG_EN);
        LoadLanguage(_currentLang);
    }

    private static void LoadLanguage(string lang)
    {
        _dict.Clear();
        
        // Find the script path to locate JSONs relative to it
        // Assuming structure: ItemToggleBuilder/Editor/Languages/{lang}.json
        // We can search for this script file
        string[] guids = AssetDatabase.FindAssets("ItemToggleLocalization");
        if (guids.Length == 0) return;
        
        string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        string dir = Path.GetDirectoryName(scriptPath);
        string langPath = Path.Combine(dir, "Languages", $"{lang}.json");

        if (File.Exists(langPath))
        {
            try 
            {
                string json = File.ReadAllText(langPath);
                LangData data = JsonUtility.FromJson<LangData>(json);
                if (data != null && data.items != null)
                {
                    foreach (var item in data.items)
                    {
                        if (!string.IsNullOrEmpty(item.k))
                            _dict[item.k] = item.v;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ItemToggleBuilder] Failed to load language {lang}: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"[ItemToggleBuilder] Language file not found: {langPath}");
        }
    }

    public static string Get(string key)
    {
        if (_dict.TryGetValue(key, out string val)) return val;
        return key; // Fallback to key
    }

    [Serializable]
    private class LangData
    {
        public List<LangItem> items;
    }

    [Serializable]
    private class LangItem
    {
        public string k;
        public string v;
    }
}
