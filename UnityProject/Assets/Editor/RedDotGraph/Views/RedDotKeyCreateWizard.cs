#if UNITY_EDITOR
using RedDot.Editor.Data;
using TEngine;
using UnityEditor;
using UnityEngine;

namespace RedDot.Editor.Views
{
    /// <summary>
    /// 新建红点key/逻辑ID向导(K批5 2026-09-17,与 key 侧对齐):从节点 key/逻辑ID 搜索窗口内联进入,
    /// 免跳出画布开字典窗。校验非空+名称唯一(ContainsKeyStr)+禁':'。
    /// func 侧:名称仅为展示别名(零运行时/导出消费),id 自增 ++maxId 即可——代理键定案(K批3)下
    /// 无需人工挑数字,业务只认 RedDotKeys.XXX 常量携带的 externalId。
    /// </summary>
    public class RedDotKeyCreateWizard : ScriptableWizard
    {
        private RedDotGraphData _data;
        private RedDotEnumDict _dict;
        private RedDotKeySearchWindowProvider.OnSelectDelegate _onSelect;
        private bool _isFunc;

        public string key = "";
        public string desc = "";

        /// <summary>DisplayWizard 返回实例后再注入依赖(OnWizardCreate 点击时才读取,注入时机安全)。</summary>
        public static void Open(RedDotGraphData data, RedDotEnumDict dict, RedDotKeySearchWindowProvider.OnSelectDelegate onSelect, bool isFunc)
        {
            var wizard = DisplayWizard<RedDotKeyCreateWizard>(isFunc ? "新建逻辑ID" : "新建红点key", "创建");
            wizard._data = data;
            wizard._dict = dict;
            wizard._onSelect = onSelect;
            wizard._isFunc = isFunc;
        }

        private void OnWizardCreate()
        {
            key = (key ?? "").Trim();
            desc = (desc ?? "").Trim();
            if (string.IsNullOrEmpty(key))
            {
                errorString = _isFunc ? "名称不能为空" : "key 不能为空";
                return;
            }
            if (key.Contains(":"))
            {
                errorString = "不能包含 ':'(该字符是 'id:key' 展示解析的分隔符)";
                return;
            }
            if (_dict.ContainsKeyStr(key))
            {
                errorString = _isFunc
                    ? "名称已存在:" + key + "(请在搜索列表中直接选择)"
                    : "key 已存在:" + key + "(请在搜索列表中直接选择)";
                return;
            }
            var id = _dict.Add();
            _dict.ChangeKey(id, key);
            _dict.ChangeDesc(id, desc);
            Log.Info("新建{0}:{1}(id:{2})", _isFunc ? "逻辑ID" : "红点key", key, id);
            if (_onSelect != null)
            {
                _onSelect(id);
            }
        }
    }
}
#endif
