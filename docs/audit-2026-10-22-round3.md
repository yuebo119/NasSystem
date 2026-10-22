# NAS System 第 3 轮审计报告（健壮性与故障路径）

- 日期：2026-10-22
- 主轴：健壮性与故障路径
- 交叉轴：架构与依赖
- 模式：审计并修复
- 预算：无硬上限，按任务书停止条件收口

## 1. 已读清单

- `docs/audit-2026-10-22-exhaustive.md` 与第 1/2 轮报告：F-001 到 F-085 全部账本，含 F-072/F-073 部署残余与 F-084 跳过目录残留。本轮不重报。
- `.ai/lessons.md`、`docs/CONVENTIONS.md`、`docs/decisions/`：重点核对 §2 异步与取消、§1 分层边界、ITM-N21 错误码表设计。
- `git log --oneline -40` 与最近提交 diff：避让第 1/2 轮改动面（图片解码、快照分批、OCR upsert、扫描容错、门禁口径）。
- 外部进程与外部依赖清单：ffmpeg、PaddleOCR、CLIP、sqlite-vec、SkiaSharp。

## 2. 本轮结论

健壮性主轴确认 4 条新发现：外部进程取消后成孤儿、服务端异常被映射成 400、版本快照重试路径留孤儿、领域层解析存储 JSON（交叉轴）。4 条全部修复并配锁定测试；单测从 214 增加到 222，集成从 310 增加到 312，干净门禁 21 通过 0 失败。

## 3. 发现总表

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-086 | High | 健壮性 | FfmpegTranscodeService.cs:469 | 客户端取消只释放句柄，ffmpeg 成孤儿继续转码 | 已修 5a6c846 |
| F-087 | Medium | 错误语义 | GlobalExceptionHandler.cs:56 | 所有 InvalidOperationException 一律映射 400，掩盖服务端故障 | 已修 b719b23 |
| F-088 | Low | 健壮性 | FileVersionService.cs:82,104 | 快照先建后登记，重试与失败路径会留无记录引用的孤儿 | 已修 2f4382b |
| F-089 | Low | 架构 | FileOcrText.cs（原 ParseMany） | 领域实体自行解析持久化 JSON 列，存储格式漏进 Domain | 已修 2ab40a0 |

严重级分布：High 1、Medium 1、Low 2。确认 4 条，推测 0 条。

## 4. 发现完整论证

### F-086 外部进程取消后成孤儿（High，已修）

