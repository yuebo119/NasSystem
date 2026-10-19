// =============================================================================
// NasSystem.Application - Configuration/ConfigDescriptors.cs
// 配置项注册表（单一事实源）
// =============================================================================

namespace NasSystem.Application.Configuration;

/// <summary>
/// 全部可 web 配置项的注册表。新增配置的唯一入口：
/// 这里加一条 descriptor，消费方用 key 读，配置页自动出现该字段。
/// 键名规范：{模块}.{参数}，全小写中划线（如 fileversion.max-count）
/// </summary>
public static class ConfigDescriptors
{
    /// <summary>键名常量。消费方照这些取，不写字面量（防拼写漂移）</summary>
    public static class Keys
    {
        public const string VersionMaxCount = "fileversion.max-count";
        public const string VersionMaxAgeDays = "fileversion.max-age-days";
        public const string OcrAutoRecognize = "ocr.auto-recognize";
        public const string WebDavEnabled = "webdav.enabled";
        public const string WebDavAllowHttp = "webdav.allow-http";
        public const string WebDavAuthMaxFailures = "webdav.auth-max-failures";
        public const string WebDavAuthWindowMinutes = "webdav.auth-window-minutes";
        public const string SnapshotEnabled = "snapshot.enabled";
        public const string SnapshotSchedule = "snapshot.schedule";
        public const string SnapshotKeepCount = "snapshot.keep-count";
        public const string SemanticEnabled = "semantic.enabled";
        public const string SemanticAutoIndex = "semantic.auto-index";
    }

    public static IReadOnlyList<ConfigDescriptor> All { get; } =
    [
        // ─── 文件版本（ROADMAP Phase 1）───
        new(Keys.VersionMaxCount, "version", "config.versionMaxCount",
            ConfigValueType.Int, "10", Min: 1, Max: 100,
            DescriptionKey: "config.versionMaxCountDesc"),
        new(Keys.VersionMaxAgeDays, "version", "config.versionMaxAgeDays",
            ConfigValueType.Int, "30", Min: 1, Max: 365,
            DescriptionKey: "config.versionMaxAgeDaysDesc"),

        // ─── OCR（ROADMAP Phase 2）───
        new(Keys.OcrAutoRecognize, "ocr", "config.ocrAutoRecognize",
            ConfigValueType.Bool, "false",
            DescriptionKey: "config.ocrAutoRecognizeDesc"),

        // ─── WebDAV 协议层（ROADMAP Phase 0）───
        new(Keys.WebDavEnabled, "webdav", "config.webdavEnabled",
            ConfigValueType.Bool, "true",
            DescriptionKey: "config.webdavEnabledDesc"),
        new(Keys.WebDavAllowHttp, "webdav", "config.webdavAllowHttp",
            ConfigValueType.Bool, "false",
            DescriptionKey: "config.webdavAllowHttpDesc"),
        // 认证失败锁定：同源 IP 在窗口期内失败达阈值即锁。原为 Api 层
        // 硬编码（10 次 / 5 分钟），随挂载服务独立页开放为可配置项，
        // WebDavAuthGuard 按 key 读（改完即生效，重启不恢复默认）
        new(Keys.WebDavAuthMaxFailures, "webdav", "config.webdavAuthMaxFailures",
            ConfigValueType.Int, "10", Min: 1, Max: 100,
            DescriptionKey: "config.webdavAuthMaxFailuresDesc"),
        new(Keys.WebDavAuthWindowMinutes, "webdav", "config.webdavAuthWindowMinutes",
            ConfigValueType.Int, "5", Min: 1, Max: 60,
            DescriptionKey: "config.webdavAuthWindowMinutesDesc"),

        // ─── 清单快照（ROADMAP Phase 2）───
        // schedule 是五段 cron（分 时 日 月 周），默认每日 03:00。
        // String 类型的长度界复用 Min/Max（cron 五段最短 9 字符如 "0 0 * * *"，
        // 最长含空格与星号约 40 字符）
        new(Keys.SnapshotEnabled, "snapshot", "config.snapshotEnabled",
            ConfigValueType.Bool, "false",
            DescriptionKey: "config.snapshotEnabledDesc"),
        new(Keys.SnapshotSchedule, "snapshot", "config.snapshotSchedule",
            ConfigValueType.String, "0 3 * * *", Min: 9, Max: 64,
            DescriptionKey: "config.snapshotScheduleDesc"),
        new(Keys.SnapshotKeepCount, "snapshot", "config.snapshotKeepCount",
            ConfigValueType.Int, "10", Min: 1, Max: 100,
            DescriptionKey: "config.snapshotKeepCountDesc"),

        // ─── 语义搜图（ROADMAP Phase 2，探针结论见 ADR-0003）───
        // 总开关：关闭时后台建索引停跑，搜索返回"未启用"。
        // auto-index：开启时后台自动扫缺向量图片逐张编码（单张 58ms，
        // 10 万张外推约 97 分钟）；关闭则只在手动触发时建
        new(Keys.SemanticEnabled, "semantic", "config.semanticEnabled",
            ConfigValueType.Bool, "false",
            DescriptionKey: "config.semanticEnabledDesc"),
        new(Keys.SemanticAutoIndex, "semantic", "config.semanticAutoIndex",
            ConfigValueType.Bool, "true",
            DescriptionKey: "config.semanticAutoIndexDesc")

        // 缩略图参数不在此表：ThumbnailService 是单例 + IOptions 构造期读取，
        // 动态化要么改生命周期要么每请求读库，与"低频调整的基础设施参数"
        // 的定位不匹配，继续留在 appsettings（Thumbnails 节）
    ];

    /// <summary>分组标题的 i18n 键（顺序即配置页的卡片顺序）</summary>
    public static IReadOnlyList<(string Group, string TitleKey)> GroupTitles { get; } =
    [
        ("version", "config.groupVersion"),
        ("ocr", "config.groupOcr"),
        ("webdav", "config.groupWebdav"),
        ("snapshot", "config.groupSnapshot"),
        ("semantic", "config.groupSemantic")
    ];

    /// <summary>按键取 descriptor。未知键返回 null（调用方按"不可配置"处理）</summary>
    public static ConfigDescriptor? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>取整型 descriptor 的默认值</summary>
    public static int IntDefault(string key) =>
        int.TryParse(Find(key)?.DefaultValue, out var value) ? value : 0;

    /// <summary>取布尔 descriptor 的默认值</summary>
    public static bool BoolDefault(string key) =>
        bool.TryParse(Find(key)?.DefaultValue, out var value) && value;

    /// <summary>取字符串 descriptor 的默认值（无匹配回落空串）</summary>
    public static string StringDefault(string key) =>
        Find(key)?.DefaultValue ?? string.Empty;
}
