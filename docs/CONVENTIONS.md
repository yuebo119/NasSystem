# NAS System 编码规范（v1.0）

> 本文件是 NAS 项目编码规范的**唯一真源**。`.ai/` 质量系统（gate/review/refine/test 四系统）的
> 全部门禁规则、评审依据、口径数字以本文件为准。
> 修改本文件必须同步 `.ai/scripts/gate-check.sh` 的对应检查项与 `.ai/gate/prompt.md` 的 G 表（三方一致）。
>
> 章节约定：每章含「约束」+「现状基线」。现状基线记录适配时点（2026-08-24）的存量债务数，
> 门禁对基线类检查采用**只减不增**模式：新增违规即 FAIL，存量债务登记 action-items 逐步清零。

---

## §1 分层架构

```
NasSystem.Domain          ← 领域层：实体/值对象/领域服务接口/仓储接口。零外部依赖
NasSystem.Application     ← 应用层：用例编排/DTO/验证器。仅依赖 Domain + Shared
NasSystem.Infrastructure  ← 基础设施层：PalORM(SQLite)/文件系统/JWT/缩略图实现
NasSystem.Api             ← 表现层：Minimal API 端点/SignalR Hub/中间件
NasSystem.Shared          ← 共享内核：Result/ApiResponse/PagedResult（零依赖）
```

### 约束

| # | 规则 |
|---|------|
| 1.1 | Domain 层 csproj 零 ProjectReference、零 PackageReference |
| 1.2 | Application 层仅允许 ProjectReference Domain + Shared；禁止引用 Infrastructure/Api |
| 1.3 | Domain + Application 源码零基础设施关键字：`DbContext` / `SqlConnection` / `SqliteConnection` / `DbCommand` |
| 1.4 | Application + Domain 源码零表现层关键字：`HttpClient` / `IHttpClientFactory` / `HttpContext` / `Hub` |
| 1.5 | 文件系统访问收口 `IFileSystemService` 抽象；Application 层裸 IO 调用（`File.X`/`Directory.X`/`new FileStream\|StreamReader\|StreamWriter\|ZipArchive\|DriveInfo\|FileInfo\|DirectoryInfo`）属存量债务，只减不增 |
| 1.6 | 具体异常类型必须 `sealed`；公共接口必须 `I` 前缀 |

### 现状基线（2026-08-24；2026-10-22 审计收口后重定标）

- Application 层裸 IO 直调 **0 处**（白名单口径：`File.Exists|Create|Delete|Copy|Move|...` 等真实 System.IO 成员名，剔除 `"File.Xxx"` 错误码字符串误报）。2026-10-22 审计 F-027 收口归零：FileVersionStore 从静态类改为可注入服务，版本/分享/扫描/磁盘探测域全部经 IFileSystemService，接口补齐 CreateHardLink/CopyFile/ReplaceFile/EnumerateFiles/EnumerateDirectories/GetAvailableFreeSpace/IsVolumeReady。2026-10-22 第 2 轮 F-084 把 `new FileInfo/new DirectoryInfo` 纳入门禁口径（此前 G16 模式漏掉这两型，FileService.Sync 两处裸 IO 未被计入），两处改经 `IFileSystemService.GetDirectoryInfo/GetFileInfo`，基线保持 0
- 异常 sealed 违规 0；接口 I 前缀违规 0 ✓

## §2 异步与并发

| # | 规则 |
|---|------|
| 2.1 | 禁止 `async void`（事件处理器除外，需注释说明） |
| 2.2 | 禁止同步阻塞异步：`.Result` / `.Wait()` / `GetAwaiter().GetResult()`（`IsCompletedSuccessfully` 快路径豁免） |
| 2.3 | 禁止 `TransactionScope`（用 `IUnitOfWork.BeginTransactionAsync` 替代） |
| 2.4 | 大文件 IO 必须流式：下载走 `Results.File(stream, ...)`；ZIP 打包写 `FileStream` 不进 `MemoryStream` |
| 2.5 | 目录扫描用异步枚举并设深度上限；循环体接入 `CancellationToken` |

## §3 数据库（SQLite）

| # | 规则 |
|---|------|
| 3.1 | 启动时启用 WAL + busy_timeout：`PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;`（多用户并发写 SQLITE_BUSY 防线）。换 PalORM 后由 provider 在连接级自动调优 8 项 PRAGMA（含 busy_timeout=5000、synchronous=NORMAL、journal_mode=WAL），无需手写拦截器 |
| 3.2 | 业务唯一性由数据库唯一索引兜底：`FileItems.Path`、`FileShares.ShareCode`、`Users.Username`、`(FileVersions.FileItemId, VersionNumber)`——应用层查重不能替代唯一索引（TOCTOU） |
| 3.3 | 统计聚合在 SQL 端完成（`GROUP BY` / `SUM`），禁止全表加载内存统计 |
| 3.4 | 列表查询数据库端分页，禁止 Take(N) 后内存二次过滤再分页 |
| 3.5 | 删除语义 = 默认软删除（`IsDeleted=true` 子树标记），`Permanent=true` 才物理删除；回收站读软删记录 |

