using System.Collections.Generic;

namespace GameLogic.RedDot.Editor.Config
{
    /// <summary>
    /// 编辑器节点可选的红点状态（与运行时 <see cref="Core.RedDotStatus"/> 位掩码一致）。
    /// </summary>
    public enum RedDotNodeContextStatus
    {
        Normal = 1,
        Number = 2,
        New = 4,
        Full = 8,
    }

    /// <summary>
    /// 红点状态的编辑器显示配置（描述 + 图标资源名）。
    /// </summary>
    public class RedDotStatusConfig
    {
        public readonly string StatusDesc;
        public readonly string Icon;

        public RedDotStatusConfig(string statusDesc, string icon)
        {
            StatusDesc = statusDesc;
            Icon = icon;
        }
    }

    /// <summary>
    /// 红点编辑器全局配置：文件扩展名、窗口标题、状态图标映射。
    /// </summary>
    public static class RedDotEditorConfig
    {
        /// <summary>红点图资产扩展名（ScriptedImporter 注册）。</summary>
        public const string EXTENSION = "reddotgraph";

        /// <summary>导出运行时配置的 JSON 扩展名。</summary>
        public const string EXPORT_EXTENSION = "json";

        public const string KEY_WINDOW_TITLE = "Key设置";
        public const string EXTERNAL_ID_WINDOW_TITLE = "逻辑ID设置";

        private static Dictionary<RedDotNodeContextStatus, RedDotStatusConfig> s_statusConfig;

        private static void InitConfig()
        {
            if (s_statusConfig != null) return;
            s_statusConfig = new Dictionary<RedDotNodeContextStatus, RedDotStatusConfig>
            {
                { RedDotNodeContextStatus.Normal, new RedDotStatusConfig("红点", "Icons/icon_normal") },
                { RedDotNodeContextStatus.Full, new RedDotStatusConfig("满", "Icons/icon_full") },
                { RedDotNodeContextStatus.New, new RedDotStatusConfig("新", "Icons/icon_new") },
                { RedDotNodeContextStatus.Number, new RedDotStatusConfig("数字", "Icons/icon_number") },
            };
        }

        public static RedDotStatusConfig GetStatusConfig(RedDotNodeContextStatus status)
        {
            InitConfig();
            return s_statusConfig[status];
        }
    }
}
