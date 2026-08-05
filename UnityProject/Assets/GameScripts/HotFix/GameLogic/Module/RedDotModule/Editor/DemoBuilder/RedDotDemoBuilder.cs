using GameLogic.RedDot;
using GameLogic.RedDotDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.RedDot.Editor.DemoBuilder
{
    /// <summary>
    /// 红点 Demo 一键构建工具。
    /// 菜单：TEngine/RedDot/Build Demo Scene
    /// 自动生成 RedDotDemoUI.prefab + RedDotDemoScene.unity + 挂载 RedDotWidget。
    /// </summary>
    public static class RedDotDemoBuilder
    {
        private const string UI_PREFAB_PATH = "Assets/AssetRaw/UI/RedDotDemoUI.prefab";
        private const string SCENE_PATH = "Assets/Scenes/RedDotDemoScene.unity";

        [MenuItem("TEngine/RedDot/Build Demo Scene", false, 100)]
        public static void BuildDemo()
        {
            EditorUtility.DisplayProgressBar("RedDot Demo", "Building UI prefab...", 0.3f);
            var uiPrefab = BuildUIPrefab();

            EditorUtility.DisplayProgressBar("RedDot Demo", "Building scene...", 0.7f);
            BuildScene(uiPrefab);

            EditorUtility.ClearProgressBar();
            // 用 Debug.Log 替代 DisplayDialog，避免 execute_menu_item 后台执行时被模态对话框阻塞
            Debug.Log($"[RedDotDemoBuilder] ✅ Demo 构建完成！\nUI Prefab: {UI_PREFAB_PATH}\nScene: {SCENE_PATH}\n打开场景 {SCENE_PATH} 即可运行。");
        }

        // ─── UI Prefab 构建 ───

        private static GameObject BuildUIPrefab()
        {
            // 根：Canvas
            var canvasGo = new GameObject("RedDotDemoUI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // UIWindow 脚本挂在根上
            // 注意：实际运行时 UIModule 会实例化 prefab 并加 UIWindow，这里 prefab 根节点就是 UI 根
            var rect = canvasGo.GetComponent<RectTransform>();
            // Canvas 根锚点撑满 + sizeDelta 归零（ScreenSpaceOverlay 下 Canvas 自动全屏，sizeDelta 不该硬设）
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            // 关键：显式重置 scale 为 1（避免 CanvasScaler 在某些情况下把 localScale 置 0 导致 UI 不可见）
            rect.localScale = Vector3.one;

            // 标题
            var title = CreateText(canvasGo.transform, "Title", "红点系统 Demo - 邮件", 24, TextAnchor.UpperCenter);
            SetRect(title, 0, 1, 1, 1, new Vector2(0, -80), Vector2.zero, new Vector2(0.5f, 1));

            // 红点显示区
            var displayArea = new GameObject("DisplayArea");
            displayArea.transform.SetParent(canvasGo.transform, false);
            var displayRect = displayArea.AddComponent<RectTransform>();
            displayRect.anchorMin = new Vector2(0.1f, 0.4f);
            displayRect.anchorMax = new Vector2(0.9f, 0.9f);
            displayRect.offsetMin = displayRect.offsetMax = Vector2.zero;

            // 4 个红点节点行
            CreateRedDotRow(displayArea.transform, "RowRoot", "Root (总未读)", "Root", 0);
            CreateRedDotRow(displayArea.transform, "RowMail", "Mail (邮件入口)", "Mail", -60);
            CreateRedDotRow(displayArea.transform, "RowUnread", "Mail.Unread (未读数)", "Mail.Unread", -120);
            CreateRedDotRow(displayArea.transform, "RowAnnounce", "Mail.Announce (公告)", "Mail.Announce", -180);

            // 按钮区
            var btnArea = new GameObject("BtnArea");
            btnArea.transform.SetParent(canvasGo.transform, false);
            var btnRect = btnArea.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.55f, 0.05f);
            btnRect.anchorMax = new Vector2(0.98f, 0.95f);
            btnRect.offsetMin = btnRect.offsetMax = Vector2.zero;

            CreateButton(btnArea.transform, "BtnAddUnread", "+1 未读邮件", 0);
            CreateButton(btnArea.transform, "BtnReadOne", "读取一封", -50);
            CreateButton(btnArea.transform, "BtnAddNewMail", "新增 New 邮件", -100);
            CreateButton(btnArea.transform, "BtnClearNewMail", "清空 New 邮件", -150);
            CreateButton(btnArea.transform, "BtnToggleAnnounce", "切换公告", -200);

            // 日志区（用容器+子Text结构，避免Text与Image同节点冲突）
            var logContainer = new GameObject("TextLogContainer", typeof(RectTransform));
            logContainer.transform.SetParent(canvasGo.transform, false);
            SetRect(logContainer, 0.02f, 0.02f, 0.52f, 0.35f, Vector2.zero, Vector2.zero, Vector2.zero);
            var logBg = logContainer.AddComponent<Image>();
            logBg.color = new Color(0, 0, 0, 0.4f);
            var logText = CreateText(logContainer.transform, "TextLog", "", 14, TextAnchor.UpperLeft);
            var logTextRect = logText.GetComponent<RectTransform>();
            logTextRect.anchorMin = Vector2.zero;
            logTextRect.anchorMax = Vector2.one;
            logTextRect.offsetMin = new Vector2(8, 4);
            logTextRect.offsetMax = new Vector2(-8, -4);

            // 保存为 prefab（注意：UIWindow 类型的脚本不应直接加到 prefab，UIModule 会在实例化时挂载）
            // 所以这里只保存 UI 节点结构，UIWindow 脚本通过 prefab 关联
            EnsureFolder("Assets/AssetRaw/UI");
            PrefabUtility.SaveAsPrefabAsset(canvasGo, UI_PREFAB_PATH);
            Object.DestroyImmediate(canvasGo);

            // 关键修复：CanvasScaler 在 SaveAsPrefabAsset 序列化时会把根 RectTransform 的
            // scale 置 0（prefab 无 Canvas 父级，CanvasScaler 算出 scaleFactor=0）。
            // 保存后必须重新加载 prefab 资产，用 SerializedObject 强制修正根 RectTransform。
            FixRootRectTransformAfterSave(UI_PREFAB_PATH);

            Debug.Log($"[RedDotDemoBuilder] UI prefab saved: {UI_PREFAB_PATH}");
            return AssetDatabase.LoadAssetAtPath<GameObject>(UI_PREFAB_PATH);
        }

        private static void CreateRedDotRow(Transform parent, string rowName, string labelText, string redDotKey, float yOffset)
        {
            var row = new GameObject(rowName);
            row.transform.SetParent(parent, false);
            var rowRect = row.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0, 1);
            rowRect.anchorMax = new Vector2(1, 1);
            rowRect.pivot = new Vector2(0, 1);
            rowRect.anchoredPosition = new Vector2(0, yOffset);
            rowRect.sizeDelta = new Vector2(0, 50);

            // 标签
            var label = CreateText(row.transform, "Label", labelText, 16, TextAnchor.MiddleLeft);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(0.7f, 1);
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

            // 红点容器（右侧）
            var dotContainer = new GameObject("RedDotContainer");
            dotContainer.transform.SetParent(row.transform, false);
            var containerRect = dotContainer.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.75f, 0);
            containerRect.anchorMax = new Vector2(1, 1);
            containerRect.offsetMin = containerRect.offsetMax = Vector2.zero;

            // 红点 GameObject（红圆点 Image）
            var dotObj = new GameObject("DotObj");
            dotObj.transform.SetParent(dotContainer.transform, false);
            var dotRect = dotObj.AddComponent<RectTransform>();
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.3f, 0.5f);
            dotRect.sizeDelta = new Vector2(30, 30);
            var dotImage = dotObj.AddComponent<Image>();
            dotImage.color = Color.red;
            dotImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            dotImage.type = Image.Type.Simple;
            dotObj.SetActive(false); // 默认隐藏

            // 数字文本（UGUI Legacy Text，与标题/日志保持一致）
            var numObj = new GameObject("NumText");
            numObj.transform.SetParent(dotObj.transform, false);
            var numRect = numObj.AddComponent<RectTransform>();
            numRect.anchorMin = numRect.anchorMax = new Vector2(0.5f, 0.5f);
            numRect.sizeDelta = new Vector2(30, 30);
            numRect.anchoredPosition = Vector2.zero;
            var numText = numObj.AddComponent<Text>();
            numText.text = "0";
            numText.fontSize = 14;
            numText.alignment = TextAnchor.MiddleCenter;
            numText.color = Color.white;
            numText.raycastTarget = false;
            numText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            numObj.SetActive(false);

            // 注：不再挂 RedDotWidget 组件。RedDotDemoUI 在 SetupBindings 里
            // 直接通过 FindChild 找到 DotObj/NumText 并用 Bind(node.State,...) 绑定。
            // （RedDotWidget 作为独立 UIWidget 需要 UIModule 的 widget 创建流程，
            //  在 UIWindow 子节点直接挂载不会被自动激活。）
        }

        private static GameObject CreateButton(Transform parent, string name, string text, float yOffset)
        {
            var btnGo = new GameObject(name);
            btnGo.transform.SetParent(parent, false);
            var btnRect = btnGo.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0, 1);
            btnRect.anchorMax = new Vector2(1, 1);
            btnRect.pivot = new Vector2(0, 1);
            btnRect.anchoredPosition = new Vector2(0, yOffset);
            btnRect.sizeDelta = new Vector2(0, 40);

            var btnImage = btnGo.AddComponent<Image>();
            btnImage.color = new Color(0.2f, 0.4f, 0.8f, 1f);
            var button = btnGo.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.3f, 0.5f, 0.9f, 1f);
            colors.pressedColor = new Color(0.15f, 0.3f, 0.6f, 1f);
            button.colors = colors;

            // 按钮文字
            var txt = CreateText(btnGo.transform, "Text", text, 16, TextAnchor.MiddleCenter);
            var txtRect = txt.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = txtRect.offsetMax = Vector2.zero;

            return btnGo;
        }

        private static GameObject CreateText(Transform parent, string name, string text, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var uiText = go.AddComponent<Text>();
            uiText.text = text;
            uiText.fontSize = fontSize;
            uiText.alignment = anchor;
            uiText.color = Color.white;
            uiText.raycastTarget = false;
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return go;
        }

        private static void SetRect(GameObject go, float xMin, float yMin, float xMax, float yMax, Vector2 offsetMin, Vector2 offsetMax, Vector2 pivot)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            if (pivot != Vector2.zero) rect.pivot = pivot;
        }

        // ─── 场景构建 ───

        private const string GAME_ENTRY_PREFAB = "Assets/TEngine/Settings/Prefab/GameEntry.prefab";
        private const string UI_ROOT_PREFAB = "Assets/TEngine/Settings/Prefab/UIRoot.prefab";
        private const string PROCEDURE_SETTING_PATH = "Assets/TEngine/Settings/ProcedureSetting_RedDot.asset";

        private static void BuildScene(GameObject uiPrefab)
        {
            EnsureFolder("Assets/Scenes");

            // 先创建 ProcedureSetting_RedDot.asset（指向 ProcedureRedDotDemo）
            EnsureProcedureSetting();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Main Camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.orthographic = true;
            cam.orthographicSize = 5;
            camGo.AddComponent<AudioListener>();

            // Directional Light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            // UIRoot prefab（UIModule 显示 UI 时需要 UIRoot 节点，含 UICanvas/UICamera/EventSystem）
            var uiRootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UI_ROOT_PREFAB);
            if (uiRootPrefab != null)
            {
                PrefabUtility.InstantiatePrefab(uiRootPrefab, scene);
            }
            else
            {
                Debug.LogError($"[RedDotDemoBuilder] UIRoot prefab not found: {UI_ROOT_PREFAB}");
            }

            // GameEntry prefab（负责初始化框架：RootModule/ResourceModule/YooAsset/ProcedureSystem 等）
            // 这是关键：GameEntry 会启动 ProcedureSetting 的流程，触发 ProcedureRedDotDemo
            var gameEntryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GAME_ENTRY_PREFAB);
            if (gameEntryPrefab != null)
            {
                var gameEntry = (GameObject)PrefabUtility.InstantiatePrefab(gameEntryPrefab, scene);
                // 覆盖 GameEntry 上 Settings 的 procedureSetting，指向 ProcedureSetting_RedDot
                OverrideProcedureSetting(gameEntry);
            }
            else
            {
                Debug.LogError($"[RedDotDemoBuilder] GameEntry prefab not found: {GAME_ENTRY_PREFAB}");
            }

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            Debug.Log($"[RedDotDemoBuilder] Scene saved: {SCENE_PATH}");
        }

        /// <summary>创建/更新 ProcedureSetting_RedDot.asset，entrance 指向 ProcedureRedDotDemo。</summary>
        private static void EnsureProcedureSetting()
        {
            var so = AssetDatabase.LoadAssetAtPath<TEngine.ProcedureSetting>(PROCEDURE_SETTING_PATH);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<TEngine.ProcedureSetting>();
                AssetDatabase.CreateAsset(so, PROCEDURE_SETTING_PATH);
            }
            // 反射设置 private 字段
            var t = typeof(TEngine.ProcedureSetting);
            var availField = t.GetField("availableProcedureTypeNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var entranceField = t.GetField("entranceProcedureTypeName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            availField?.SetValue(so, new[] { "Procedure.ProcedureRedDotDemo" });
            entranceField?.SetValue(so, "Procedure.ProcedureRedDotDemo");
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RedDotDemoBuilder] ProcedureSetting created: {PROCEDURE_SETTING_PATH}");
        }

        /// <summary>覆盖 GameEntry 实例上 Settings 组件的 procedureSetting 字段为 ProcedureSetting_RedDot。</summary>
        private static void OverrideProcedureSetting(GameObject gameEntry)
        {
            var redDotSetting = AssetDatabase.LoadAssetAtPath<TEngine.ProcedureSetting>(PROCEDURE_SETTING_PATH);
            if (redDotSetting == null) return;

            // Settings 是 GameEntry 下的子物体（名为 "Settings"）
            var settingsTr = gameEntry.transform.Find("Settings");
            if (settingsTr == null)
            {
                // 可能在根上挂的 Settings 组件
                var rootSettings = gameEntry.GetComponent<TEngine.Settings>();
                if (rootSettings != null) SetProcedureSettingField(rootSettings, redDotSetting);
                return;
            }
            var settingsComp = settingsTr.GetComponent<TEngine.Settings>();
            if (settingsComp != null)
            {
                SetProcedureSettingField(settingsComp, redDotSetting);
            }
        }

        private static void SetProcedureSettingField(TEngine.Settings settings, TEngine.ProcedureSetting redDotSetting)
        {
            var t = typeof(TEngine.Settings);
            var field = t.GetField("procedureSetting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(settings, redDotSetting);
            // 标记 dirty，场景保存时 Unity 自动记录为 prefab override
            EditorUtility.SetDirty(settings);
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                string folderName = System.IO.Path.GetFileName(path);
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        /// <summary>
        /// 修复 prefab 根 RectTransform 被 CanvasScaler 置 0 的 scale。
        /// 直接通过 SerializedObject 修改 prefab 资产文件，绕过 CanvasScaler 的运行时逻辑。
        /// </summary>
        private static void FixRootRectTransformAfterSave(string prefabPath)
        {
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                Debug.LogError($"[RedDotDemoBuilder] FixRoot: prefab not found at {prefabPath}");
                return;
            }
            var rootRect = prefabAsset.GetComponent<RectTransform>();
            if (rootRect == null) return;

            // 通过 SerializedObject 直接改 prefab 资产（避免触发 CanvasScaler 的 HandleScale）
            var so = new SerializedObject(rootRect);
            so.FindProperty("m_LocalScale").vector3Value = Vector3.one;
            so.FindProperty("m_AnchorMin").vector2Value = Vector2.zero;
            so.FindProperty("m_AnchorMax").vector2Value = Vector2.one;
            so.FindProperty("m_SizeDelta").vector2Value = Vector2.zero;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(prefabAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RedDotDemoBuilder] Fixed root RectTransform: scale={rootRect.localScale}");
        }
    }
}
