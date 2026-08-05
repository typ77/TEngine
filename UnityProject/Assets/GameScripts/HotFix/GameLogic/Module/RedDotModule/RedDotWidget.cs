using GameLogic.DataBinding;
using GameLogic.RedDot.Core;
using TEngine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.RedDot
{
    /// <summary>
    /// 红点 UI 组件。挂在需要显示红点的 UI 节点上，配置红点 key 即可自动刷新。
    ///
    /// 使用方式：
    /// 1. 在 prefab 上挂 RedDotWidget（或作为 UIWidget 子项内嵌）
    /// 2. Inspector 配置 RedDotKey（与配置表中的 key 一致）
    /// 3. 拖入红点 GameObject 引用（可选：数字文本）
    ///
    /// 刷新逻辑（由 MVE 的 BindableProperty 自动驱动）：
    /// - Status == Null：隐藏红点
    /// - Status == Number：显示红点 + 数字（超过 99 显示 "99+"）
    /// - 其它状态（Normal/New/Full）：显示红点，不显示数字
    ///
    /// 生命周期：绑定在 <see cref="UIBase.SetupBindings"/> 中完成，
    /// <see cref="UIBase.RemoveAllBindings"/> 在组件销毁时自动调用，无需手动解绑。
    /// </summary>
    public class RedDotWidget : UIWidget
    {
        [Tooltip("红点节点 key（与 RedDotSystem 配置一致）")]
        [SerializeField] private string m_redDotKey;

        [Tooltip("红点根节点（显隐控制）")]
        [SerializeField] private GameObject m_dotObj;

        [Tooltip("数字文本（可选，仅 Number 状态显示）")]
        [SerializeField] private TMP_Text m_numText;

        /// <summary>
        /// 当前绑定的红点 key。运行时可修改（会重新绑定）。
        /// </summary>
        public string RedDotKey
        {
            get => m_redDotKey;
            set
            {
                if (m_redDotKey == value) return;
                m_redDotKey = value;
                if (IsPrepare)
                {
                    RemoveAllBindings();
                    BindRedDot();
                }
            }
        }

        protected override void ScriptGenerator()
        {
            // 节点引用通过 Inspector 序列化，无需 FindChild
        }

        protected override void SetupBindings()
        {
            base.SetupBindings();
            BindRedDot();
        }

        private void BindRedDot()
        {
            if (string.IsNullOrEmpty(m_redDotKey))
            {
                if (m_dotObj != null) m_dotObj.SetActive(false);
                return;
            }

            var node = RedDotSystem.Instance.GetNode(m_redDotKey);
            if (node == null)
            {
                Log.Warning($"[RedDotWidget] 未找到红点节点：{m_redDotKey}（widget: {name}）");
                if (m_dotObj != null) m_dotObj.SetActive(false);
                return;
            }

            Bind(node.State, OnRedDotStateChanged);
        }

        private void OnRedDotStateChanged(RedDotNodeState state)
        {
            if (m_dotObj == null) return;

            bool showDot = state.IsActive;
            m_dotObj.SetActive(showDot);

            if (m_numText != null)
            {
                bool showNum = state.Status == RedDotStatus.Number && state.Count > 0;
                m_numText.gameObject.SetActive(showNum);
                if (showNum)
                {
                    m_numText.text = state.Count > 99 ? "99+" : state.Count.ToString();
                }
            }
        }
    }
}
