using System.Reflection;

namespace MorningCat.I18n;

/// <summary>
/// MorningCat 国际化组件
/// </summary>
public class I18nManager
{
    /// <summary>全局静态实例</summary>
    public static I18nManager Instance { get; private set; } = new();

    private Dictionary<string, string> _translations = new();
    private Dictionary<string, string> _embeddedTranslations = new();
    private string _currentLang = "zh";
    private string _langDirectory = "";

    /// <summary>语言切换事件</summary>
    public event Action<string>? LanguageChanged;

    /// <summary>当前语言</summary>
    public string CurrentLang => _currentLang;

    /// <summary>语言目录路径</summary>
    public string LangDirectory => _langDirectory;

    /// <summary>可用语言列表</summary>
    public List<string> AvailableLanguages { get; private set; } = new();

    /// <summary>所有翻译键值对（当前语言）</summary>
    public IReadOnlyDictionary<string, string> Translations => _translations;

    /// <summary>初始化失败时的错误信息</summary>
    public string? InitError { get; private set; }

    /// <summary>
    /// 默认初始化（使用 en 语言，仅加载内嵌翻译，不创建 lang 目录）
    /// 用于在配置加载前提供基本翻译支持
    /// </summary>
    public void InitializeDefault()
    {
        _currentLang = "en";
        _embeddedTranslations = LoadEmbeddedTranslations("zh");
        _translations = LoadEmbeddedTranslations("en");
        Instance = this;
    }

