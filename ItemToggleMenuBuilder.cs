#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations; // Animator graph APIs
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

using AnimCtrl = UnityEditor.Animations.AnimatorController;

/// <summary>
/// Item Toggle Builder with Custom Grouping
/// </summary>
public class ItemToggleMenuBuilder : EditorWindow
{
    [Serializable]
    private class ToggleItem
    {
        public bool isHeader;
        public string title;      // For header
        public Renderer renderer; // For item
        public bool selected;     // Selection state
    }

    // ===== Common Settings =====
    [SerializeField] private string assetFolder = "Assets/Oniya/ItemToggleBuilder/MA_ItemToggle";
    [SerializeField] private bool useAbsolutePathMode = true;

    [MenuItem("Tools/Oniya/ItemToggleBuilder")]
    private static void Open() => GetWindow<ItemToggleMenuBuilder>("ItemToggleBuilder");

    private void OnEnable()
    {
        minSize = new Vector2(560, 390);
        ItemToggleLocalization.Initialize();

        if (_items == null || _items.Count == 0) RefreshList();
        BuildReorderableList();
    }

    private void OnGUI()
    {
        // Language Selector
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("English", EditorStyles.miniButtonLeft)) ItemToggleLocalization.CurrentLang = ItemToggleLocalization.LANG_EN;
            if (GUILayout.Button("中文", EditorStyles.miniButtonMid)) ItemToggleLocalization.CurrentLang = ItemToggleLocalization.LANG_ZH;
            if (GUILayout.Button("日本語", EditorStyles.miniButtonRight)) ItemToggleLocalization.CurrentLang = ItemToggleLocalization.LANG_JA;
        }
        GUILayout.Space(5);

        GUILayout.Label(ItemToggleLocalization.Get("title_main"), EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(ItemToggleLocalization.Get("help_desc"), MessageType.Info);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawAutoToggleTab();
        }

        GUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(ItemToggleLocalization.Get("btn_select_all_on"), GUILayout.Height(24))) SelectByEnabled(true);
            if (GUILayout.Button(ItemToggleLocalization.Get("btn_select_all_off"), GUILayout.Height(24))) SelectByEnabled(false);
            GUILayout.FlexibleSpace();
        }

        GUILayout.Space(8);

        // Check for limit errors (Now warnings, as we auto-split)
        bool willAutoSplit = false;
        if (_items != null)
        {
            int currentCount = 0;
            foreach (var item in _items)
            {
                if (item.isHeader) currentCount = 0;
                else if (item.selected)
                {
                    currentCount++;
                    if (currentCount > 8)
                    {
                        willAutoSplit = true;
                        break;
                    }
                }
            }
        }

        if (willAutoSplit)
        {
            EditorGUILayout.HelpBox(ItemToggleLocalization.Get("msg_auto_split_warning"), MessageType.Info);
        }

        // Generate Button
        int selCount = _items != null ? _items.Count(x => !x.isHeader && x.selected) : 0;
        GUI.enabled = selCount > 0 && targetRoot != null;
        string genLabel = (targetRoot == null)
            ? ItemToggleLocalization.Get("msg_assign_root")
            : string.Format(ItemToggleLocalization.Get("btn_generate_fmt"), selCount);
        if (GUILayout.Button(genLabel, GUILayout.Height(32)))
        {
            BuildSelected();
        }
        GUI.enabled = true;
    }

    // =============================
    // ========== Logic ============
    // =============================
    [SerializeField] private GameObject targetRoot;
    [SerializeField] private string submenuName = "Item_Toggles";
    [SerializeField] private string itemMenuPrefix = "アイテム"; 
    [SerializeField] private string parameterPrefix = "RT_";
    [SerializeField] private bool autoAttachUnderAvatarRoot = true;

    [SerializeField] private List<ToggleItem> _items = new List<ToggleItem>();
    [SerializeField] private bool showSelectedOnly = false;
    private bool _prevShowSelectedOnly = false;
    private Vector2 _scroll;

    private UnityEditorInternal.ReorderableList _rl;

    private void DrawAutoToggleTab()
    {
        EditorGUI.BeginChangeCheck();
        var newRoot = (GameObject)EditorGUILayout.ObjectField(ItemToggleLocalization.Get("label_root"), targetRoot, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            if (newRoot != targetRoot)
            {
                targetRoot = newRoot;
                RefreshList();
                BuildReorderableList();

                if (targetRoot != null)
                {
                    string safeName = San(targetRoot.name);
                    parameterPrefix = SanParam(safeName) + "_";
                    submenuName = safeName;
                    string basePath = "Assets/Oniya/ItemToggleBuilder";
                    assetFolder = $"{basePath}/{safeName}";
                }
            }
        }

        submenuName = EditorGUILayout.TextField(ItemToggleLocalization.Get("label_submenu_name"), string.IsNullOrEmpty(submenuName) ? "Render_Toggles" : submenuName);
        parameterPrefix = EditorGUILayout.TextField(ItemToggleLocalization.Get("label_param_prefix"), string.IsNullOrEmpty(parameterPrefix) ? "RT_" : parameterPrefix);
        autoAttachUnderAvatarRoot = EditorGUILayout.ToggleLeft(ItemToggleLocalization.Get("toggle_auto_attach"), autoAttachUnderAvatarRoot);

        using (new EditorGUILayout.HorizontalScope())
        {
            bool prev = showSelectedOnly;
            showSelectedOnly = GUILayout.Toggle(showSelectedOnly, ItemToggleLocalization.Get("toggle_show_selected_only"), "Button", GUILayout.Width(120));
            if (prev != showSelectedOnly)
            {
                BuildReorderableList();
                _prevShowSelectedOnly = showSelectedOnly;
                Repaint();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(ItemToggleLocalization.Get("btn_add_group"), GUILayout.Width(150)))
            {
                AddGroupHeader();
            }
        }

        if (_items == null)
        {
            EditorGUILayout.HelpBox(ItemToggleLocalization.Get("msg_assign_root"), MessageType.Info);
        }
        else if (_items.Count == 0 && targetRoot != null)
        {
            EditorGUILayout.HelpBox(ItemToggleLocalization.Get("msg_no_renderers"), MessageType.Warning);
        }
        else
        {
            // Ensure at least one group if items exist
            if (_items.Count > 0)
            {
                bool hasHeader = false;
                foreach (var item in _items) { if (item.isHeader) { hasHeader = true; break; } }
                if (!hasHeader)
                {
                    _items.Insert(0, new ToggleItem { isHeader = true, title = ItemToggleLocalization.Get("default_group_name") });
                }
            }

            if (_rl == null) BuildReorderableList();
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            _rl?.DoLayoutList();
            EditorGUILayout.EndScrollView();
        }

        GUILayout.Space(6);
        useAbsolutePathMode = EditorGUILayout.ToggleLeft(ItemToggleLocalization.Get("toggle_absolute_path"), useAbsolutePathMode);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(ItemToggleLocalization.Get("label_save_path"), GUILayout.Width(48));
            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.TextField(assetFolder);
            if (EditorGUI.EndChangeCheck())
            {
                if (!string.IsNullOrEmpty(n)) assetFolder = n;
            }
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                var picked = EditorUtility.OpenFolderPanel(ItemToggleLocalization.Get("dialog_select_save_folder"), Application.dataPath, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    if (picked.StartsWith(Application.dataPath))
                    {
                        assetFolder = "Assets" + picked.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(ItemToggleLocalization.Get("dialog_warning_title"), ItemToggleLocalization.Get("msg_outside_project"), "OK");
                    }
                }
            }
        }
    }

    private void RefreshList()
    {
        _items.Clear();
        if (targetRoot == null) return;

        var rs = targetRoot.GetComponentsInChildren<Renderer>(true)
            .Where(r => !(r is TrailRenderer) && !(r is LineRenderer))
            .ToList();

        // Create default group
        _items.Add(new ToggleItem { isHeader = true, title = ItemToggleLocalization.Get("default_group_name") + " 1" });

        int countInGroup = 0;
        int groupIdx = 2;
        foreach (var r in rs)
        {
            if (countInGroup >= 8)
            {
                _items.Add(new ToggleItem { isHeader = true, title = ItemToggleLocalization.Get("default_group_name") + " " + groupIdx });
                groupIdx++;
                countInGroup = 0;
            }
            _items.Add(new ToggleItem { isHeader = false, renderer = r, selected = false });
            countInGroup++;
        }
    }

    private void AddGroupHeader()
    {
        _items.Add(new ToggleItem { isHeader = true, title = ItemToggleLocalization.Get("default_group_name") });
        BuildReorderableList();
        _rl.index = _items.Count - 1;
    }

    private void SelectByEnabled(bool select)
    {
        foreach (var item in _items)
        {
            if (!item.isHeader && item.renderer != null)
            {
                if (select) item.selected = item.renderer.enabled;
                else item.selected = false;
            }
        }
    }

    private void BuildReorderableList()
    {
        if (_items == null) return;

        _rl = new UnityEditorInternal.ReorderableList(_items, typeof(ToggleItem), true, false, false, false);
        _rl.elementHeightCallback = (index) => 
        {
            if (index >= _items.Count) return 0;
            var item = _items[index];
            if (showSelectedOnly && !item.isHeader && !item.selected) return 0; // Hide unselected
            return item.isHeader ? EditorGUIUtility.singleLineHeight + 6 : EditorGUIUtility.singleLineHeight + 2;
        };

        _rl.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            if (index >= _items.Count) return;
            var item = _items[index];

            if (showSelectedOnly && !item.isHeader && !item.selected) return;

            if (item.isHeader)
            {
                // Draw Header
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), new Color(0.2f, 0.2f, 0.2f, 1f));
                
                // Count items in this group
                int count = 0;
                for (int i = index + 1; i < _items.Count; i++)
                {
                    if (_items[i].isHeader) break;
                    if (_items[i].selected) count++;
                }
                
                string countLabel = string.Format(ItemToggleLocalization.Get("label_item_count"), count);
                Color countColor = count > 8 ? Color.red : Color.white;
                if (count > 8) countLabel += " " + ItemToggleLocalization.Get("error_group_full");

                float buttonW = 50;
                float countW = 100;
                
                // Title Field
                item.title = EditorGUI.TextField(new Rect(rect.x, rect.y, rect.width - buttonW - countW - 5, rect.height), item.title);
                
                // Count Label
                var style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = countColor;
                style.alignment = TextAnchor.MiddleRight;
                EditorGUI.LabelField(new Rect(rect.x + rect.width - buttonW - countW, rect.y, countW, rect.height), countLabel, style);

                // Remove Button
                if (GUI.Button(new Rect(rect.x + rect.width - buttonW, rect.y, buttonW, rect.height), ItemToggleLocalization.Get("btn_remove")))
                {
                    EditorApplication.delayCall += () => {
                        _items.RemoveAt(index);
                        Repaint();
                    };
                }
            }
            else
            {
                // Draw Renderer Item
                if (item.renderer == null) return;
                rect.y += 1;
                rect.height = EditorGUIUtility.singleLineHeight;

                var checkRect = new Rect(rect.x, rect.y, 20, rect.height);
                item.selected = EditorGUI.Toggle(checkRect, item.selected);

                float x = rect.x + 24;
                string path = GetRelativePath(item.renderer.transform);
                string label = $"{item.renderer.name}   <color=#888>({path})</color>";
                
                var style = new GUIStyle(EditorStyles.label) { richText = true };
                var c = GUI.color;
                GUI.color = item.renderer.enabled ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                EditorGUI.LabelField(new Rect(x, rect.y, rect.width - x, rect.height), label, style);
                GUI.color = c;
            }
        };
    }

    private void BuildSelected()
    {
        EnsureFolders(assetFolder);

        // Root Menu
        var rootMenuPath = $"{assetFolder}/{San(submenuName)}_Menu.asset";
        var rootMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        rootMenu.controls = new List<VRCExpressionsMenu.Control>();
        AssetDatabase.CreateAsset(rootMenu, rootMenuPath);

        // FX Controller
        var ctrlPath = $"{assetFolder}/{San(submenuName)}_FX.controller";
        var ctrl = AnimCtrl.CreateAnimatorControllerAtPath(ctrlPath);

        // Holder Object
        var holder = new GameObject($"MA_RenderToggle_{San(submenuName)}");
        Undo.RegisterCreatedObjectUndo(holder, ItemToggleLocalization.Get("undo_create_holder"));

        var childMenus = new List<VRCExpressionsMenu>();
        VRCExpressionsMenu currentSubMenu = null;
        string currentBaseName = "Items";
        int currentGroupItemCount = 0;
        int currentPage = 1;

        try
        {
            var maParams = holder.AddComponent<ModularAvatarParameters>();
            maParams.parameters = new List<ParameterConfig>();

            foreach (var item in _items)
            {
                if (item.isHeader)
                {
                    // Create new SubMenu
                    var child = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    child.controls = new List<VRCExpressionsMenu.Control>();
                    AssetDatabase.AddObjectToAsset(child, rootMenu);
                    
                    currentBaseName = item.title;
                    currentPage = 1;
                    
                    rootMenu.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = currentBaseName,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = child
                    });
                    childMenus.Add(child);
                    currentSubMenu = child;
                    currentGroupItemCount = 0;
                }
                else
                {
                    if (!item.selected || item.renderer == null) continue;

                    // Ensure we have a submenu (Auto-create default group if missing)
                    if (currentSubMenu == null)
                    {
                        var child = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                        child.controls = new List<VRCExpressionsMenu.Control>();
                        AssetDatabase.AddObjectToAsset(child, rootMenu);
                        
                        currentBaseName = "Items";
                        currentPage = 1;

                        rootMenu.controls.Add(new VRCExpressionsMenu.Control
                        {
                            name = currentBaseName,
                            type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                            subMenu = child
                        });
                        childMenus.Add(child);
                        currentSubMenu = child;
                        currentGroupItemCount = 0;
                    }

                    // Handle Page Overflow (Auto-split)
                    if (currentGroupItemCount >= 8)
                    {
                        currentPage++;
                        var child = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                        child.controls = new List<VRCExpressionsMenu.Control>();
                        AssetDatabase.AddObjectToAsset(child, rootMenu);

                        rootMenu.controls.Add(new VRCExpressionsMenu.Control
                        {
                            name = $"{currentBaseName} {currentPage}",
                            type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                            subMenu = child
                        });
                        childMenus.Add(child);
                        currentSubMenu = child;
                        currentGroupItemCount = 0;
                    }

                    var r = item.renderer;
                    string niceName = r.name;
                    string param = MakeUniqueParam($"{parameterPrefix}{SanParam(niceName)}", maParams.parameters.Select(p => p.nameOrPrefix));
                    bool defaultOn = r.enabled;

                    // Create Animation
                    var onClip = CreateSingleToggleClip(r, true, $"{assetFolder}/{San(param)}_On.anim");
                    var offClip = CreateSingleToggleClip(r, false, $"{assetFolder}/{San(param)}_Off.anim");

                    // Add Layer
                    var layerName = $"RT_{SanLayer(niceName)}"; 
                    AddBinaryLayer(ctrl, layerName, param, onClip, offClip, defaultOn);

                    // Add Parameter
                    maParams.parameters.Add(new ParameterConfig
                    {
                        nameOrPrefix = param,
                        syncType = ParameterSyncType.Bool,
                        defaultValue = defaultOn ? 1f : 0f,
                        saved = true
                    });

                    // Generate Thumbnail
                    Texture2D thumb = null;
                    try
                    {
                        string absPath = assetFolder.StartsWith("Assets") 
                            ? Application.dataPath + assetFolder.Substring(6) 
                            : Path.GetFullPath(assetFolder);
                        thumb = CaptureThumbnailAndSave(r.gameObject, absPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ItemToggleBuilder] Failed to generate thumbnail: {ex.Message}");
                    }

                    // Add Control
                    currentSubMenu.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = niceName,
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = param },
                        icon = thumb
                    });

                    currentGroupItemCount++;
                }
            }

            AssetDatabase.SaveAssets();

            // ExpressionParameters
            var epPath = $"{assetFolder}/{San(submenuName)}_ExpressionParameters.asset";
            var ep = CreateParametersAssetFromMA(epPath, maParams.parameters);

            BindParametersToMenusSerialized(rootMenu, childMenus, ep);

            var installer = EnsureMenuInstallerOn(holder, rootMenu);
            SetEnumByNameSerialized(installer, new[] { "installTarget", "installTo" }, new[] { "AvatarRoot", "Auto" });

            // FX Merge
            var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = ctrl;
            TrySetEnumByName(merge, new[] { "layerType", "mergeLayer", "targetLayer" }, new[] { "FX", "Fx", "FxLayer" });
            if (useAbsolutePathMode) SetEnumByNameExact(merge, "pathMode", new[] { "Absolute", "絶対的", "AbsolutePath" });
            else SetEnumByNameExact(merge, "pathMode", new[] { "Relative", "相対的" });

            // Prefab
            var prefabPath = $"{assetFolder}/{holder.name}.prefab";

            if (autoAttachUnderAvatarRoot && targetRoot != null)
            {
                Undo.SetTransformParent(holder.transform, targetRoot.transform, ItemToggleLocalization.Get("undo_parent_avatar"));
                holder.transform.localPosition = Vector3.zero;
                holder.transform.localRotation = Quaternion.identity;
                holder.transform.localScale = Vector3.one;
                holder.transform.SetAsLastSibling();
                PrefabUtility.SaveAsPrefabAssetAndConnect(holder, prefabPath, InteractionMode.AutomatedAction);
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(holder, prefabPath);
            }

            // Cleanup
            EditorUtility.SetDirty(rootMenu);
            foreach (var m in childMenus) EditorUtility.SetDirty(m);
            EditorUtility.SetDirty(ep);
            EditorUtility.SetDirty(ctrl);

            AssetDatabase.SaveAssets();
            AssetDatabase.ForceReserializeAssets(new[] { rootMenuPath, ctrlPath, epPath, prefabPath });
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath));
            
            var msg = (autoAttachUnderAvatarRoot && targetRoot != null)
                ? ItemToggleLocalization.Get("msg_done_auto")
                : ItemToggleLocalization.Get("msg_done_manual");
            EditorUtility.DisplayDialog(ItemToggleLocalization.Get("dialog_done_title"), msg, "OK");
        }
        finally
        {
            if (!(autoAttachUnderAvatarRoot && targetRoot != null)) DestroyImmediate(holder);
            CleanUpThumbnailObjects();
        }
    }

    // ==========================================
    // ============ Helper Logic ================
    // ==========================================
    
    private const int ThumbnailWidth = 256;
    private const int ThumbnailHeight = 256;
    private const string ThumbnailCameraLayer = "DT_Thumbnail";
    private const string ThumbnailCameraName = "DTTempThumbnailCamera";
    private const string ThumbnailWearableName = "DTTempThumbnailWearable";
    private const float ThumbnailCameraFov = 45.0f;
    private const int StartingUserLayer = 8;
    private const int MaxLayers = 32;

    private static Texture2D CaptureThumbnailAndSave(GameObject target, string saveDirectory)
    {
        if (target == null) return null;

        Texture2D texture = null;
        RenderTexture renderTexture = null;
        GameObject cameraObj = null;
        GameObject clone = null;
        GameObject lightObj = null;

        try
        {
            if (!PrepareWearableThumbnailCameraLayer())
                Debug.LogWarning("Could not allocate a layer for thumbnail generation.");

            renderTexture = new RenderTexture(ThumbnailWidth, ThumbnailHeight, 24);
            cameraObj = new GameObject(ThumbnailCameraName);
            var camera = cameraObj.AddComponent<Camera>();
            
            camera.targetTexture = renderTexture;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0, 0, 0, 0);
            camera.fieldOfView = ThumbnailCameraFov;
            camera.cullingMask = LayerMask.GetMask(ThumbnailCameraLayer);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;

            clone = Instantiate(target);
            clone.name = ThumbnailWearableName;
            var clonePos = new Vector3(0, -1000, 0);
            clone.transform.position = clonePos;
            RecursiveSetLayer(clone, LayerMask.NameToLayer(ThumbnailCameraLayer));

            // Bounds Calculation & Positioning
            var renderers = clone.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!hasBounds) bounds = new Bounds(clone.transform.position, Vector3.one * 0.1f);

            Vector3 objectCenter = bounds.center;
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDim <= 0.001f) maxDim = 0.1f;

            float distance = (maxDim * 0.6f) / Mathf.Tan(ThumbnailCameraFov * 0.5f * Mathf.Deg2Rad);
            float minSafeDistance = (maxDim / 2.0f) + camera.nearClipPlane + 0.02f;
            distance = Mathf.Max(distance, minSafeDistance);

            cameraObj.transform.position = objectCenter + new Vector3(0, 0, distance);
            cameraObj.transform.LookAt(objectCenter);

            // Add Light
            lightObj = new GameObject("TempLight");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = Color.white;
            lightObj.transform.SetParent(cameraObj.transform);
            lightObj.transform.localRotation = Quaternion.identity;

            Physics.SyncTransforms();

            // Pass 1
            camera.Render();
            
            RenderTexture.active = renderTexture;
            texture = new Texture2D(ThumbnailWidth, ThumbnailHeight, TextureFormat.ARGB32, false);
            texture.ReadPixels(new Rect(0, 0, ThumbnailWidth, ThumbnailHeight), 0, 0);
            texture.Apply();
            RenderTexture.active = null;

            // Pass 2: Auto Framing
            Rect contentRect = CalculateVisiblePixelsRect(texture);
            
            if (contentRect.width > 0 && contentRect.height > 0)
            {
                Vector2 currentCenter = contentRect.center;
                Vector2 centerOffset = currentCenter - new Vector2(0.5f, 0.5f);
                float contentMaxDim = Mathf.Max(contentRect.width, contentRect.height);
                float targetFill = 0.85f;
                
                if (contentMaxDim < targetFill * 0.8f || Mathf.Abs(centerOffset.x) > 0.1f || Mathf.Abs(centerOffset.y) > 0.1f)
                {
                    float visibleHeightAtDist = 2.0f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    float visibleWidthAtDist = visibleHeightAtDist * camera.aspect;
                    
                    Vector3 moveOffset = camera.transform.right * (centerOffset.x * visibleWidthAtDist) + 
                                         camera.transform.up * (centerOffset.y * visibleHeightAtDist);
                    
                    cameraObj.transform.position += moveOffset;
                    
                    float zoomFactor = contentMaxDim / targetFill;
                    zoomFactor = Mathf.Max(zoomFactor, 0.1f); 
                    
                    float newDistance = distance * zoomFactor;
                    newDistance = Mathf.Max(newDistance, 0.15f);
                    
                    cameraObj.transform.position += cameraObj.transform.forward * (distance - newDistance);
                    
                    camera.Render();
                    
                    RenderTexture.active = renderTexture;
                    texture.ReadPixels(new Rect(0, 0, ThumbnailWidth, ThumbnailHeight), 0, 0);
                    texture.Apply();
                    RenderTexture.active = null;
                }
            }

            byte[] bytes = texture.EncodeToPNG();
            string filename = $"{target.name}_Thumbnail.png";
            string fullPath = Path.Combine(saveDirectory, filename);
            File.WriteAllBytes(fullPath, bytes);
            
            if (fullPath.StartsWith(Application.dataPath))
            {
                AssetDatabase.ImportAsset("Assets" + fullPath.Substring(Application.dataPath.Length)); 
                string relativePath = "Assets" + fullPath.Substring(Application.dataPath.Length);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(relativePath);
            }
            return texture;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to generate thumbnail: {e.Message}");
            return null;
        }
        finally
        {
            CleanUpThumbnailObjects(); 
            if (renderTexture != null) renderTexture.Release();
            if (texture != null && !EditorUtility.IsPersistent(texture)) DestroyImmediate(texture);
        }
    }

    private static void RecursiveSetLayer(GameObject obj, int layerIndex)
    {
        if (obj.layer == 0) obj.layer = layerIndex;
        for (var i = 0; i < obj.transform.childCount; i++)
        {
            var child = obj.transform.GetChild(i);
            RecursiveSetLayer(child.gameObject, layerIndex);
        }
    }

    private static void CleanUpThumbnailObjects()
    {
        var existingCamObj = GameObject.Find(ThumbnailCameraName);
        if (existingCamObj != null) DestroyImmediate(existingCamObj);
        var existingDummy = GameObject.Find(ThumbnailWearableName);
        if (existingDummy != null) DestroyImmediate(existingDummy);
    }

    private static bool PrepareWearableThumbnailCameraLayer()
    {
        if (!HasCullingLayer(ThumbnailCameraLayer))
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            if (assets == null) return true;
            var so = new SerializedObject(assets);
            var layers = so.FindProperty("layers");
            for (var i = MaxLayers - 1; i >= StartingUserLayer; i--)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layer.stringValue))
                {
                    layer.stringValue = ThumbnailCameraLayer;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return true;
                }
            }
            return false;
        }
        return true;
    }

    private static bool HasCullingLayer(string layerName)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
        if (assets == null) return false;
        var so = new SerializedObject(assets);
        var layers = so.FindProperty("layers");
        for (var i = 0; i < MaxLayers; i++)
        {
            var elem = layers.GetArrayElementAtIndex(i);
            if (elem.stringValue.Equals(layerName)) return true;
        }
        return false;
    }

    private static Rect CalculateVisiblePixelsRect(Texture2D tex)
    {
        int w = tex.width;
        int h = tex.height;
        Color[] pixels = tex.GetPixels();
        int minX = w, maxX = 0, minY = h, maxY = 0;
        bool found = false;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (pixels[y * w + x].a > 0.01f) 
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    found = true;
                }
            }
        }
        if (!found) return new Rect(0, 0, 0, 0);
        float xNorm = (float)minX / w;
        float yNorm = (float)minY / h;
        float wNorm = (float)(maxX - minX + 1) / w;
        float hNorm = (float)(maxY - minY + 1) / h;
        return new Rect(xNorm, yNorm, wNorm, hNorm);
    }

    // ---------------- Utilities ----------------
    private static void EnsureFolders(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!path.StartsWith("Assets")) throw new Exception(ItemToggleLocalization.Get("error_save_path_assets"));
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private static string San(string s) => string.IsNullOrEmpty(s) ? "New" : string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    private static string SanLayer(string s) => string.IsNullOrEmpty(s) ? "Layer" : string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == ' '));
    private static string SanParam(string s) => string.IsNullOrEmpty(s) ? "Param" : string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));

    private static string GetRelativePath(Transform t)
    {
        if (t == null) return "";
        var root = t.root;
        string Build(Transform x)
        {
            if (x == null || x == root) return "";
            var up = Build(x.parent);
            return string.IsNullOrEmpty(up) ? x.name : $"{up}/{x.name}";
        }
        return Build(t);
    }

    private static string MakeUniqueParam(string baseName, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing ?? Enumerable.Empty<string>());
        if (!set.Contains(baseName)) return baseName;
        int i = 1;
        while (set.Contains($"{baseName}_{i}")) i++;
        return $"{baseName}_{i}";
    }

    private static AnimationClip CreateSingleToggleClip(Renderer r, bool enabled, string path)
    {
        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
        var binding = new EditorCurveBinding
        {
            type = typeof(GameObject),
            path = GetRelativePath(r.transform),
            propertyName = "m_IsActive"
        };
        var curve = new AnimationCurve(new Keyframe(0, enabled ? 1 : 0));
        AnimationUtility.SetEditorCurve(clip, binding, curve);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static void AddBinaryLayer(AnimCtrl ctrl, string layerName, string boolParam, AnimationClip onClip, AnimationClip offClip, bool defaultOn)
    {
        if (!ctrl.parameters.Any(p => p.name == boolParam))
            ctrl.AddParameter(boolParam, UnityEngine.AnimatorControllerParameterType.Bool);

        var baseName = SanLayer(layerName);
        var name = baseName;
        int suffix = 1;
        while (ctrl.layers.Any(l => l.name == name)) name = $"{baseName}_{suffix++}";

        var sm = new AnimatorStateMachine { name = name };
        var ctrlPath = AssetDatabase.GetAssetPath(ctrl); 
        if (!string.IsNullOrEmpty(ctrlPath)) AssetDatabase.AddObjectToAsset(sm, ctrl);

        var layer = new AnimatorControllerLayer
        {
            name = name,
            stateMachine = sm,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override
        };
        ctrl.AddLayer(layer);

        var stOn = sm.AddState("On", new Vector3(200, 100));
        var stOff = sm.AddState("Off", new Vector3(400, 100));

        stOn.motion = onClip;
        stOff.motion = offClip;

        var toOn = stOff.AddTransition(stOn); toOn.hasExitTime = false; toOn.exitTime = 0; toOn.AddCondition(AnimatorConditionMode.If, 0, boolParam);
        var toOff = stOn.AddTransition(stOff); toOff.hasExitTime = false; toOff.exitTime = 0; toOff.AddCondition(AnimatorConditionMode.IfNot, 0, boolParam);

        sm.defaultState = defaultOn ? stOn : stOff;

        EditorUtility.SetDirty(ctrl);
        EditorUtility.SetDirty(sm);
        EditorUtility.SetDirty(stOn);
        EditorUtility.SetDirty(stOff);
    }

    private static VRCExpressionParameters CreateParametersAssetFromMA(string path, IEnumerable<ParameterConfig> defs)
    {
        var ep = ScriptableObject.CreateInstance<VRCExpressionParameters>();
        var list = new List<VRCExpressionParameters.Parameter>();
        foreach (var d in defs)
        {
            if (string.IsNullOrEmpty(d.nameOrPrefix)) continue;
            list.Add(new VRCExpressionParameters.Parameter
            {
                name = d.nameOrPrefix,
                valueType = VRCExpressionParameters.ValueType.Bool,
                saved = d.saved
            });
        }
        ep.parameters = list.ToArray();
        AssetDatabase.CreateAsset(ep, path);
        return ep;
    }

    private static bool TrySetEnumByName(object obj, IEnumerable<string> names, IEnumerable<string> desiredNamesInPriority)
    {
        var t = obj.GetType();
        var enumType = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Concat<MemberInfo>(t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                         .Select(mi =>
                         {
                             Type et = null;
                             if (mi is FieldInfo fi && fi.FieldType.IsEnum) et = fi.FieldType;
                             if (mi is PropertyInfo pi && pi.PropertyType.IsEnum) et = pi.PropertyType;
                             return (mi, et);
                         })
                         .FirstOrDefault(x => x.et != null);
        if (enumType.et == null) return false;

        foreach (var desired in desiredNamesInPriority)
        {
            try
            {
                var val = Enum.Parse(enumType.et, desired);
                if (TrySet(obj, names, val)) return true;
            }
            catch { /* ignore */ }
        }
        return false;
    }

    private static bool SetObjectRefSerialized(UnityEngine.Object target, IEnumerable<string> propNames, UnityEngine.Object value)
    {
        var so = new SerializedObject(target);
        foreach (var n in propNames)
        {
            var p = so.FindProperty(n);
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference)
            {
                p.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }
        }
        return false;
    }

    private static bool SetEnumByNameSerialized(UnityEngine.Object target, IEnumerable<string> propNames, IEnumerable<string> desiredNamesInPriority)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = null;
        foreach (var n in propNames)
        {
            var cand = so.FindProperty(n);
            if (cand != null && p == null && cand.propertyType == SerializedPropertyType.Enum) p = cand;
        }
        if (p == null)
        {
            var it = so.GetIterator();
            if (it.NextVisible(true))
            {
                do
                {
                    if (it.propertyType == SerializedPropertyType.Enum)
                    {
                        p = it.Copy();
                        break;
                    }
                } while (it.NextVisible(false));
            }
        }
        if (p == null) return false;

        foreach (var desired in desiredNamesInPriority)
        {
            int index = -1;
            for (int i = 0; i < p.enumDisplayNames.Length; i++)
            {
                if (string.Equals(p.enumDisplayNames[i], desired, StringComparison.OrdinalIgnoreCase))
                {
                    index = i; break;
                }
            }
            if (index >= 0)
            {
                p.intValue = index;
                so.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }
        }
        return false;
    }

    private static bool SetEnumByNameExact(UnityEngine.Object target, string propName, IEnumerable<string> desiredNames)
    {
        if (target == null) return false;
        var so = new SerializedObject(target);
        var p = so.FindProperty(propName);
        if (p != null && p.propertyType == SerializedPropertyType.Enum)
        {
            foreach (var name in desiredNames)
            {
                for (int i = 0; i < p.enumNames.Length; i++)
                {
                    if (string.Equals(p.enumNames[i], name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.enumDisplayNames[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        p.intValue = i;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(target);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static void BindParametersToMenusSerialized(VRCExpressionsMenu root, List<VRCExpressionsMenu> childMenus, VRCExpressionParameters ep)
    {
        void SetParams(UnityEngine.Object menuObj)
        {
            if (menuObj == null) return;
            var propNames = new[] { "parameters", "expressionParameters", "paramAsset" };
            if (SetObjectRefSerialized(menuObj, propNames, ep)) return;
            var so = new SerializedObject(menuObj);
            var it = so.GetIterator();
            if (it.NextVisible(true))
            {
                do
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.type.Contains("VRCExpressionParameters"))
                    {
                        it.objectReferenceValue = ep;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(menuObj);
                        break;
                    }
                } while (it.NextVisible(false));
            }
        }
        SetParams(root);
        if (childMenus != null) foreach (var m in childMenus) SetParams(m);
        if (ep) EditorUtility.SetDirty(ep);
    }

    private static bool TrySet(object obj, IEnumerable<string> names, object value)
    {
        var t = obj.GetType();
        foreach (var n in names)
        {
            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType.IsInstanceOfType(value)) { f.SetValue(obj, value); return true; }
            var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(value)) { p.SetValue(obj, value); return true; }
        }
        return false;
    }

    // === EnsureMenuInstallerOn ===
    private static ModularAvatarMenuInstaller EnsureMenuInstallerOn(GameObject holder, VRCExpressionsMenu rootMenu)
    {
        var installer = holder.GetComponent<ModularAvatarMenuInstaller>();
        if (installer == null) installer = holder.AddComponent<ModularAvatarMenuInstaller>();

        var setOk = SetObjectRefSerialized(installer, new[] { "menu", "Menu", "menuAsset", "menuToAppend" }, rootMenu);
        if (!setOk)
        {
            if (!TrySet(installer, new[] { "menu", "Menu", "menuAsset", "menuToAppend" }, rootMenu))
            {
                Debug.LogWarning($"[RT] {ItemToggleLocalization.Get("log_installer_fail")}");
            }
        }
        return installer;
    }
}
#endif
