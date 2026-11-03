using System;
using System.Collections.Generic;

namespace RedDot.Editor.Data
{
    /// <summary>
    /// 红点分组(子图):组内是独立完整红点树,仅组根对外(终版设计 §5.1/D8 支持嵌套)。
    /// id 从 1 分配,0 保留=主图。dynamicMount=true 时组根不连静态父,运行时 SetDynamicParent 挂载。
    /// 注:本类不包 #if UNITY_EDITOR——被未包裹的 RedDotGraphData(编入热更 dll)序列化引用。
    /// </summary>
    [Serializable]
    public class RedDotGroup
    {
        public int id;
        public string name;
        public int rootNodeId;     // 组根 contextId——组对外唯一连接面
        public bool dynamicMount;

        /// <summary>幻影框手工摆放位置(组树骨架在主图的矩形左上,随资产保存;hasEditorPos=false 时用派生布局)。</summary>
        public float editorPosX;
        public float editorPosY;
        public bool hasEditorPos;
    }

    /// <summary>字典派生分组的节(D7 派生投影视图:Name=节名,Ids=字典条目 id)。仅编辑视图用,不落导出物。</summary>
    [Serializable]
    public class RedDotKeyGroupSection
    {
        public string Name;
        public List<int> Ids = new List<int>();
    }
}
