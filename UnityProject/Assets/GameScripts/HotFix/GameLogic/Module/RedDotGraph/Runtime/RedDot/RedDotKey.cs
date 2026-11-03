namespace RedDot.Runtime.RedDot
{
    /// <summary>
    /// P0-5:红点 key 常量元数据(生成表 RedDotKeys 的条目类型)。key 字符串与 externalId 一体携带,
    /// 业务经 RegisterChecker(RedDotKeys.XXX, checker) 注册、DataChange(RedDotKeys.XXX) 报脏——
    /// 拼错=编译错,externalId 魔法数清零。隐式转 string,可直接传入一切 key 参数位。
    /// </summary>
    public sealed class RedDotKey
    {
        /// <summary>红点节点全局 key(与图配置/导出 JSON 一致)</summary>
        public readonly string Key;

        /// <summary>外部接口 ID(与图配置一致;聚合节点=-1,不可注册 checker)</summary>
        public readonly int ExternalId;

        public RedDotKey(string key, int externalId)
        {
            Key = key;
            ExternalId = externalId;
        }

        public static implicit operator string(RedDotKey key)
        {
            return key.Key;
        }

        public override string ToString()
        {
            return Key;
        }
    }
}
