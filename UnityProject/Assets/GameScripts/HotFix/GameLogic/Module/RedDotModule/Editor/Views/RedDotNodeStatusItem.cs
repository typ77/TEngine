using GameLogic.RedDot.Editor.Config;
using GameLogic.RedDot.Editor.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic.RedDot.Editor.Views
{
    /// <summary>
    /// 节点上的单个状态切换项（图标 + Toggle）。
    /// 纯代码构建（无 UXML 依赖），Toggle 变化时更新 nodeContext.redDotType 位掩码。
    /// </summary>
    public class RedDotNodeStatusItem : VisualElement
    {
        private readonly Image _iconImg;
        private readonly RedDotNodeContext _nodeContext;
        private readonly RedDotGraphData _graphData;

        public RedDotNodeStatusItem(RedDotNodeContextStatus status, RedDotNodeContext nodeContext, RedDotGraphData graphData)
        {
            _nodeContext = nodeContext;
            _graphData = graphData;

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            _iconImg = new Image { scaleMode = ScaleMode.ScaleAndCrop, style = { width = 20, height = 20, marginRight = 2 } };
            Add(_iconImg);

            var toggle = new Toggle();
            toggle.RegisterCallback<ChangeEvent<bool>>(evt =>
            {
                nodeContext.redDotType += (evt.newValue ? 1 : -1) * (int)status;
                graphData.Dirty();
            });
            toggle.value = (nodeContext.redDotType & (int)status) > 0;
            Add(toggle);

            SetRedDotStatus(status);
        }

        private void SetRedDotStatus(RedDotNodeContextStatus status)
        {
            var cfg = RedDotEditorConfig.GetStatusConfig(status);
            if (cfg == null) return;
            _iconImg.image = Resources.Load<Texture2D>(cfg.Icon);
        }
    }
}
