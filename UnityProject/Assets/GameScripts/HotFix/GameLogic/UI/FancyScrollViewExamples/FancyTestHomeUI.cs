using UnityEngine.UI;
using TEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    /// <summary>
    /// FancyScrollView 示例测试主界面。
    /// 提供 Dropdown 导航 9 个 Example UIWindow。
    /// 每个 Example 独立运行，互不影响。
    /// </summary>
    [Window(UILayer.UI)]
    public class FancyTestHomeUI : UIWindow
    {
        // ── Prefab 节点引用 ──────────────────────────
        private Dropdown m_dropdownExamples;
        private Text m_textTitle;

        // ── Example 映射 ─────────────────────────────
        private static readonly string[] ExampleNames =
        {
            "01_Basic",
            //"02_FocusOn", 功能已经与01_Basic合并。无需单独制作
            "03_InfiniteScroll",
            "04_Metaball",
            "05_Voronoi",
            "06_LoopTabBar",
            "07_ScrollRect",
            "08_GridView",
            "09_LoadTexture"
        };

        /// <summary>
        /// 当前打开的 Example 窗口类型全名（含命名空间），null 表示无。
        /// </summary>
        private string _currentExampleTypeName;

        // ── 生命周期 ────────────────────────────────

        protected override void ScriptGenerator()
        {
            m_dropdownExamples = FindChildComponent<Dropdown>("m_dropdownExamples");
            m_textTitle = FindChildComponent<Text>("m_textTitle");
        }

        protected override void OnCreate()
        {
            // 初始化标题
            if (m_textTitle != null)
            {
                m_textTitle.text = "FancyScrollView Examples";
            }

            // 初始化 Dropdown 选项
            if (m_dropdownExamples != null)
            {
                m_dropdownExamples.options.Clear();
                m_dropdownExamples.options.Add(new Dropdown.OptionData("-- Select Example --"));
                for (int i = 0; i < ExampleNames.Length; i++)
                {
                    m_dropdownExamples.options.Add(new Dropdown.OptionData(ExampleNames[i]));
                }
                m_dropdownExamples.captionText.text = "-- Select Example --";
                m_dropdownExamples.onValueChanged.AddListener(OnExampleSelected);
            }
        }

        protected override void OnDestroy()
        {
            if (m_dropdownExamples != null)
            {
                m_dropdownExamples.onValueChanged.RemoveListener(OnExampleSelected);
            }
        }

        // ── Example 切换 ─────────────────────────────

        private void OnExampleSelected(int index)
        {
            if (index <= 0 || index > ExampleNames.Length)
            {
                // 选中占位项或越界，忽略
                return;
            }

            int exampleIndex = index - 1; // 扣除占位项偏移
            string exampleName = ExampleNames[exampleIndex];
            // 编号取自 exampleName 前缀（如 "03"），而非数组下标，避免已注释 Example 导致错位
            string typeName = $"GameLogic.Example{exampleName.Substring(0, 2)}_{exampleName.Substring(3)}UI";

            // 尝试关闭当前 Example
            CloseCurrentExample();

            // 尝试打开新 Example
            TryShowExample(typeName, exampleName);
        }

        private void TryShowExample(string typeName, string displayName)
        {
            var type = System.Type.GetType(typeName);
            if (type == null)
            {
                Log.Warning($"[FancyTestHomeUI] Example not implemented yet: {displayName} ({typeName})");
                return;
            }

            _currentExampleTypeName = typeName;

            // 直接调用非泛型重载 ShowUIAsync(Type, params object[])
            GameModule.UI.ShowUIAsync(type);

            Log.Info($"[FancyTestHomeUI] Opened: {displayName}");
        }

        private void CloseCurrentExample()
        {
            if (string.IsNullOrEmpty(_currentExampleTypeName))
            {
                return;
            }

            var type = System.Type.GetType(_currentExampleTypeName);
            if (type != null)
            {
                // 直接调用非泛型重载 CloseUI(Type)
                GameModule.UI.CloseUI(type);
            }
            _currentExampleTypeName = null;
        }
    }
}
