# 多语言适配方案设计参考

> 小T的设计笔记 — 运行时全面本地化，一次适配所有语言

## 一、现状分析

TEngine 已有 LocalizationModule（基于 I2 Localization 集成）。需要补齐：
1. ✅ 已有：多语言资源管理、基础翻译功能
2. ❌ 缺失：运行时语言切换 → UI 自动刷新
3. ❌ 缺失：代码动态创建文本的本地化绑定
4. ❌ 缺失：字体按语言切换
5. ❌ 缺失：RTL 语言（阿拉伯语/希伯来语）支持
6. ❌ 缺失：参数化文本（"你获得了 {0} 个金币"）

## 二、架构设计

```
ILocalizationModule
├── 语言管理
│   ├── SetLanguageAsync(lang) → 切换语言
│   ├── GetCurrentLanguage()
│   └── event OnLanguageChanged → UI 订阅刷新
│
├── 文本获取
│   ├── GetText(key) → string
│   ├── GetTextFormat(key, params) → string     ← 参数化
│   ├── TryGetText(key, out text) → bool        ← 安全获取
│   └── SmartGetText(key, fallback) → string    ← 带默认值
│
├── UI 自动绑定
│   ├── LocalizeText component（挂载到 Text/TMP_Text 上）
│   ├── LocalizeImage component（根据语言切换图片）
│   └── LocalizeFont component（根据语言切换字体）
│
├── 字体管理
│   ├── 中/日/韩 → NotoSansCJK
│   ├── 英文/欧洲 → NotoSans
│   ├── 阿拉伯语 → NotoNaskhArabic
│   └── 运行时按语言切换字体资源（走 YooAsset 热更）
│
└── 数据源（可插拔）
    ├── I2 Localization 兼容
    ├── JSON 自定义格式
    └── CSV 导入/导出
```

## 三、核心 API 设计

```csharp
public interface ILocalizationModule
{
    // === 语言管理 ===
    Language CurrentLanguage { get; }
    UniTask SetLanguageAsync(Language lang, bool refreshUI = true);
    event Action<Language> OnLanguageChanged;
    List<Language> AvailableLanguages { get; }

    // === 文本获取 ===
    string GetText(string key);
    string GetTextFormat(string key, params object[] args); // "欢迎, {0}!"
    bool TryGetText(string key, out string text);
    string SmartGetText(string key, string fallback = "");

    // === 资源 ===
    void RegisterLanguageAssets(Language lang, string assetPath);
    void SetLanguageFont(Language lang, TMP_FontAsset font);
    
    // === 数据管理 ===
    UniTask LoadLanguageDataAsync(Language lang);
    UniTask ReloadAllAsync();
}

// 枚举（扩展 TEngine 已有 Language 枚举）
public enum Language
{
    Unspecified,
    ChineseSimplified,
    ChineseTraditional,
    English,
    Japanese,
    Korean,
    French,
    German,
    Spanish,
    Portuguese,
    Russian,
    Arabic,
    // ...
}
```

## 四、UI 组件绑定

```csharp
// 静态绑定（挂在 UGUI Text / TMP_Text 上）
// → 在 Inspector 中选择 key
// → 切换语言时自动刷新
// → 支持 OnLanguageChanged 事件

// 代码动态绑定
var text = GameModule.Localization.GetText("ui_login_welcome");
var formatted = GameModule.Localization.GetTextFormat("ui_gold_reward", goldAmount);

// 带参数的 UI 组件
// 在扩展 LocalizeText 组件中：
// Key: "ui_gold_reward" → 值: "获得了 {0} 个金币"
// 代码赋值: localizeText.SetArgs(goldAmount);
```

## 五、运行时切换流程

```
用户选择 English
  → SetLanguageAsync(English)
    → 1. 加载 English 语言数据（Json/CSV）
    → 2. 如有字体差异，加载英文字体（YooAsset）
    → 3. 触发 OnLanguageChanged 事件
    → 4. 所有打开的 UIWindow 接到事件 → 自动刷新文本
    → 5. 所有 LocalizeText 组件自动刷新
    → 6. 操作结束，UI 全部切换为英文 ✓
```

## 六、字体管理策略

```csharp
// 全局字体配置
[CreateAssetMenu(fileName = "FontConfig", menuName = "TEngine/FontConfig")]
public class FontConfig : ScriptableObject
{
    public FontMapping[] FontMappings;
    
    [Serializable]
    public class FontMapping
    {
        public Language Language;
        public TMP_FontAsset Font;
        public string AssetPath; // YooAsset 资源路径
    }
}

// 运行时按需加载
await GameModule.Localization.SetLanguageFontAsync(Language.Arabic, "fonts/noto_naskh_arabic");
```