- 证据：修复前 `RunProcessAsync`（`FfmpegTranscodeService.cs`）用 `using var process` 加 `ReadToEndAsync(ct)` / `WaitForExitAsync(ct)`，取消只抛 `OperationCanceledException`；`using` 释放句柄不终止子进程。调用方 `GetOrCreateHlsAsync` 的 `finally` 只释放转码闸。当前 `:469` 重写，`:504-516` 三个 catch 分支都先 `TryKill`，`:52` 新增 15 秒探测超时，`:144/:201/:424/:430` 探测与抽帧带超时降级。
- 机理：`Process` 对象释放只关闭父进程侧句柄，子进程与操作系统层面的执行无关。客户端关闭播放页触发 `RequestAborted`，转码请求抛取消，ffmpeg 继续编码到自然结束；转码闸已释放，后续请求可再起一个 ffmpeg，孤儿数量随取消次数累积。
- 触发条件：登录用户打开视频触发按需转码，在转码完成前关闭页面或切走。正常操作即可触发，不需要恶意。
- 最坏影响：每个孤儿持续占用 CPU 与磁盘写入；多个孤儿并行可打满 NAS 资源，拖慢或阻塞其他请求。属资源耗尽型可用性问题，按"现实条件下可触发的资源泄漏"定 High。
- 现有测试为何没拦住：`FfmpegTranscodeTests` 只覆盖成功转码、缓存命中、清理与降级，没有取消路径；取消后进程存活与否从未被观测。
- 自我反驳：是否能靠"管道关闭"让子进程自己退？第一版用 ffmpeg 文件增长做判据，去掉 Kill 后测试仍通过，原因是日志管道关闭可能让 ffmpeg 提前退出，但那不是契约，取决于 ffmpeg 的日志写入时机与错误处理。用一个只写文件、不依赖管道的子进程复测，去掉 Kill 后按 PID 判据明确转红，说明必须显式杀。另一处风险是只读 stderr 不读 stdout：`stdout` 被重定向但从不消费，子进程写满管道会永久阻塞，本轮一并改为双管道同时吸收。
- 最小修复：取消/超时/异常三条路径先杀进程树并 `WaitForExit(5000)` 回收；探测与抽帧加 15 秒超时，超时按"探测失败"降级单档 1080p；正式转码不设内部超时（长视频耗时不确定），依赖客户端取消加杀进程。
- 验证方式：`ProcessCancellationTests` 用只写文件的 PowerShell/POSIX 子进程，按启动回调拿到的 PID 断言取消后 5 秒内已退出。S3 移除取消分支的 `TryKill` 后该用例转红（1 failed），恢复后绿。
- 回归风险：`RunProcessAsync` 增加可选 `timeout` 与 `onStarted` 参数，签名向后兼容；杀进程树对 ffmpeg 无子进程也不产生副作用。探测超时 15 秒对正常文件远高于实际所需（毫秒级）。
- 静默控制问句：如果不杀进程，测试的 PID 判据会失败；如果不吸 stdout，畸形输入下子进程可能阻塞在被写满的管道上，表现为请求永不返回。

### F-087 服务端异常被映射成客户端错误（Medium，已修）

- 证据：`GlobalExceptionHandler` 原有一个 `InvalidOperationException => 400` 分支；仓库内该类异常共 7 处，逐条核对均为服务端故障或编程错误：缩略图缩放失败（`ThumbnailService.cs:337,384`）、ffmpeg 启动失败（`FfmpegTranscodeService.cs:484`）、模型下载结果为空（`ClipModelDownloader.cs:111`）、Zip 流内断言（`ZipDirectoryResult.cs:61`）、启动期认证配置（`AuthenticationExtensions.cs:45`）、`Result.Value` 误用（`Result.cs:98`）。当前 `:56` 保留说明并删除该分支，落默认 500。
- 机理：异常类型不是"客户端还是服务端"的判据。把 `InvalidOperationException` 一律当 400，会让缩放失败、子进程启动失败这类真实故障返回"请求参数无效"，调用方据此不再重试、日志也不再指向服务端。
- 触发条件：任何走到这些分支的请求，例如对一张能解码但缩放阶段失败的图片请求缩略图，或 ffmpeg 二进制缺失时请求 HLS。
- 最坏影响：错误语义失真，用户与前端把服务端故障当输入问题，排障方向被误导；不丢数据。
- 现有测试为何没拦住：`ErrorStatusContractTests` 只覆盖参数绑定失败，未覆盖异常类型映射；该类映射从未被单测锁定。
- 自我反驳：删掉分支会不会让本该 400 的场景变 500？仓库里客户端状态类错误一律经 `Result` 返回 `Error.Validation`/`Error.Conflict`，没有依赖 `InvalidOperationException` 表达客户端语义的调用点，7 处逐条核对完毕。新增 `GlobalExceptionHandlerTests` 只锁 500 一侧，客户端语义由既有 Result 测试覆盖。
- 最小修复：删除该分支，保留说明性注释，默认分支交回 500。
- 验证方式：`GlobalExceptionHandlerTests` 构造抛 `InvalidOperationException` 的下游并断言 500。S3 临时加回 400 映射后转红，恢复后绿。
- 回归风险：未来若真的需要"当前状态下无法执行"，应由服务层返回 `Error.Validation`，不要重新依赖异常类型。
- 静默控制问句：如果映射仍是 400，用例断言 500 会失败；这是本条唯一的观测面。

