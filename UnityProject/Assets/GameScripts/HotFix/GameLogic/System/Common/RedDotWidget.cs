using System;
using RedDot.Runtime.RedDot;
using TMPro;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 红点显示组件(UIWidget 包装,双模式):
    /// ①新图绑定——固定绑定读节点上 RedDotKeyBinder 序列化的 key,或运行时 SetKey(列表复用/Tab 数据驱动);
    /// ②本地直控——SetLocalState 不入树零订阅(邮件 item 等纯表现场景)。
    /// 任意 Set* 先拆另一模式在订订阅(互斥);六态渲染唯一出口 RenderState。
    /// </summary>
    public partial class RedDotWidget
    {
        private string m_key;               // 新图生效 key(binder 序列化值或 SetKey 运行时值)
        private uint m_bindId;              // 新图 per-id 绑定(cell 粒度用,常驻 UI 为 0)
        private bool m_bound;               // 新图树订阅中
        private bool m_localMode;           // 本地直控模式(无任何订阅)
        private int m_countCap = 99;        // 数字封顶显示(>=cap 显示 cap+)

        /// <summary>
        /// 状态修饰器（可选）：在 RenderState 前允许调用方修饰状态（例如槽位在图状态为 Null 时叠加业务层战力提升绿箭头）
        /// </summary>
        public Func<RedDotStatus, int, (RedDotStatus status, int num)> StatusModifier { get; set; }

        protected override void BindMemberProperty()
        {
            // 新图固定绑定:prefab 上 RedDotKeyBinder 序列化的 key(动态场景由调用方 SetKey 后置覆盖)
            var binder = gameObject.GetComponent<RedDotKeyBinder>();
            if (binder != null)
            {
                m_key = binder.RedDotKey;
                m_bindId = binder.BindId;
                m_countCap = binder.CountCap;
            }
        }

        protected override void RegisterEvent()
        {
            // 固定绑定生效(配置未就绪期走模块 P0-3 缓冲,加载完成后自动重放并回放一次回调)
            if (!string.IsNullOrEmpty(m_key))
            {
                BindNewGraph();
            }
        }

        protected override void OnDestroy()
        {
            StatusModifier = null;
            UnbindNewGraph();
        }

        #region 新红点图(RedDotGraph)绑定

        /// <summary>
        /// 新红点图绑定(语义 key)。运行时换绑唯一入口,列表复用/Tab 数据驱动均走此。
        /// </summary>
        /// <param name="key">语义 key(RedDotKeys.XXX);空串=解除绑定并熄灭</param>
        /// <param name="id">per-id 绑定(列表 cell 场景,常驻 UI 为 0)</param>
        public void SetKey(string key, uint id = 0)
        {
            var nextKey = key ?? string.Empty;
            if (m_key == nextKey && m_bindId == id && m_bound)
            {
                return;
            }
            UnbindNewGraph();
            m_localMode = false;
            m_key = nextKey;
            m_bindId = id;
            if (string.IsNullOrEmpty(nextKey))
            {
                var status = RedDotStatus.RedDotStatusNull;
                var num = 0;
                if (StatusModifier != null)
                {
                    (status, num) = StatusModifier(status, num);
                }
                RenderState(status, num);
                return;
            }
            // AddUI 注册即回放一次回调,绑定当帧即取到当前态
            BindNewGraph();
        }

        /// <summary>解除新图绑定并熄灭(回到未绑定态)。</summary>
        public void ClearKey()
        {
            UnbindNewGraph();
            m_localMode = false;
            m_key = string.Empty;
            m_bindId = 0;
            RenderState(RedDotStatus.RedDotStatusNull, 0);
        }

        /// <summary>
        /// 本地直控(不入树):纯表现场景。Null=全熄;只控红点显示,不改本节点显隐(整节点显隐归业务)。
        /// </summary>
        public void SetLocalState(RedDotStatus status, int num = 0)
        {
            if (!m_localMode)
            {
                UnbindNewGraph();
                m_localMode = true;
            }
            RenderState(status, num);
        }

        /// <summary>本地直控语法糖:红点 / 全熄。</summary>
        public void SetLocalVisible(bool on)
        {
            SetLocalState(on ? RedDotStatus.RedDotStatusRedDot : RedDotStatus.RedDotStatusNull);
        }

        private void BindNewGraph()
        {
            if (m_bound || string.IsNullOrEmpty(m_key))
            {
                return;
            }
            // 必须走模块收口(P0-3 未就绪缓冲契约),禁止直连 RedDotManager
            RedDotGraphModule.Instance.AddUI(m_key, OnGraphChanged, m_bindId);
            m_bound = true;
        }

        private void UnbindNewGraph()
        {
            if (!m_bound)
            {
                return;
            }
            RedDotGraphModule.Instance.RemoveUI(m_key, OnGraphChanged, m_bindId);
            m_bound = false;
        }

        private void OnGraphChanged()
        {
            if (m_localMode || gameObject == null || string.IsNullOrEmpty(m_key))
            {
                return;
            }
            RedDotGraphModule.Instance.GetRedDotStatus(m_key, m_bindId, out var status, out var num);
            if (StatusModifier != null)
            {
                (status, num) = StatusModifier(status, num);
            }
            RenderState(status, num);
        }

        /// <summary>强制按当前绑定和修饰器重刷一次状态显示</summary>
        public void Refresh()
        {
            if (m_bound && !string.IsNullOrEmpty(m_key))
            {
                OnGraphChanged();
            }
            else if (!m_localMode)
            {
                var status = RedDotStatus.RedDotStatusNull;
                var num = 0;
                if (StatusModifier != null)
                {
                    (status, num) = StatusModifier(status, num);
                }
                RenderState(status, num);
            }
        }

        #endregion

        
        #region 统一渲染

        /// <summary>
        /// 唯一渲染出口:先全熄再按单态点亮。新形态节点(六态槽)直渲;
        /// 旧形态节点(Gen 未绑定槽位全空)对应 SetActive 为空调用,安全无效果。
        /// </summary>
        private void RenderState(RedDotStatus status, int num)
        {
            if (gameObject == null)
            {
                return;
            }
            HideAllDots();
            switch (status)
            {
                case RedDotStatus.RedDotStatusRedDot:
                    m_goReddot?.SetActive(true);
                    break;
                case RedDotStatus.RedDotStatusBlueDot:
                    m_goBluedot?.SetActive(true);
                    break;
                case RedDotStatus.RedDotStatusRedNumber:
                    if (num > 0)
                    {
                        m_goReddotNum?.SetActive(true);
                        SetCountText(m_tmpReddot, num);
                    }
                    break;
                case RedDotStatus.RedDotStatusBlueNumber:
                    if (num > 0)
                    {
                        m_goBluedotNum?.SetActive(true);
                        SetCountText(m_tmpBluedot, num);
                    }
                    break;
                case RedDotStatus.RedDotStatusTips:
                    // 暂借红数字位承载"新"标签,专用槽位待 prefab 补节点后调整
                    m_goDotTips?.SetActive(true);
                    break;
                case RedDotStatus.RedDotStatusFull:
                    m_goFullDot?.SetActive(true);
                    break;
                default:
                    break;
            }
        }

        /// <summary>六态全熄(只熄红点子节点,不触碰本节点显隐)。</summary>
        public void HideAllDots()
        {
            if (gameObject == null)
            {
                return;
            }
            m_goFullDot?.SetActive(false);
            m_goDotTips?.SetActive(false);
            m_goReddot?.SetActive(false);
            m_goReddotNum?.SetActive(false);
            m_goBluedot?.SetActive(false);
            m_goBluedotNum?.SetActive(false);
        }

        private void SetCountText(TextMeshProUGUI text, int num)
        {
            if (text == null)
            {
                return;
            }
            // TMP SetText(format, float) 为零分配重载
            text.SetText(num >= m_countCap ? "{0}+" : "{1}", m_countCap, num);
        }
        
        #endregion
    }
}
