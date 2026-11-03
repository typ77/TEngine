using GameLogic;
using TEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RedDot.Editor.DemoBuilder
{
    /// <summary>
    /// 红点 Demo 一键构建工具（RedDotGraph 新图版）。
    /// 菜单：TEngine/RedDot/Build Demo Scene
    /// 自动生成 RedDotDemoUI.prefab（8 个 RedDotWidget 行:六态 8 槽位 + RedDotKeyBinder）+ RedDotDemoScene.unity。
    /// 槽位顺序必须与 System/Common/RedDotWidget_Gen.g.cs 的 GetComponent 索引严格一致:
    /// 0=goFullDot 1=goDotTips 2=goReddot 3=goReddotNum 4=tmpReddot(TMP) 5=goBluedot 6=goBluedotNum 7=tmpBluedot(TMP)。
    /// 注意:工程未导入 TMP Essentials 时数字字体由 RedDotDemoUI.EnsureTmpFont 运行时兜底;
    /// 导入 TMP Essentials(Window &gt; TextMeshPro &gt; Import TMP Essential Resources)后重建 prefab 即走默认字体。
    /// </summary>
    public static class RedDotDemoBuilder
    {
        private const string UI_PREFAB_PATH = "Assets/AssetRaw/UI/RedDotDemoUI.prefab";
        private const string SCENE_PATH = "Assets/Scenes/RedDotDemoScene.unity";

        /// <summary>行定义:节点名/标签文案/RedDotKeyBinder 序列化 key(空=动态行 SetKey 换绑)。</summary>
        private static readonly string[][] RowDefs =
        {
            new[] { "RowRoot", "Root (根,全态聚合 63)", "Root" },
            new[] { "RowMail", "Mail (邮件,聚合 红点|蓝点|红数字)", "Mail" },
            new[] { "RowUnread", "Mail.Unread (红数字 4)", "Mail.Unread" },
            new[] { "RowAnnounce", "Mail.Announce (红点|蓝点 3)", "Mail.Announce" },
            new[] { "RowLimitTime", "Shop.LimitTime (满 32)", "Shop.LimitTime" },
            new[] { "RowSignIn", "Activity.SignIn (新标签 16)", "Activity.SignIn" },
            new[] { "RowWelfare", "Activity.Welfare (蓝数字 8)", "Activity.Welfare" },
            new[] { "RowDynamic", "动态节点 (活动期次)", "" },
        };

        /// <summary>按钮定义:节点名/文案。</summary>
        private static readonly string[][] ButtonDefs =
        {
            new[] { "m_btn_AddUnread", "+1 未读邮件" },
            new[] { "m_btn_ReadOne", "读取一封" },
            new[] { "m_btn_ToggleAnnounce", "公告 红→蓝→关" },
            new[] { "m_btn_ToggleShopFull", "限时商店 满/空" },
            new[] { "m_btn_ToggleSignIn", "签到 领取/重置" },
            new[] { "m_btn_AddWelfare", "+1 福利" },
            new[] { "m_btn_ClaimWelfare", "-1 福利" },
            new[] { "m_btn_AddDynamic", "新增动态节点" },
            new[] { "m_btn_RemoveDynamic", "移除动态节点" },
            new[] { "m_btn_ToggleWelfareEnabled", "门控 禁用/启用 Welfare" },
        };

        [MenuItem("TEngine/RedDot/Build Demo Scene", false, 100)]
        public static void BuildDemo()
        {
            EditorUtility.DisplayProgressBar("RedDot Demo", "Building UI prefab...", 0.3f);
            var uiPrefab = BuildUIPrefab();

            EditorUtility.DisplayProgressBar("RedDot Demo", "Building scene...", 0.7f);
            BuildScene(uiPrefab);

            EditorUtility.ClearProgressBar();
            // 用 Debug.Log 替代 DisplayDialog，避免 execute_menu_item 后台执行时被模态对话框阻塞
            Debug.Log(Utility.Text.Format(
                "[RedDotDemoBuilder] Demo 构建完成!\nUI Prefab: {0}\nScene: {1}\n打开场景 {1} 即可运行。",
                UI_PREFAB_PATH, SCENE_PATH));
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

            // UIWindow 脚本运行时由 UIModule 挂载,prefab 根只保存 UI 节点结构
            var rect = canvasGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            // 显式重置 scale 为 1（避免 CanvasScaler 在某些情况下把 localScale 置 0 导致 UI 不可见）
            rect.localScale = Vector3.one;

            // 标题
            var title = CreateText(canvasGo.transform, "Title", "红点系统 Demo - RedDotGraph 六态图", 24, TextAnchor.UpperCenter);
            SetRect(title, 0f, 1f, 1f, 1f, new Vector2(0, -60), Vector2.zero, new Vector2(0.5f, 1));

            // 红点显示区（左上:8 行）
            var displayArea = new GameObject("DisplayArea");
            displayArea.transform.SetParent(canvasGo.transform, false);
            var displayRect = displayArea.AddComponent<RectTransform>();
            displayRect.anchorMin = new Vector2(0.02f, 0.3f);
            displayRect.anchorMax = new Vector2(0.52f, 0.92f);
            displayRect.offsetMin = displayRect.offsetMax = Vector2.zero;

            var yOffset = 0f;
            foreach (var rowDef in RowDefs)
            {
                CreateRedDotRow(displayArea.transform, rowDef[0], rowDef[1], rowDef[2], yOffset);
                yOffset -= 50f;
            }

            // 按钮区（右侧:10 个）
            var btnArea = new GameObject("BtnArea");
            btnArea.transform.SetParent(canvasGo.transform, false);
            var btnRect = btnArea.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.55f, 0.03f);
            btnRect.anchorMax = new Vector2(0.98f, 0.97f);
            btnRect.offsetMin = btnRect.offsetMax = Vector2.zero;

            var btnYOffset = 0f;
            foreach (var btnDef in ButtonDefs)
            {
                CreateButton(btnArea.transform, btnDef[0], btnDef[1], btnYOffset);
                btnYOffset -= 46f;
            }

            // 日志区（左下）
            var logContainer = new GameObject("TextLogContainer", typeof(RectTransform));
            logContainer.transform.SetParent(canvasGo.transform, false);
            SetRect(logContainer, 0.02f, 0.02f, 0.52f, 0.27f, Vector2.zero, Vector2.zero, Vector2.zero);
            var logBg = logContainer.AddComponent<Image>();
            logBg.color = new Color(0, 0, 0, 0.4f);
            logBg.raycastTarget = false;
            var logText = CreateText(logContainer.transform, "m_text_Log", "", 14, TextAnchor.UpperLeft);
            var logTextRect = logText.GetComponent<RectTransform>();
            logTextRect.anchorMin = Vector2.zero;
            logTextRect.anchorMax = Vector2.one;
            logTextRect.offsetMin = new Vector2(8, 4);
            logTextRect.offsetMax = new Vector2(-8, -4);

            // 保存为 prefab
            EnsureFolder("Assets/AssetRaw/UI");
            PrefabUtility.SaveAsPrefabAsset(canvasGo, UI_PREFAB_PATH);
            Object.DestroyImmediate(canvasGo);

            // CanvasScaler 在 SaveAsPrefabAsset 序列化时会把根 RectTransform 的 scale 置 0
            // （prefab 无 Canvas 父级，CanvasScaler 算出 scaleFactor=0），保存后强制修正
            FixRootRectTransformAfterSave(UI_PREFAB_PATH);

            Debug.Log(Utility.Text.Format("[RedDotDemoBuilder] UI prefab saved: {0}", UI_PREFAB_PATH));
            return AssetDatabase.LoadAssetAtPath<GameObject>(UI_PREFAB_PATH);
        }

        /// <summary>构建一个红点行:widget 节点(UIBindComponent 8 槽位 + RedDotKeyBinder) + 六态槽位子节点。</summary>
        private static void CreateRedDotRow(Transform parent, string rowName, string labelText, string redDotKey, float yOffset)
        {
            var row = new GameObject(rowName);
            row.transform.SetParent(parent, false);
            var rowRect = row.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0, 1);
            rowRect.anchorMax = new Vector2(1, 1);
            rowRect.pivot = new Vector2(0, 1);
            rowRect.anchoredPosition = new Vector2(0, yOffset);
            rowRect.sizeDelta = new Vector2(0, 44);

            // 标签（非交互,关 Raycast）
            var label = CreateText(row.transform, "Label", labelText, 15, TextAnchor.MiddleLeft);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(0.62f, 1);
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

            // 红点六态槽位容器（右侧）
            var container = new GameObject("RedDotContainer");
            container.transform.SetParent(row.transform, false);
            var containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.65f, 0);
            containerRect.anchorMax = new Vector2(1, 1);
            containerRect.offsetMin = containerRect.offsetMax = Vector2.zero;

            // 六态槽位（全部默认隐藏,RedDotWidget.RenderState 唯一出口驱动显隐）
            var goFullDot = CreateDot(container.transform, "m_goFullDot", new Color(0.95f, 0.45f, 0.1f));   // 满:橙
            var goDotTips = CreateDot(container.transform, "m_goDotTips", new Color(1f, 0.85f, 0.2f));       // 新:黄
            var goReddot = CreateDot(container.transform, "m_goReddot", Color.red);                          // 红点
            var goReddotNum = CreateNumSlot(container.transform, "m_goReddotNum", new Color(0.85f, 0.15f, 0.15f)); // 红数字
            var goBluedot = CreateDot(container.transform, "m_goBluedot", new Color(0.25f, 0.5f, 1f));       // 蓝点
            var goBluedotNum = CreateNumSlot(container.transform, "m_goBluedotNum", new Color(0.15f, 0.35f, 0.85f)); // 蓝数字

            // UIBindComponent 槽位表:顺序与 RedDotWidget_Gen.g.cs 的 GetComponent 索引严格一致
            var bind = row.AddComponent<UIBindComponent>();
            var so = new SerializedObject(bind);
            var prop = so.FindProperty("m_components");
            prop.arraySize = 8;
            prop.GetArrayElementAtIndex(0).objectReferenceValue = goFullDot.GetComponent<RectTransform>();
            prop.GetArrayElementAtIndex(1).objectReferenceValue = goDotTips.GetComponent<RectTransform>();
            prop.GetArrayElementAtIndex(2).objectReferenceValue = goReddot.GetComponent<RectTransform>();
            prop.GetArrayElementAtIndex(3).objectReferenceValue = goReddotNum.GetComponent<RectTransform>();
            prop.GetArrayElementAtIndex(4).objectReferenceValue = goReddotNum.GetComponentInChildren<TextMeshProUGUI>(true);
            prop.GetArrayElementAtIndex(5).objectReferenceValue = goBluedot.GetComponent<RectTransform>();
            prop.GetArrayElementAtIndex(6).objectReferenceValue = goBluedotNum.GetComponent<RectTransform>();
            prop.GetArrayElementAtIndex(7).objectReferenceValue = goBluedotNum.GetComponentInChildren<TextMeshProUGUI>(true);
            so.ApplyModifiedPropertiesWithoutUndo();

            // RedDotKeyBinder 序列化 key（动态行留空,RedDotDemoUI 用 SetKey 换绑）
            var binder = row.AddComponent<RedDotKeyBinder>();
            var binderSo = new SerializedObject(binder);
            binderSo.FindProperty("m_redDotKey").stringValue = redDotKey ?? string.Empty;
            binderSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>圆点槽位（Image,默认隐藏,非交互）。</summary>
        private static GameObject CreateDot(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(26, 26);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>数字槽位（底色 Image 节点 + 子节点 TextMeshProUGUI 数字,默认隐藏）。
        /// UGUI 单节点不能同时挂 Image 与 TextMeshProUGUI,故拆两层;TMP 子节点供槽位 4/7 取用。</summary>
        private static GameObject CreateNumSlot(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(40, 26);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.type = Image.Type.Simple;
            image.raycastTarget = false;

            var textGo = new GameObject(name + "_Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "0";
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            go.SetActive(false);
            return go;
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
            btnRect.sizeDelta = new Vector2(0, 38);

            var btnImage = btnGo.AddComponent<Image>();
            btnImage.color = new Color(0.2f, 0.4f, 0.8f, 1f);
            var button = btnGo.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.3f, 0.5f, 0.9f, 1f);
            colors.pressedColor = new Color(0.15f, 0.3f, 0.6f, 1f);
            button.colors = colors;

            var txt = CreateText(btnGo.transform, "Text", text, 15, TextAnchor.MiddleCenter);
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
            if (pivot != Vector2.zero)
            {
                rect.pivot = pivot;
            }
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
                Debug.LogError(Utility.Text.Format("[RedDotDemoBuilder] UIRoot prefab not found: {0}", UI_ROOT_PREFAB));
            }

            // GameEntry prefab（负责初始化框架：RootModule/ResourceModule/YooAsset/ProcedureSystem 等）
            // GameEntry 会启动 ProcedureSetting 的流程，触发 ProcedureRedDotDemo
            var gameEntryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GAME_ENTRY_PREFAB);
            if (gameEntryPrefab != null)
            {
                var gameEntry = (GameObject)PrefabUtility.InstantiatePrefab(gameEntryPrefab, scene);
                // 覆盖 GameEntry 上 Settings 的 procedureSetting，指向 ProcedureSetting_RedDot
                OverrideProcedureSetting(gameEntry);
            }
            else
            {
                Debug.LogError(Utility.Text.Format("[RedDotDemoBuilder] GameEntry prefab not found: {0}", GAME_ENTRY_PREFAB));
            }

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            Debug.Log(Utility.Text.Format("[RedDotDemoBuilder] Scene saved: {0}", SCENE_PATH));
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
            Debug.Log(Utility.Text.Format("[RedDotDemoBuilder] ProcedureSetting created: {0}", PROCEDURE_SETTING_PATH));
        }

        /// <summary>覆盖 GameEntry 实例上 Settings 组件的 procedureSetting 字段为 ProcedureSetting_RedDot。</summary>
        private static void OverrideProcedureSetting(GameObject gameEntry)
        {
            var redDotSetting = AssetDatabase.LoadAssetAtPath<TEngine.ProcedureSetting>(PROCEDURE_SETTING_PATH);
            if (redDotSetting == null)
            {
                return;
            }

            // Settings 是 GameEntry 下的子物体（名为 "Settings"）
            var settingsTr = gameEntry.transform.Find("Settings");
            if (settingsTr == null)
            {
                // 可能在根上挂的 Settings 组件
                var rootSettings = gameEntry.GetComponent<TEngine.Settings>();
                if (rootSettings != null)
                {
                    SetProcedureSettingField(rootSettings, redDotSetting);
                }
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
                Debug.LogError(Utility.Text.Format("[RedDotDemoBuilder] FixRoot: prefab not found at {0}", prefabPath));
                return;
            }
            var rootRect = prefabAsset.GetComponent<RectTransform>();
            if (rootRect == null)
            {
                return;
            }

            var so = new SerializedObject(rootRect);
            so.FindProperty("m_LocalScale").vector3Value = Vector3.one;
            so.FindProperty("m_AnchorMin").vector2Value = Vector2.zero;
            so.FindProperty("m_AnchorMax").vector2Value = Vector2.one;
            so.FindProperty("m_SizeDelta").vector2Value = Vector2.zero;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(prefabAsset);
            AssetDatabase.SaveAssets();
            Debug.Log(Utility.Text.Format("[RedDotDemoBuilder] Fixed root RectTransform: scale={0}", rootRect.localScale));
        }
    }
}
