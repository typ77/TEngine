#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RedDot.Editor.Views
{
    /// <summary>
    /// 红点编辑器使用说明窗(K批9 2026-09-17):工具栏"使用说明"或菜单 Tools/红点系统/使用说明 打开。
    /// 纯静态富文本文档,与图数据无交互;内容改这里一处即可。
    /// </summary>
    public class RedDotHelpWindow : EditorWindow
    {
        private Vector2 _scroll;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _bodyStyle;

        public static void ShowWindow()
        {
            var win = GetWindow<RedDotHelpWindow>(typeof(RedDotWindow));
            win.titleContent = new GUIContent("红点编辑器使用说明");
            win.minSize = new Vector2(760f, 560f);
            win.Focus();
        }

        private static void EnsureStyles()
        {
            if (_sectionStyle != null)
            {
                return;
            }
            _sectionStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 14,
            };
            _sectionStyle.margin.top = 10;
            _sectionStyle.margin.bottom = 2;
            _bodyStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
            };
            _bodyStyle.margin.left = 12;
            _bodyStyle.margin.bottom = 4;
        }

        private void OnGUI()
        {
            EnsureStyles();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var section in BuildSections())
            {
                EditorGUILayout.LabelField(section.Key, _sectionStyle);
                EditorGUILayout.LabelField(section.Value, _bodyStyle);
            }
            EditorGUILayout.EndScrollView();
        }

        private static List<KeyValuePair<string, string>> BuildSections()
        {
            var sections = new List<KeyValuePair<string, string>>();
            void Add(string title, string body)
            {
                sections.Add(new KeyValuePair<string, string>(
                    "<b><color=#FFD159>▍" + title + "</color></b>", body));
            }

            Add("总览:红点系统链路",
                "编辑器画图 → <b>保存</b>(.reddotgraph 编辑器源资产,与游戏无关) → <b>导出红点配置</b>(运行时 JSON + 自动再生成 RedDotKeys.cs) → 业务接入。\n" +
                "运行时由 YooAsset 加载 JSON 建图;加载期自动做「JSON vs 常量表」双向对账,版本错配启动即报。\n" +
                "<color=#E0A0A0>注意:「保存」只写编辑器源,要让游戏看到变化必须「导出」。</color>");

            Add("定义:红点 key",
                "节点的<b>全局唯一字符串身份</b>,运行时与业务都认它。命名规范:PascalCase + 下划线分层(如 <color=#8FD48F>MailEntry</color>、<color=#8FD48F>HolidayRoot</color>);<color=#E0A0A0>禁止包含 ':'</color>。<b>两个节点不得配同一个 key</b>(导出会产重复顶点,运行时整份加载失败)。\n" +
                "列表/搜索里「12:MailEntry」前缀的数字是<b>字典条目 ID</b>——编辑器内部代理键,自动递增,无语义。改 key 字符串 = 该条目被引用的所有节点一起改名(节点按 ID 间接引用)。");

            Add("定义:红点逻辑ID(externalId)",
                "节点的<b>业务数据源路由号</b>。纯内部代理键,数值无语义、自动递增——<b>业务禁止手写数字,只认 RedDotKeys.XXX 常量</b>。\n" +
                "<b>-1 = 纯聚合哨兵</b>:该节点没有自身数据源,状态完全由子节点聚合而来,不可注册 checker(逻辑ID搜索窗第一项可直选)。运行时做列表汇总时会给这类节点分配<b>负数系统段</b> id(自 -1000001 起),与配置号空间天然隔离。");

            Add("定义:红点可存在状态(掩码)与聚合规则",
                "六态掩码;覆盖优先级(高→低,2026-09-20 重定):<color=#8FD48F>满 > 红点 > 蓝点 > 红数字 > 蓝数字 > 新</color>(位值为固定兼容值,与优先级解耦)。节点上六枚图标 = 该节点<b>允许出现的状态集合</b>。\n" +
                "聚合规则(过滤式):子状态 ∩ 父掩码,为空不采纳;数字态同态累加数值,跨态按覆盖优先级取高。\n" +
                "<color=#E0A0A0>⇒ 最常见的「配了不亮」:父节点掩码没有勾全子树实际会产生的状态,子贡献被过滤掉。</color>");

            Add("定义:组 / 组根 / 引用节点 / 动态组",
                "<b>组</b>:画布分区 + 树面板导航单位;组与组的嵌套关系<b>派生自引用节点接线</b>,无独立父字段。\n" +
                "<b>组根</b>:建组即自动创建(橙色框,组内树的起点)。组根不可删、不可移、禁止被连入。\n" +
                "<b>引用节点</b>:在主图/父组里代表「整个组」的占位节点(无 key)。禁止出边;每组至多一个引用(单拷贝);给引用节点<b>连入边 = 把组挂到树上</b>。\n" +
                "<b>动态组(副页卡)</b>:勾选 dynamicMount 的组不进静态树,运行时服务器下发活动列表后由业务调 SetDynamicParent 动态接入。");

            Add("编辑操作",
                "■ 建节点:画布右键 → 创建 RedDot(带搜索);工具栏「新建引用节点」建组占位。\n" +
                "■ 配 key / 逻辑ID:节点上「红点key / 红点逻辑ID」按钮 → 分组模糊搜索;搜索窗顶部「<b>＋ 新建…</b>」可内联创建并自动选中。\n" +
                "■ Key设置 / 逻辑ID设置窗口:平铺列表直接改名/描述/删除(被引用条目删除会弹窗拦截);每行左侧灰字 = 该条目被哪些组使用;顶部「按组筛选」下拉(全部/未分组/主图/各具体组),「搜索」框按 key字符串/条目ID/描述 过滤列表。\n" +
                "■ 连线:父节点右侧「子节点」口 → 子节点左侧口;选中边按 Delete 删除。\n" +
                "■ 组操作:树面板行右键(建子组/改名/删组/主副迁移)、画布右键「创建组」、节点右键「移入组 / 归组为新组」。");

            Add("校验(导出前应清零)",
                "面包屑右侧「<b>校验: 结构N keyM 项问题</b>」双轴计数:\n" +
                "■ 结构轴 V1~V9:组根存在/组内单树/跨组边/引用节点出边·单拷贝·悬空/引用环/动态组静态引用等。\n" +
                "■ key 轴 K1~K6:未设置 key(K1)、悬空 keyId(K2)、未设置逻辑ID(K3,导出 externalId=0 加载必炸)、悬空逻辑ID(K4)、字典 key 字符串重复或为空(K5)、同 key 配到多节点(K6)。");

            Add("导出与产物",
                "工具栏「导出红点配置」→ 选目标 JSON。导出前有两道门禁:\n" +
                "① 存在 key 轴问题时弹确认(导了运行时也会加载失败);\n" +
                "② 顶点数比目标文件<b>缩水</b>时弹确认(防「图没画完就导出」覆盖线上配置——历史事故形态:202 顶点被覆盖为 10)。\n" +
                "产物:运行时 JSON(YooAsset 地址 RedDotGraphConfig)+ <b>RedDotKeys.cs</b>(热更域 GameLogic 内,含每个 key 的中文描述注释)。\n" +
                "<color=#E0A0A0>RedDotKeys.cs 是生成物禁止手改;重生成 = 再次导出,或菜单 Tools/红点系统/生成Key常量表(选导出JSON)。</color>");

            Add("业务接入(热更域)",
                "■ 注册数据源:<color=#8FD48F>RedDotGraphModule.Instance.RegisterChecker(RedDotKeys.XXX, (out status, out num) => {...})</color> —— 常量拼错=编译错;聚合节点常量注释标「不可注册」,注册会被拒绝并报错。\n" +
                "■ 报脏:<color=#8FD48F>DataChange(RedDotKeys.XXX[, id])</color>;\n" +
                "■ 绑 UI:<color=#8FD48F>AddUI(key, 回调) / RemoveUI</color>(配置未就绪期自动缓冲,时序无须关心);\n" +
                "■ 解锁门控:<color=#8FD48F>SetNodeEnabled(key, true/false)</color>(未解锁系统红点整体隔离);\n" +
                "■ 列表角标汇总:<color=#8FD48F>SummarizeCells(key, cellIds 提供器, 掩码)</color>。");

            Add("注意事项(常见坑)",
                "1. 叶子节点逻辑ID配 -1:没有数据源也没有子节点 → <b>永不亮</b>。除非业务走 SummarizeCells/直写运行时信息,否则应配真实逻辑ID。\n" +
                "2. 父掩码漏勾状态:子能产「红数字」而父没勾 → 子贡献被过滤,父永不亮。\n" +
                "3. 同 key 配两个节点 / 字典重名字符串:导出重复顶点,运行时<b>整份加载失败</b>(K5/K6 会报)。\n" +
                "4. 改 key 字符串后必须<b>重新导出</b>,并与热更 dll 同步发布(否则加载期对账 Warning:版本错配)。\n" +
                "5. 过渡期真实资产尚有域未重建:导出认准目标文件,「顶点数缩水」弹窗务必看清数字再确认。\n" +
                "6. <color=#E0A0A0>RedDotSmokeTest.reddotgraph 是 U9 交互验证专用冒烟示例,不是真实配置。</color>\n" +
                "7. 验证器菜单(U1~U9 / P0-2)默认隐藏,回归需先开 REDDOT_DEV 宏;资产<b>有意</b>变更后跑「RedDot/U7 写导出基准」同步 golden 字节基准。");
            return sections;
        }
    }
}
#endif