    /// <summary>
    /// 初始化国际化组件
    /// </summary>
    /// <param name="lang">目标语言（如 zh, en, zh-cn）</param>
    /// <param name="baseDirectory">运行基础目录，lang 文件夹将创建在此目录下</param>
    /// <returns>是否初始化成功</returns>
    public bool Initialize(string lang, string baseDirectory)
    {
        _currentLang = lang;
        _langDirectory = Path.Combine(baseDirectory, "lang");
        Instance = this;

        try
        {
            // 1. 创建 lang 目录
            if (!Directory.Exists(_langDirectory))
            {
                Directory.CreateDirectory(_langDirectory);
            }

            // 2. 解压内嵌的 zh.yml 和 en.yml
            ExtractEmbeddedLang("zh");
            ExtractEmbeddedLang("en");

            // 3. 加载内嵌翻译（用于完整性对比）
            _embeddedTranslations = LoadEmbeddedTranslations("zh");

            // 4. 扫描可用语言
            ScanAvailableLanguages();

            // 5. 检查目标语言是否存在
            var langFile = Path.Combine(_langDirectory, $"{lang}.yml");
            if (!File.Exists(langFile))
            {
                // zh 和 en 是内嵌的，一定存在；第三方语言包不存在则报错
                if (lang != "zh" && lang != "en")
                {
                    InitError = $"Language pack not found: {lang}.yml";
                    return false;
                }
            }

            // 6. 加载目标语言
            _translations = LoadLangFile(langFile);

            // 7. 对内嵌语言包检查完整性
            if (lang == "zh" || lang == "en")
            {
                CheckAndRepairCompleteness(lang);
            }

            return true;
        }
        catch (Exception ex)
        {
            InitError = $"I18n initialization failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 静态快捷翻译方法
    /// </summary>
    public static string S(string key, params object[] args) => Instance.T(key, args);

    /// <summary>
    /// 获取翻译文本
    /// </summary>
    /// <param name="key">翻译键</param>
    /// <param name="args">格式化参数</param>
    /// <returns>翻译后的文本，如果键不存在则返回键名</returns>
    public string T(string key, params object[] args)
    {
        if (_translations.TryGetValue(key, out var value))
        {
            if (args.Length > 0)
            {
                try
                {
                    return string.Format(value, args);
                }
                catch
                {
                    return value;
                }
            }
            return value;
        }

        // 回退到键名
        return key;
    }

    /// <summary>
    /// 获取所有翻译键值对（供 API 返回给前端）
    /// </summary>
    public Dictionary<string, string> GetAllTranslations()
    {
        return new Dictionary<string, string>(_translations);
    }

    /// <summary>
    /// 切换语言（运行时）
    /// </summary>
    /// <param name="lang">目标语言</param>
    /// <returns>是否切换成功</returns>
    public bool SwitchLanguage(string lang)
    {
        var langFile = Path.Combine(_langDirectory, $"{lang}.yml");
        if (!File.Exists(langFile))
        {
            return false;
        }

        var translations = LoadLangFile(langFile);
        _currentLang = lang;
        _translations = translations;
        LanguageChanged?.Invoke(lang);
        return true;
    }

    /// <summary>
    /// 获取指定语言的翻译（不切换当前语言）
    /// </summary>
    public Dictionary<string, string>? GetTranslationsForLang(string lang)
    {
        var langFile = Path.Combine(_langDirectory, $"{lang}.yml");
        if (!File.Exists(langFile))
            return null;

        return LoadLangFile(langFile);
    }

    private void ExtractEmbeddedLang(string lang)
    {
        var targetPath = Path.Combine(_langDirectory, $"{lang}.yml");
        var resourceName = $"MorningCat.I18n.Lang.{lang}.yml";

        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return;

        if (!File.Exists(targetPath))
        {
            // 文件不存在，直接解压
            using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);
        }
        else
        {
            // 文件已存在，检查完整性
            var embeddedContent = ReadStreamContent(stream);
            var embeddedKeys = ParseYamlKeys(embeddedContent);

            var existingContent = File.ReadAllText(targetPath);
            var existingKeys = ParseYamlKeys(existingContent);

            // 如果已有文件缺少键，替换
            bool incomplete = false;
            foreach (var key in embeddedKeys)
            {
                if (!existingKeys.Contains(key))
                {
                    incomplete = true;
                    break;
                }
            }

            if (incomplete)
            {
                File.WriteAllText(targetPath, embeddedContent);
            }
        }
    }

    private Dictionary<string, string> LoadEmbeddedTranslations(string lang)
    {
        var resourceName = $"MorningCat.I18n.Lang.{lang}.yml";
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return new Dictionary<string, string>();

        var content = ReadStreamContent(stream);
        return ParseYaml(content);
    }

    private Dictionary<string, string> LoadLangFile(string filePath)
    {
        if (!File.Exists(filePath)) return new Dictionary<string, string>();

        try
        {
            var content = File.ReadAllText(filePath);
            return ParseYaml(content);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void CheckAndRepairCompleteness(string lang)
    {
        var langFile = Path.Combine(_langDirectory, $"{lang}.yml");
        if (!File.Exists(langFile)) return;

        var existingContent = File.ReadAllText(langFile);
        var existingKeys = ParseYamlKeys(existingContent);

        bool incomplete = false;
        foreach (var key in _embeddedTranslations.Keys)
        {
            if (!existingKeys.Contains(key))
            {
                incomplete = true;
                break;
            }
        }

        if (incomplete)
        {
            // 重新解压覆盖
            var resourceName = $"MorningCat.I18n.Lang.{lang}.yml";
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                var content = ReadStreamContent(stream);
                File.WriteAllText(langFile, content);
                _translations = ParseYaml(content);
            }
        }
    }

    private void ScanAvailableLanguages()
    {
        AvailableLanguages.Clear();
        if (!Directory.Exists(_langDirectory)) return;

        foreach (var file in Directory.GetFiles(_langDirectory, "*.yml"))
        {
            var langName = Path.GetFileNameWithoutExtension(file);
            AvailableLanguages.Add(langName);
        }
    }

    /// <summary>
    /// 简单的 YAML 解析（扁平键值对，格式: key: value）
    /// 忽略注释和空行，不处理嵌套结构
    /// </summary>
    private static Dictionary<string, string> ParseYaml(string content)
    {
        var result = new Dictionary<string, string>();
        var lines = content.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            // 移除引号包裹
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            // 处理转义序列
            value = value.Replace("\\n", "\n")
                         .Replace("\\t", "\t")
                         .Replace("\\\\", "\\");

            if (!string.IsNullOrEmpty(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// 解析 YAML 中的所有键名
    /// </summary>
    private static HashSet<string> ParseYamlKeys(string content)
    {
        var result = new HashSet<string>();
        var lines = content.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = line[..colonIndex].Trim();
            if (!string.IsNullOrEmpty(key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    private static string ReadStreamContent(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