### 现状基线（2026-08-25 更新）

- ✅ WAL/busy_timeout/synchronous 启动配置已落地；FileItems.Path 唯一部分索引 + ShareCode/Username/版本号唯一索引已兜底（旧库经幂等 DDL 迁移）
- ✅ 应用层数据完整性冲突转译为业务错误已落地（PalormUnitOfWork 按主码 19 + 扩展码 787/1299/275 转译为 UniqueConstraintException，覆盖唯一/外键/NOT NULL/CHECK 四类）

## §4 安全

| # | 规则 |
|---|------|
| 4.1 | JWT SecretKey 只能来自环境变量/用户机密，`appsettings*.json` 中必须是占位符 `<SET-VIA-ENV>` 形态；代码内不得存在默认密钥字符串 |
| 4.2 | 生产 CORS 禁止 `AllowAnyOrigin`；白名单来自配置 |
| 4.3 | 密码哈希 PBKDF2 ≥ 100k 迭代 + 独立盐；分享码/令牌用 `RandomNumberGenerator`，禁止 `new Random()` |
| 4.4 | 分享访问令牌必须带签名（HMAC），Base64 明文拼接视为无效 |
| 4.5 | 网络端点四件套：认证端点限流、上传大小限制（`MaxRequestBodySize`）、错误路径测试、结构化日志（不含密钥/PII） |
| 4.6 | 路径防穿越：虚拟路径 → 物理路径转换必须解析 `..` 并校验落在挂载根内（复用 `Domain.ValueObjects.FilePath`） |

### 现状基线（2026-08-25 更新）

- ✅ SecretKey 占位符化 + 注册期 fail-fast；分享令牌 HMAC-SHA256 签名；分享码/前端密码加密随机数
- ✅ Kestrel 上传 500MB 限制 + auth 端点限流（仅生产严格）；生产 CORS 白名单策略（SetIsOriginAllowed 拒绝制，来源清单待接入配置）

### 上传链路的体积上限（2026-10-17 流式化后）

上传只受一个上限约束：Kestrel 的 `MaxRequestBodySize`（`UploadLimit.MaxRequestBodySize`，
500MB）。`UploadLimit` 已注入 DI 供系统设置端点回读，是单一事实源。

2026-10-17 起 multipart 解析从 `ReadFormAsync` 改 `MultipartReader` 流式
（FileEndpoints.UploadFile）：表单层的 `FormOptions.MultipartBodyLengthLimit`
与 `MemoryBufferThreshold` 不再参与上传链路（无 ReadFormAsync 调用方，
配置已删）。改动前这两个阈值与 Kestrel 各管一段，且超过内存阈值的文件 part
会落临时盘，上传变成网络 → 临时文件 → 目标文件三趟磁盘 IO。流式化后为单趟，
容量控制改为余量预检加边写边检，中断留下的 `.uploading-` 半成品由扫描同步
跳过。注意 multipart 字段顺序：`path`/`overwrite` 必须先于 `file`（流单向消费）。

### 压缩层归属（2026-10-22 补实测数据，结论：两层等价，无需纠结）

微软文档记录 nginx 代理时会剥掉请求的 `Accept-Encoding`，若属实则生产路径上
ASP.NET Core 的响应压缩中间件空转，真正压缩的是 nginx 的 gzip（level 6，无 brotli）。
本机无 nginx 与 docker，该行为**仍未实测**。

但 2026-10-22 优化轮实测出的事实让这个未决项的**实际影响可以忽略**：
应用侧把 Brotli/Gzip 都设为 `CompressionLevel.Optimal` 后，两者输出几乎相同——
最大产物 element-plus（636,602 字节）br 201,296 / gzip 204,466，差 1.5%。
也就是说无论最终是哪一层在压，客户端拿到的体积基本一致；真正要保证的是
**等级不是默认的 Fastest**（Fastest 下 br 只有 40%，Optimal 是 31%）。

部署后仍建议跑一条命令确认压缩层归属：

```
curl -sD- -o /dev/null -H 'Accept-Encoding: br' https://<host>/api/v1/system/health | grep -i content-encoding
```

有 `br` 说明压缩在应用侧（等级配置生效）；只有 `gzip` 或无该头，说明被 nginx
接管，此时要调的是 nginx 的 `gzip_comp_level`。

**缓存正确性**已本地实测收口：.NET 11 起压缩响应**始终**带
`Vary: Accept-Encoding`（未压缩时也带，见 breaking-changes/11），而 nginx
自 1.7.7 起在 proxy_cache 中处理 Vary，因此现有 `proxy_cache_key`
（不含编码）不会把 br 变体发给不支持 br 的客户端。代价只是不同编码变体
各自占一份缓存，属可接受的轻微低效。

### 静态资源缓存策略（2026-10-22）

Vite 产物是内容哈希文件名，`/assets/` 可以无限期强缓存；`index.html`、`sw.js`
与 SPA 回退必须每次回源校验，否则发版后浏览器拿旧 HTML 引用已删除的哈希资源。

