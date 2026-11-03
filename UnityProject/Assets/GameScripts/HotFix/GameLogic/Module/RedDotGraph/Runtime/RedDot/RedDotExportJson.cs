using System;
using System.Collections.Generic;

namespace RedDot.Runtime.RedDot
{
    [Serializable]
    public class RedDotNodeJson
    {
        //红点节点全局key
        public string key;
        //红点状态类型
        public int redDotType;
        //逻辑ID
        public int externalId;
        //key 中文描述(K批8:随导出带给 RedDotKeys 生成器写注释,业务注册 checker 免回编辑器查语义;
        //仅导出流程内存流转,[NonSerialized] 后 JSON 文件不携带——运行时加载不消费此字段,实测占文件 11% 体积)
        [NonSerialized] public string desc;
    }

    [Serializable]
    public class RedDotEdgeJson
    {
        //当前红点节点
        public string key;
        //红点子节点列表
        public List<string> outKeys = new List<string>();
    }
    
    [Serializable]
    public class RedDotExportJson
    {
        public List<RedDotNodeJson> vertexs = new List<RedDotNodeJson>();
        public List<RedDotEdgeJson> edges = new List<RedDotEdgeJson>();
    }
}