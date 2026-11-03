using System.Collections.Generic;

namespace RedDot.Editor.Config
{
    /// <summary>
    /// 红点节点状态六态(编辑器勾选框口径,显示标签见 RedDotEditorConfig.InitConfig)。
    /// 数值与运行时 RedDotStatus 逐值对齐。注意成员名是 P0-2 历史命名,与运行时现名按"值"对应而非按"名":
    /// Tips(1)=RedDotStatusRedDot 红点、BlueNoNumber(2)=RedDotStatusBlueDot 蓝点、Default(4)=RedDotStatusRedNumber 红数字、
    /// BlueNumber(8)=RedDotStatusBlueNumber 蓝数字、New(16)=RedDotStatusTips 新标签位、Full(32)=RedDotStatusFull 满。
    /// </summary>
    public enum RedDotNodeContextStatus
    {
        Tips = 1,
        BlueNoNumber = 2,
        Default = 4,
        BlueNumber = 8,
        New = 16,
        Full = 32,
    }

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

    public class RedDotEditorConfig
    {
        public const string EXTENSION = "reddotgraph";
        public const string EXPORT_EXTENSION = "json";
        public const string KEY_WINDOW_TITLE = "Key设置";
        public const string EXTERNAL_ID_WINDOW_TITLE = "逻辑ID设置";

        /// <summary>运行时配置标准落点(K批10:收集器对 Configs/Other 按 AddressByFileName 寻址,
        /// 文件名即模块加载地址 RedDotGraphConfig——导出对话框默认预填此路径;导去别处游戏加载不到,会追加确认)。</summary>
        public const string DEFAULT_EXPORT_PATH = "Assets/AssetRaw/Configs/Other/RedDotGraphConfig.json";

        //K批(2026-09-17):ENUM_CLASS_TEMPLATE 移除(RedDotKeyEnum 枚举导出死工作流,产物全库零消费者;
        //业务消费导出链同步生成的 RedDotKeys 常量表)

        private static Dictionary<RedDotNodeContextStatus, RedDotStatusConfig> s_statusConfig;

        private static void InitConfig()
        {
            if (s_statusConfig != null) return;
            s_statusConfig = new Dictionary<RedDotNodeContextStatus, RedDotStatusConfig>();
            s_statusConfig.Add(RedDotNodeContextStatus.Tips, new RedDotStatusConfig(
                "红点",
                "Icons/icon_normal"
                ));
            s_statusConfig.Add(RedDotNodeContextStatus.BlueNoNumber, new RedDotStatusConfig(
                "蓝点",
                "Icons/icon_normal2"   // 占位:蓝点专用图标待 UI 资源补充
                ));
            s_statusConfig.Add(RedDotNodeContextStatus.Default, new RedDotStatusConfig(
                "红数字",
                "Icons/icon_number"
                ));
            s_statusConfig.Add(RedDotNodeContextStatus.BlueNumber, new RedDotStatusConfig(
                "蓝数字",
                "Icons/icon_number_blue"   // 占位:蓝数字专用图标待 UI 资源补充
                ));
            s_statusConfig.Add(RedDotNodeContextStatus.New, new RedDotStatusConfig(
                "新",
                "Icons/icon_new"
            ));
            s_statusConfig.Add(RedDotNodeContextStatus.Full, new RedDotStatusConfig(
                "满",
                "Icons/icon_full"
            ));
        }

        public static RedDotStatusConfig GetStatusConfig(RedDotNodeContextStatus status)
        {
            InitConfig();
            return s_statusConfig[status];
        }
    }
}