应用侧（`ServiceCollectionExtensions` 的 `UseStaticFiles` 与 SPA fallback）与
边缘（`nginx.conf` 两个 location）发同一条策略：前者保证直连 Kestrel 的部署，
后者覆盖启用生产 profile 的场景。nginx 侧用 `proxy_hide_header Cache-Control`
隐藏上游那份，否则 `add_header` 会追加出两条同值的 Cache-Control。
锁定测试 `StaticCacheHeaderTests` 三例。

## §5 时间处理

| # | 规则 |
|---|------|
| 5.1 | 库代码时间获取注入 `IDateTimeProvider`（已定义于 Domain.Services）；硬编码 `DateTime.UtcNow` 属存量债务只减不增 |
| 5.2 | 时间戳副本名等用户可见输出统一 UtcNow，禁止 `DateTime.Now` 混用 |

### 现状基线（2026-08-24）

- 硬编码 UtcNow 共 **16 处**（src 下非注释口径）。2026-10-22 审计 F-028 收口（两批）：IDateTimeProvider 重新引入（ITM-N33 曾当死代码清掉），第一批覆盖版本/分享/转码/WebDAV 守卫/JWT/仓储/存储域，第二批覆盖 Api 层端点、Infrastructure 四服务与 Application 的 FileService/UserService。余下 16 处 = Domain 实体 13 处（Entity 基类 MarkAsModified、IDomainEvent.OccurredAt、FileVersion/SystemSetting/FileItem/FileShare/User/BackupDevice/FileOcrText 的工厂与变更方法）+ Shared ApiResponse 信封 2 处 + SystemDateTimeProvider 自身 1 处（合法实现）。实体层收口需要把时间戳穿进每个工厂与变更方法，属领域语义改动，单独批次处理；生产语义本就正确（实体在请求内创建/变更时读当时时间），代价只是实体级测试无法冻结时间

## §6 测试规范（xUnit）

| # | 规则 |
|---|------|
| 6.1 | 框架 xUnit + Moq + FluentAssertions；属性 `[Fact]` / `[Theory]` + `[InlineData]` |
| 6.2 | 测试方法命名 `Method_Scenario_ExpectedResult` 三段式（集成测试可 `Provider_Operation_Behavior`）；存量 6 个老式命名（BasicIntegrationTests.cs）只减不增 |
| 6.3 | 新增/修改的生产代码必须有对应测试（正向 + 边界/异常各 ≥1） |
| 6.4 | 文件系统测试用临时目录 + try/finally 清理；SQLite 用每测试类独立的临时文件库（PalORM 无内存 provider），`using var`/Dispose 自动释放 |
| 6.5 | 断言验证行为不验证实现；优先属性断言而非裸 NotNull |
| 6.6 | 测试间无状态依赖，不依赖执行顺序 |

## §7 前端（Vue 3 + Element Plus）

| # | 规则 |
|------|------|
| 7.1 | Element Plus 按需引入（unplugin resolver）；`main.ts` 禁止 `import ElementPlus from 'element-plus'` 全量导入与全量 CSS |
| 7.2 | 图标按需 import 为组件对象，禁止 `import * as ElementPlusIconsVue` 全局注册 |
| 7.3 | API 封装返回类型与运行时一致（拦截器解包后签名必须是 `ApiResponse<T>`），禁止新增 `as any` |
| 7.4 | 批量操作一次请求传数组，禁止前端循环逐项调用支持数组的 API |
| 7.5 | 实时状态推送走 SignalR（`/hubs/nas`），轮询间隔 ≥30s 且后台标签页暂停 |
| 7.6 | 认证用户 JWT 走 Header 或一次性签名 URL，禁止拼入 query string；公开分享访问令牌（匿名端点+媒体标签场景）经 query 传递属合法形态（NPD-6） |

### 现状基线（2026-08-25 更新）

- ✅ Element Plus 按需引入落地（全量 import/CSS/图标全局注册已移除）；认证 JWT 三处下载/预览改 Header+blob
- ⏳ dayjs/file-saver/@vueuse 死依赖已卸载；虚拟滚动与 SignalR 接活仍待前端专项

## §8 构建与工程化

| # | 规则 |
|---|------|
| 8.1 | 目标框架 net11.0 / C# LangVersion 14（`Directory.Build.props` 单一来源） |
| 8.2 | `TreatWarningsAsErrors` 目标 true；当前 false 为存量债务，新警告不得引入 |
| 8.3 | 提交信息格式 `类型：描述`；公共 API/行为/计数变更同次提交同步文档（三方一致） |
| 8.4 | 工作树脏检查：任务切换前 `git status --short` 清洁或显式声明 |

## §9 口径数字单一来源

| 数字 | 真源 |
|------|------|
| NAS-G 编号 | `.ai/scripts/gate-check.sh`（现行 G1..G22） |
| 基线数字（UtcNow 16 / 裸 IO 0 / 测试命名 6） | 本文件 §5.1 / §1.5 / §6.2，改动须同步脚本基线常量 |
| 缺陷登记 ITM-N 编号 | `.ai/lessons.md` 第二章 |
| 误判模式 NPD-N 编号 | `.ai/review/known-false-positives.md` |