### F-088 版本快照重试与失败路径留孤儿（Low，已修）

- 证据：修复前 `FileVersionService.cs` 先 `_versionStore.CreateSnapshot(...)` 再 `orphanSnapshot = versionPath`；`CreateSnapshot` 内部硬链接失败会降级复制，复制中途失败抛异常时 `orphanSnapshot` 仍为 null，外层 catch 不清理。重试分支（版本号唯一索引冲突）换名重建后直接覆盖 `orphanSnapshot`，第一次尝试的快照永久无人引用。当前 `:82` 先登记再建，`:104` 重试前 `TryDeleteOrphanSnapshot`，`:133` 抽出清理助手。
- 机理：保留策略只按库记录删物理文件（`.versions` 被扫描排除，扫不回来），没有记录引用的快照永远清不掉。
- 触发条件：并发覆盖同一文件导致版本号唯一索引冲突后重试成功；或快照复制中途失败（磁盘满、源文件被占用）。
- 最坏影响：`.versions` 目录里的旧内容副本长期占盘；单次量是一份旧文件，反复发生会累积。不丢数据（是多余副本，不是缺内容）。
- 现有测试为何没拦住：`FileVersionAutoTests` 覆盖正常产版本与保留策略，未覆盖重试与复制失败路径。
- 自我反驳：重试只发生两次（`MaxAttempts = 2`），命中概率低，量级也小，所以定 Low 而不是 Medium；但修复成本是三行加一个助手，且同一函数在并发覆盖场景本就会走重试，留着不合理。
- 最小修复：先登记孤儿路径再建快照；重试前删除上一次无记录引用的快照；清理逻辑收成一个 `TryDeleteOrphanSnapshot`。
- 验证方式：`FileVersionServiceOrphanTests` 用 Mock 让第一次 `SaveChangesAsync` 抛 `UniqueConstraintException`，断言第一次快照被删、第二次保留。S3 移除重试前删除后转红，恢复后绿。
- 回归风险：删除的是无记录引用的文件；第二次快照在断言中必须保留，用例已锁。
- 静默控制问句：如果不删第一次快照，`deletedFiles` 不含 `createdSnapshots[0]`，用例转红。

### F-089 领域层解析存储格式 JSON（Low，已修）

- 证据：修复前 `FileOcrText.Restore` 接收 `regionsJson` 并调用 `OcrTextRegion.ParseMany`，后者用 `System.Text.Json` 反序列化。当前 `RowMapper.cs:172,179` 新增 `ParseOcrRegions`，`FileOcrText.Restore` 改收 `IReadOnlyList<OcrTextRegion>?`；`grep -rn "System.Text.Json" src/NasSystem.Domain` 已无命中。
- 机理：Domain 应只表达业务语义（一行文字与归一化坐标），JSON 列是持久化实现细节。领域实体解析存储格式会让换存储形态（列改表、格式版本升级）直接改动领域层，也让 Domain 承担与业务无关的容错规则。
- 触发条件：架构层面持续存在；不是运行时故障。
- 最坏影响：层边界腐化，未来存储格式变更的改动面扩散到 Domain；影响可维护性，不影响运行。
- 现有测试为何没拦住：解析逻辑原本在 Domain，没有针对"存储格式解析归属"的检查；正常运行路径一直是对的。
- 自我反驳：这算不算为重构而重构？`System.Text.Json` 是 BCL，不违反 §1.1 的零包依赖，运行也没有问题；但 §1 的意图是 Domain 不依赖基础设施形态，这里确实是边界穿透，代价是 3 个文件的小改动，收益是边界归位。判定 Low、按架构轴登记。
- 最小修复：把解析搬到 `RowMapper.ParseOcrRegions`（Infrastructure），Domain 只接收强类型列表。
- 验证方式：`RowMapperOcrRegionTests` 7 例锁定有效 JSON、null/空、损坏、坐标数不为 8 的降级；S3 把解析改为恒返回空后有效用例转红，恢复后绿。领域无 JSON 由 grep 复核。
- 回归风险：解析位置变了但规则逐字保留（过滤空文本与坐标数不为 8），既有 OCR 往返测试与端点测试全过。
- 静默控制问句：如果解析被停掉，区域列表恒为空，`ParseOcrRegions_ValidJson_ReturnsRegions` 转红；端点层则表现为叠加坐标丢失。

## 5. 扫描口径记录

| 类 | 命令与范围 | 命中 |
|---|---|---:|
| 外部进程 | `grep -rn "Process.Start\|RunProcessAsync\|WaitForExit" src tests`（排除 obj/bin），逐行读 `FfmpegTranscodeService` | 1 个生产进程点（F-086） |
| 异常边界 | `grep -rn "throw new InvalidOperationException" src` | 7 处逐条分类（F-087） |
| 取消传播 | 读 `Transcode/Export/Zip/OCR/Thumbnail` 端点的 `RequestAborted` 传递 | 1 处断链（F-086） |
| 事务补偿 | 读 `PalormUnitOfWork.SaveChangesAsync`、上传临时文件清理、`FileVersionStore.CreateSnapshot`、`ExportResult`/`ZipDirectoryResult` 流式 | 1 处（F-088） |
| DI 生命周期 | 逐一核对 20+ 注册的构造函数依赖 | 0 处俘虏依赖 |
| 分层穿透 | G1..G4 门禁全过；`grep -rn "System.Text.Json" src/NasSystem.Domain`；`grep -rn "IFileItemRepository" src/NasSystem.Api/Endpoints` | 1 处（F-089）、5 处 Api 直用仓储（观察） |

## 6. 修复批次与验证

| 批次 | 提交 | 内容 | 验证 |
|---|---|---|---|
| 外部进程 | `5a6c846` | F-086 | ProcessCancellationTests 1 例；S3 转红 |
| 异常映射 | `b719b23` | F-087 | GlobalExceptionHandlerTests 1 例；S3 转红 |
| 版本孤儿 | `2f4382b` | F-088 | FileVersionServiceOrphanTests 1 例；S3 转红 |
| 领域边界 | `2ab40a0` | F-089 | RowMapperOcrRegionTests 7 例；S3 转红 |

最终验证：构建 exit 0 / 0 错误；单测 222/0；集成 312/0；前端 183/26、oxlint 0、vue-tsc exit 0、生产构建 exit 0；干净门禁 21 通过 / 0 失败 / 0 跳过。

本轮新增测试 10 个：单元 8 例（区域解析 7、版本孤儿 1），集成 2 例（进程取消、异常映射）。

过程备注：调试异常映射用例时一次构建失败（`HostingEnvironment` 类型不存在），测试命令仍跑了上一次的二进制并给出误导性结果，重建后才正确。产物新鲜度陷阱在 AGENTS.md 已有登记，本轮再次实证：编译失败时测试结果不可采信。

## 7. 未决项与观察项

- F-084 残留（第 2 轮登记，待你裁决）：被跳过的不可读目录其库内活跃记录会在删除集判定里被当作"物理消失"删除。当前取舍"宁可删记录也不停扫描"，另一方向是记录被跳过目录前缀并从删除集排除。本轮未动。
- Api 层直用仓储（观察，架构轴）：`ExportEndpoints`、`SemanticEndpoints`×2、`TranscodeEndpoints`、`ThumbnailEndpoints` 五处直接注入 `IFileItemRepository` 并各自做属主判定（现为 3 处 `OwnerId != userId` 写法）。不是当前漏洞，但属主规则分散，后续新增端点可能漏检；建议下一轮做 Application 层的文件访问解析器（按虚拟路径取属主校验后的物理路径），端点只调它。
- `SnapshotService.CreateAsync` 读树不在写入事务内（观察）：快照与并发写入的一致性没有验收口径，第 2 轮已列为候选，未定义"快照一致性"前不动手。
- `TranscodeEndpoints` 错误形态（观察）：`Results.NotFound(new { message })` 与 `Results.Forbid()` 不走 `ApiResponse` 信封，与其他端点的错误契约不一致；前端只按状态码处理时无影响，建议随 Api 层收口一起改。
- `FileVersionService` 重试上限 2 次（观察）：同一文件三路以上并发覆盖时第三路放弃版本，只留日志。概率低，保留。
- 第 1/2 轮的观察项仍在：`DirectoryVersion` 静态字典不淘汰、`WebDavAuthGuard` 失败表按 IP 增长、`ThumbnailService._inFlightGenerations` 工厂重入。

DI 生命周期对账结论（本轮正面结论）：`IFileSystemService`、`DiskSpace`、`FileVersionStore`、`IThumbnailService`、`ITranscodeService`、`ClipEngineHolder`、`PaddleOcrEngineHolder`、`SystemControlService`、`WebDavAuthGuard`、`ShareEnumerationGuard` 的构造函数依赖全部是 Singleton 或纯参数对象；仓储、`PalormSessionHolder`、`PalormUnitOfWork`、各服务均为 Scoped。未发现 Singleton 捕获 Scoped 的俘虏依赖。

## 8. 下一轮建议

1. 主轴选「架构与依赖」：先裁决 F-084 残留语义，再做 Api 层属主判定收口（文件访问解析器）与 `TranscodeEndpoints` 错误信封统一。
2. 交叉轴选「安全隔离」：属主判定收口本身就是隔离面重构，值得配一次跨用户回归（`CrossUserIsolationTests` 扩用例）。
3. 若 F-084 按"保留库记录"落地，需要同时改删除集判定与门禁口径，属契约变更，先出小样再全量。

## 9. 复现命令

```bash
dotnet build NasSystem.sln
./tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe
./tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe
cd web && npm run test && npm run lint && npm run type-check && npm run build && cd ..
bash .ai/scripts/gate-check.sh
grep -rn "System.Text.Json" src/NasSystem.Domain --include=*.cs
grep -rn "IFileItemRepository" src/NasSystem.Api/Endpoints --include=*.cs
```

## 10. 收尾自检

1. 账本读了吗，有没有重报：读了 F-001 到 F-085 全部账本；F-072/F-073/F-084 按既有状态处理，未重报。
2. 基线是本轮实测吗：是。起始 214/310/183、门禁 21/0/0；结束 222/312/183、门禁 21/0/0，均为本轮复跑。
3. 每条发现十项齐全吗：F-086 到 F-089 均含证据、机理、触发、影响、测试缺口、自我反驳、最小修复、验证、回归、静默控制。
4. 有没有凑数条目：无；F-089 明确标注为架构债、运行无影响，按交叉轴登记。
5. 每个修复是否指认锁定测试，S3 做了吗：四条都有 S3 转红后恢复的记录。
6. 全量测试与门禁本轮复跑且贴计数行了吗：见第 6 节。
7. 有没有重提已否决方向：没有。
8. 有没有改范围外代码：只改外部进程封装、异常映射、版本补偿与领域边界；未改前端业务逻辑、未改 API 契约。
9. 有没有未经验证的性能或内存声明：本轮无性能声明；进程取消用 PID 判据，不做时延断言。
10. 账本编号连续吗：F-086 到 F-089 连续，F-001 到 F-085 未改。

## 11. 进度

| 栏位 | 状态 |
|---|---|
| 实现进度 | F-086 到 F-089 全部已修（4/4） |
| 验证完成度 | 4 条均有测试级 S3 转红记录；全量 222/312/183 与门禁 21/0/0 本轮复跑 |