# NAS System 第 1 轮审计报告（内存与分配）

- 日期：2026-10-22
- 主轴：内存与分配
- 交叉轴：时延与热点
- 模式：审计并修复
- 预算：无硬上限，按任务书停止条件收口

## 1. 已读清单

- `docs/audit-2026-10-22-exhaustive.md`：§3 总表、§4 完整论证、§6 未决项、§8 修复执行记录、§9 与 §10 优化轮。F-001 到 F-074 共 74 条登记，71 条确认项全部修复；F-072 剩运维配置（`KnownProxies`），F-073 剩部署侧阈值测量，均不按代码缺陷重报。
- `.ai/lessons.md`、`docs/CONVENTIONS.md`、`docs/decisions/`（PalORM 迁移、语义搜图探针、ffmpeg ABR）。
- `git log --oneline -40` 与最近提交 diff：重点核对 `9b5f753`、`6fe30ea`、`7154bcb`、`c33471c`，确认本轮不重提优化轮已否决方向。
- `docs/perf/baseline.md`（net10 + EF Core 迁移基线）、`docs/perf/net11-palorm-*` 最近结果。

## 2. 本轮结论

内存主轴上确认 6 条新发现：图片解码链路 3 条（OCR、缩略图、媒体元数据）、快照条目 1 条、目录浏览 1 条、压测夹具 1 条。前 3 条与快照、夹具共 5 条已修复并配锁定测试；目录浏览无分页需要 API 与前端契约决策，登记为待裁决。基线从 199/306/183 变为 209/307/183，门禁全绿。

## 3. 发现总表

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-075 | High | 内存/并发 | OcrService.cs（修复前 167/190） | OCR 解码在 2 路识别闸之外，并发请求各自先解全图 | 已修 bdea25f |
| F-076 | Medium | 内存 | ThumbnailService.cs（修复前 319） | 缩略图按原图全解码，并发闸只按 CPU 核数 | 已修 bdea25f |
| F-077 | Medium | 内存 | SnapshotService.cs（修复前 47-48） | 快照创建同时物化三份全树条目 | 已修 4152915（残留一份投影） |
| F-078 | Medium | 内存/时延 | FileService.Browse.cs:49 | 目录浏览无分页无上限，响应随子项数线性增长 | 待裁决 |
| F-079 | Low | 工具链 | ApiBench.cs / Seeder.cs | 压测夹具 browse 组静默缺失，热点端点无数据 | 已修 00e06e7 |
| F-080 | Medium | 内存/时延 | MediaMetadataExtractor.cs:55 | 媒体元数据为取宽高全图解码，一页 60 项成比例浪费 | 已修 bdea25f |

严重级分布：High 1、Medium 4、Low 1。确认 6 条，推测 0 条。

## 4. 发现完整论证

### F-075 OCR 并发解码在识别闸之外（High，已修）

- 证据：
  - 修复前 `src/NasSystem.Infrastructure/Ocr/OcrService.cs:167` 先 `SKBitmap.Decode(item.PhysicalPath)`，`:190` 才 `engineHolder.EnterRunAsync`。`PaddleOcrEngineHolder.cs:30` 的 `_runGate` 是 2 路。
  - OCR 端点只挂 `RequireAuthorization`，没有限流：`src/NasSystem.Api/Endpoints/FileEndpoints.cs:142-146`。
  - 一次性探针 `tmp/probe/ImageProbe`：6000x4000 JPEG（24MP）全解码进程工作集 `+94.0MB`（BGRA 96MB），三次一致；同一图按 4MP 预算解码 `+15.1MB`，按 16MP 预算 `+72.5MB`。
- 机理：SkiaSharp 像素是原生内存，不受 GC 管理。解码发生在信号量之前，N 个并发请求会各自先分配一张全图再排队，闸只约束推理不约束解码。
- 触发条件：任一登录用户并发调用 `POST /api/v1/files/ocr/{path}`，路径指向大图。普通媒体页不会并发 OCR，但 API 层没有阻止。
- 最坏影响：20 个并发 24MP 请求约 1.88GB 原生内存，100 个约 9.4GB，可导致进程 OOM 重启；属于普通用户可触发的可用性问题。
- 现有测试为何没拦住：`OcrEndpointTests` 用 24x24 到数百像素的生成图，单请求执行；没有大图也没有并发用例。
- 自我反驳：是否存在上游限制？Kestrel 500MB 是上传体限制，OCR 请求只传路径；auth 限流只挂在登录端点；OCR 服务是 Scoped，但引擎闸是单例，解码在闸外，代码顺序已核对。native 内存无法靠 GC 回收，探针读的是进程工作集，不是托管堆估值。
- 最小修复：新增 `ImageDecoder`，按像素预算请求编解码器缩放；OCR 以 16MP 为预算、64MP 为不可缩放格式的兜底上限，直接解成 BGRA 省掉第二份转换位图；`EnterRunAsync` 移到解码之前。
- 验证方式：`ImageDecoderTests` 6 例 + `PaddleOcrEngineHolderTests` 1 例；S3 把 `ImageDecoder` 的目标尺寸临时改回原图，`Decode_LargeJpeg_ScalesWithinPixelBudget` 转红（1 failed），恢复后绿。
- 回归风险：解码与推理改为串行，并发 OCR 吞吐略降；单请求延迟不变（同一段代码，只是多了等待闸的时机）。如果 OCR 吞吐成为瓶颈，可把解码改到独立的有界队列，但不能放回闸外。
- 静默控制问句：如果解码是无声 no-op，OCR 文本会为空，现有 OCR 端点测试会失败；如果闸的位置是 no-op，内存峰值回到 F-075 原状，探针可复现。

### F-076 缩略图按原图全解码（Medium，已修）

- 证据：
  - 修复前 `src/NasSystem.Infrastructure/Files/ThumbnailService.cs:319` 的 `SKBitmap.Decode(filePath)` 解全图后再 `Resize`。
  - 并发闸是 `_generationGate = max(2, ProcessorCount / 2)`（`:44`）。本机 32 逻辑核即 16 路；缩略图端点只要求认证，没有限流（`ThumbnailEndpoints.cs:20-24`）。
  - 同一 24MP 探针：全解码 `+94.0MB`，按 4MP 预算 `+15.1MB`，约 6.2 倍差。
- 机理：缩略图目标最大 1024px，却先按原图分辨率分配像素，再缩放。媒体网格一次加载多张图，浏览器会并发取缩略图，16 路同时解大图就是 1.5GB 级峰值。
- 触发条件：打开含大量高像素照片的媒体库或文件页，或直接并发请求缩略图端点。
- 最坏影响：24MP 照片 16 路约 1.5GB；50MP 照片约 3.2GB。表现为内存压力、GC 抖动、页面加载变慢，严重时 OOM。
- 现有测试为何没拦住：`ThumbnailEndpointTests` 用 4x4 PNG 验证端点契约，没有大图与并发内存用例。
- 自我反驳：闸把并发压到 16，是否已经足够？16 是 CPU 预算，不是内存预算；单张 94MB 时 16 路仍然过大。是否只有恶意用户能触发？正常媒体库滚动就会并发取图，不需要恶意。
- 最小修复：缩略图解码预算取目标框最大边长的两倍（1024 档即 2048x2048，约 4.2MP），PNG 等不可缩放格式允许到 64MP 全解，超过则失败；视频抽帧的 `ScaleJpegBytes` 走同一预算。
- 验证方式：`ImageDecoderTests` 覆盖大图缩放、小图不放大、不可缩放格式上限；S3 同 F-075。集成 `ThumbnailEndpointTests` 全过。
- 回归风险：解码尺寸降到目标的两倍，缩略图可见清晰度不受影响（最终仍等比缩到目标框）；PNG 大图仍会全解，受 64MP 硬上限约束。
- 静默控制问句：如果预算被忽略，缩略图外观不变，只有内存曲线变化；`Decode_LargeJpeg_ScalesWithinPixelBudget` 专门锁定这个差异。

### F-077 快照创建三份全树物化（Medium，已修分批，残留一份投影）

- 证据：
  - 修复前 `src/NasSystem.Infrastructure/Services/SnapshotService.cs:47` 取 `GetSnapshotEntriesAsync` 全树列表，`:48` 再建等量 `List<SnapshotEntry>`，`SnapshotRepository.cs:106` 又映射成等量 `rows` 列表后才 `BulkInsertAsync`。
  - 定时快照固定以 `/` 为根（`SnapshotBackgroundService.cs:102`），覆盖全系统。
- 机理：三个 O(N) 列表同时驻留；百万级条目时每份都是百 MB 量级。后台服务 OOM 会拖垮整个进程。
- 触发条件：开启定时快照且文件树达到十万到百万级；不需要用户请求。
- 最坏影响：每日快照时进程内存峰值叠加，可能 OOM 重启；快照失败后保留策略也不执行。
- 现有测试为何没拦住：`SnapshotServiceTests` 只造 4 个条目，没有批次断言。
- 自我反驳：`GetSnapshotEntriesAsync` 已是 6 字段投影，仓储也按 500 行批量插入，是否已经够轻？投影只降低单条大小，不改变三份全量同时驻留；`AddEntriesAsync` 一次传入全部条目时，`rows` 仍是全量。
- 最小修复：每 5000 条调用一次 `AddEntriesAsync`，批次仍在同一个 `IUnitOfWork` 事务内；`SnapshotServiceBatchTests` 用 12000 条断言 5000/5000/2000 三次登记。
- 验证方式：S3 把 `EntryBatchSize` 改为 `int.MaxValue`，批次测试转红（1 failed），恢复后绿；集成 `SnapshotServiceTests` 全过。
- 回归风险：多条写入意图在同一事务提交，语义与单批一致；若后续批次失败，整体回滚，不会留下半份快照。残留：`subtree` 仍是全量投影，彻底消除需要 keyset 分页读取，单独批次处理。
- 静默控制问句：如果分批逻辑是 no-op，条目数仍然正确，只有批次数与峰值内存变化；批次测试锁定批次数。

### F-078 目录浏览无分页无上限（Medium，待裁决）

- 证据：
  - `src/NasSystem.Application/Files/FileService.Browse.cs:49` 调用 `GetChildrenSummariesAsync`，仓储 `FileItemRepository.cs:89` 的 SQL 没有 LIMIT 或游标；端点把全部子项映射成 DTO 后一次序列化。
  - WebDAV PROPFIND 走同一服务（`WebDavEndpoints.cs:288`），另外在 `:724` 用 `MemoryStream` 建 XML、`:755` 再 `ToArray()`，多一份完整响应缓冲。
  - 50k 行夹具（20 个顶层目录，folder000 有 2500 个子项）实测：browse P50 15.50ms、P95 36.08ms、P99 43.01ms（50 次成功请求，并发 1，报告 `docs/perf/net11-palorm-20261022-123318.md`）。
- 机理：查询、DTO 映射、JSON 序列化、WebDAV XML 生成都是 O(N)，没有上限。2500 子项已经 15.5ms，按线性放大的估算标 [推断]：25k 子项约 155ms，50k 约 310ms；DTO 与 JSON 的内存也随子项数增长。
- 触发条件：单个目录放入数万文件，网页端打开该目录，或 WebDAV 客户端列目录。
- 最坏影响：单次请求几十 MB 分配与数百毫秒延迟，反复打开造成 LOH 与 GC 压力；不丢数据，但会让大目录不可用。
- 现有测试为何没拦住：没有大目录列表测试；本轮之前基准也没有真正打到 browse 端点（见 F-079）。
- 自我反驳：2500 子项 15.5ms 仍在可接受范围，个人 NAS 的常见目录可能远小于此；因此定 Medium 而不是 High。问题只在万级单目录出现，不能按当前数据夸大成普遍故障。
- 最小修复（未实施，需契约决策）：browse API 增加 `limit` 与游标，前端按页加载；或保留全量 API，新增轻量索引端点给虚拟滚动。WebDAV 不适合分页，优先把 XML 改为流式写出，避免 `MemoryStream + ToArray` 双份缓冲。两种方案都改公共契约，按任务书停下等裁决。
- 验证方式（若批准）：集成测试造 10k 子项，断言首页大小与 `hasMore`；基准对比分页前后 P95 与进程内存。WebDAV 流式改造用大目录 PROPFIND 的峰值工作集对比。
- 回归风险：分页会改变网页端加载与缓存逻辑；WebDAV 客户端依赖单次 PROPFIND 返回全部子项，不能简单分页。推荐网页端分页、WebDAV 流式，两条路径分开处理。
- 静默控制问句：如果端点静默截断，用户会看不到文件；必须用显式 `hasMore` 而不是隐藏上限。

### F-079 压测夹具 browse 组静默缺失（Low，已修）

- 证据：
  - `tools/Benchmark/Bench/ApiBench.cs` 原 URL 为 `/api/v1/files/browse/%2Ffolder000`。真实实例 curl 返回 404，响应体写明 `文件或文件夹 '/%2Ffolder000' 不存在`；同一实例用 `browse/folder000` 返回 200。
  - `Seeder.cs` 的目录树原先用随机 `OwnerId`。admin 只拥有约 1/500 的文件；即使 URL 正确，admin 登录后也可能 404。
  - 修复前基准报告 `net11-palorm-20261022-115557.md` 与 `120355.md`：总错误 1658/1948，端点表里没有 browse 组。
- 机理：ASP.NET Core 默认不把路径里的 `%2F` 解码成斜杠，路由把它当普通字符，服务再补前导 `/`，得到 `/%2Ffolder000`。夹具归属随机使问题在不同 seed 下时有时无。基准只累计错误数、不校验端点是否出现，两个缺陷都被错误计数吞掉。
- 触发条件：运行 `tools/Benchmark api`。不影响生产。
- 最坏影响：基准静默少测最热的列目录端点，性能决策建立在缺一组的数据上。
- 现有测试为何没拦住：基准 harness 没有自检；报告模板也不校验端点集合。
- 自我反驳：`%2F` 是否本应被路由解码？真实 curl 已证伪；产品用 `folder000` 正常。归属随机是否只是本轮 seed 的偶然？`Seeder.cs` 明确用 `userIds[random.Next]`，结构上必然可能不归 admin。
- 最小修复：URL 改为 `browse/folder000`；目录树统一归 `userIds[0]`（admin）；API 基准在压测前预检 browse，非 200 直接中止。
- 验证方式：修复后 50k 基准端点表重新出现 browse（50 次成功，P50 15.50ms）；S3 对应“旧 URL + 无预检”，修复前两次报告已实证 browse 缺失。
- 回归风险：无生产影响；基准现在会因夹具漂移快速失败，这是期望行为。
- 静默控制问句：如果预检是 no-op，报告会再次静默少一组端点。

### F-080 媒体元数据为取宽高全图解码（Medium，已修）

- 证据：
  - 修复前 `src/NasSystem.Infrastructure/Media/MediaMetadataExtractor.cs:53` 用 `SKBitmap.Decode(filePath)`，只为拿 `Width/Height`。
  - 调用方 `MediaService.GetMetadataAsync` 一次最多接收 200 个路径（`MediaService.cs:71-82`），前端媒体页每页 60 项一次性请求（`web/src/views/Media.vue:337-343`）。
  - 探针：同一 24MP JPEG，全解码 `+94.0MB`；只读文件头 `SKCodec.Info` 为 `+2.3MB`（三次一致），约 41 倍差。
- 机理：图片尺寸在文件头里，`SKCodec.Info` 不需要像素缓冲；原实现为两个整数分配并解码整张图。单请求内顺序执行，峰值是一张图，但 60 到 200 次解码的 CPU 与分配总量很大；并发用户会叠加。
- 触发条件：打开媒体页触发批量元数据请求，或直接调用 `POST /api/v1/media/metadata`。
- 最坏影响：60 项页面每次约 60 次全图解码，24MP 图总量约 5.6GB 分配与数秒 CPU；多用户并发时内存与线程池压力放大。属于页面级性能与内存热点，不是崩溃级。
- 现有测试为何没拦住：`MediaMetadataExtractorTests` 只覆盖 MP4/MKV/MP3，没有图片用例；媒体端点测试用 4x4 图。
- 自我反驳：顺序解码的峰值只有一张图，是否只是 CPU 问题？单请求峰值确实不高，但分配总量与 GC 压力随项数线性增长；并发请求数不受限，峰值会叠加。`SKCodec.Info` 是否对所有支持格式可用？JPEG/PNG/WebP/BMP/GIF 都由 Skia 编解码器提供头信息，新增 JPEG 用例与既有格式列表一致。
- 最小修复：`ImageDecoder.ReadDimensions` 只读 `SKCodec.Info`；`MediaMetadataExtractor.ExtractImage` 改用它，移除全图解码。
- 验证方式：`ImageDecoderTests.ReadDimensions_LargeJpeg_ReturnsHeaderSize` 与 `ReadDimensions_MissingFile_ReturnsNull`；集成 `Extract_Jpeg_ReturnsDimensions`；S3 用探针 `full` 与 `info` 两种模式对比，移除头读因子后回到 `+94.0MB`。
- 回归风险：损坏文件若头可读但像素不可解，现在仍能返回尺寸；这对元数据是更合理的行为。动画 GIF 返回首帧尺寸，与原实现一致。
- 静默控制问句：如果 `ReadDimensions` 返回 0 或 null，媒体页尺寸徽章消失；集成用例锁定 640x480。

## 5. 扫描口径记录

| 类 | 命令 | 文件数 | 命中 |
|---|---|---:|---:|
| 全量读与内存流 | `grep -rnE 'ReadAllBytes\|ReadAllText\|ReadAllLines\|\.ToArray\(\)\|new MemoryStream' src --include=*.cs`（排除 obj/bin） | 185 | 23 |
| 缓存与静态字典 | `grep -rnE 'static .*(Dictionary\|ConcurrentDictionary\|List\|Queue\|HashSet\|Cache)\|IMemoryCache\|new ConcurrentDictionary' src` | 185 | 38 |
| 图片解码点 | `grep -rn 'SKBitmap.Decode' src --include=*.cs` | 185 | 修复前 4，修复后 1 |
| 数据流追踪 | 逐文件读 `OcrService`、`ThumbnailService`、`MediaMetadataExtractor`、`SnapshotService`、`SnapshotRepository`、`FileService.Browse/Sync`、`FileShareService`、`ExportResult`、`ZipDirectoryResult`、`WebDavEndpoints`、`MediaService` | 11 | 6 条发现 |
| 内存探针 | `tmp/probe/ImageProbe`，6000x4000 JPEG/PNG，独立进程，每模式 3 次 | 1 图 | 见第 7 节 |
| 真实实例 | benchmark 50k 夹具，concurrency 1，5s | API 8 端点 | browse 50 次成功 |

说明：`grep` 命中只作线索，逐条读代码或跑探针后才登记发现。ZIP 与导出链路（`ZipDirectoryResult`、`ExportResult`）已确认流式写响应体，本轮不重复登记。

## 6. 修复批次与验证

| 批次 | 提交 | 内容 | 验证 |
|---|---|---|---|
| 图片解码 | `bdea25f` | F-075/F-076/F-080：`ImageDecoder`、OCR 闸内解码、缩略图预算、元数据头读 | 单测 209/0；集成 307/0；S3 两处转红 |
| 快照分批 | `4152915` | F-077：5000 条一批，同一事务 | 批次测试 3 批；S3 转红 |
| 基准夹具 | `00e06e7` | F-079：URL、admin 归属、browse 预检、50k 报告 | 基准 browse 组恢复 |

本轮合计新增测试 11 个：单元 10 例（199 增至 209），集成 1 例（306 增至 307）。其中 `ImageDecoderTests` 8 例、`PaddleOcrEngineHolderTests` 1 例、`SnapshotServiceBatchTests` 1 例、`MediaMetadataExtractorTests` 1 例。

提交前最终验证：构建 exit 0 / 0 错误（`--no-incremental`）；单测 209/0；集成 307/0；前端 183/26、oxlint 0 警告、vue-tsc exit 0、生产构建 exit 0；门禁 20 通过 / 0 失败 / 1 跳过（`--allow-dirty` 跳过 G22）。

## 7. 实测数据

### 7.1 图片解码内存（独立进程，工作集口径）

| 模式 | 解码尺寸 | 工作集增量 | 说明 |
|---|---|---:|---|
| 全解码 | 6000x4000（24MP） | +94.0MB | BGRA 96MB，三次一致 |
| 预算 16MP | 5250x3500（18.4MP） | +72.5MB | JPEG 分级缩放量化到 1/2 附近 |
| 预算 4MP | 2250x1500（3.4MP） | +15.1MB | 缩略图路径，约为全解 1/6 |
| 只读文件头 | 6000x4000 | +2.3MB | 元数据路径，只建 codec，无像素缓冲 |
| PNG 预算 4MP | 6000x4000 | +93.9MB | 编解码器不支持缩放，按原尺寸解；实现用 64MP 兜底上限拒绝异常大图 |

命令：`tmp/probe/ImageProbe/ImageProbe.exe decode tmp/round1/big.jpg full|scaled 4000000|16000000`，`info tmp/round1/big.jpg`。绝对值仅本机有效，不跨机器比较。

### 7.2 目录浏览时延（50k 夹具，2500 子项）

- 报告：`docs/perf/net11-palorm-20261022-123318.md`
- 并发 1，时长 5s，browse 50 次成功：P50 15.50ms、P95 36.08ms、P99 43.01ms。
- 同一轮总错误 31，与公开分享端点 51 次尝试、20 次成功吻合（生产默认 `SharePerMinute=20` 的按 IP 窗口），429 被计入错误 [推断]。不是产品缺陷，后续工具轮可把 429 单独分类。
- 结论：2500 子项仍在可接受范围；F-078 的风险集中在万级单目录，不能把当前 15.5ms 当作故障证据。

## 8. 未决项与观察项

- F-078 待裁决：目录浏览分页或流式方案。推荐网页端加 `limit` 与游标、WebDAV 改流式 XML，两条路径分开实施。
- F-077 残留：`GetSnapshotEntriesAsync` 仍全量物化一份投影。彻底消除需要 keyset 分页读取，单独批次做，避免把事务语义改复杂。
- 观察项：`DirectoryVersion.Versions` 静态字典按目录路径只增不减。条目很小，且淘汰会让未知目录返回版本 0，可能与旧缓存 ETag 相等造成错误 304，因此当前不做淘汰。
- 观察项：`WebDavAuthGuard._failures` 只在客户端再次访问或成功时清理，无全局扫描；按不同来源 IP 增长，单条约百字节，万级 IP 约 1MB。当前不登记为缺陷。
- 观察项：`SnapshotService` 的 `subtree` 列表与 `MediaService.GetMetadataAsync` 的 200 路径上限都是已知的 O(N) 边界，前者待分页，后者由头读改造后单条成本已降。

## 9. 下一轮建议

1. 主轴选「并发时序」：本轮把解码移入闸内，值得复查其他单例闸与 Scoped 服务的交错路径（缩略图 in-flight、分享下载闸、存储刷新闸）。
2. 交叉轴选「健壮性与故障路径」：给 `ImageDecoder` 的 64MP 拒绝路径补端点级错误码与前端提示，确认超大图失败不会变成 500。
3. 若先做 F-078 的契约决策，再单独开「时延与热点」轮，用 10k/50k 子项基准验证分页收益。

## 10. 复现命令

```bash
dotnet build NasSystem.sln
./tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe
./tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe
cd web && npm run test && npm run lint && npm run type-check && npm run build && cd ..
bash .ai/scripts/gate-check.sh
tmp/probe/ImageProbe/bin/Debug/net11.0/ImageProbe.exe decode tmp/round1/big.jpg full
tmp/probe/ImageProbe/bin/Debug/net11.0/ImageProbe.exe decode tmp/round1/big.jpg scaled 4000000
tmp/probe/ImageProbe/bin/Debug/net11.0/ImageProbe.exe info tmp/round1/big.jpg
dotnet run --project tools/Benchmark -- seed --rows 50000 --assert
dotnet run --project tools/Benchmark -- api --rows 50000 --concurrency 1 --duration 5 --port 6100
```

`tmp/` 已忽略，探针与 50k 工作库不入库。

## 11. 收尾自检

1. 账本读了吗，有没有重报：读了 §3/§4/§6/§8/§9/§10；F-072/F-073 按部署侧残余处理，未重报；优化轮已否决方向未重提。
2. 基线是本轮实测吗：是。构建 0 错误；单测 209/0；集成 307/0；前端 183/26、lint 0、type-check 0、构建 0；门禁见第 6 节。
3. 每条发现十项齐全吗：F-075 到 F-080 均含证据、机理、触发、影响、测试缺口、自我反驳、最小修复、验证、回归、静默控制。推测 0 条。
4. 有没有凑数条目：F-078 是唯一未修条目，明确标待裁决；F-079 是工具链缺陷，已说明不影响生产但影响测量可信度。
5. 每个修复是否指认锁定测试，S3 做了吗：F-075/F-076 共用 `ImageDecoderTests` 与 OCR 闸测试，S3 移除缩放后 1 failed；F-077 用批次测试，S3 改批大小后 1 failed；F-080 用头读测试与探针 A/B；F-079 用预检与修复前报告对照。
6. 全量测试与门禁本轮复跑且贴计数行了吗：构建、单测、集成、前端、门禁均在本次轮次执行；门禁最终数字在第 6 节与本轮交付消息中给出。
7. 有没有重提已否决方向：没有。SQLite 往返、PalORM 会话池、Dashboard 请求合并、element-plus 继续拆、压缩等级均未重提。
8. 有没有改范围外代码：只改图片解码、快照分批、媒体元数据与压测夹具；未改前端业务代码、未改 API 契约。
9. 有没有未经验证的性能或内存声明：内存数字来自独立进程探针，三次一致；时延来自 benchmark 报告；PNG 不支持缩放是实测行为。OCR 吞吐影响标为推断，未给绝对值。
10. 账本编号连续吗：F-075 到 F-080 连续；原账本 F-001 到 F-074 未改。

## 12. 进度

| 栏位 | 状态 |
|---|---|
| 实现进度 | F-075/F-076/F-077/F-079/F-080 已修（5/6）；F-078 待裁决（1/6） |
| 验证完成度 | 代码修复全部有锁定测试；单测 209/209、集成 307/307、前端 183/183、门禁 0 失败；F-078 未修复故无验证 |

本轮 83% 的发现已闭环，剩余 1 条属于契约变更，按任务书不擅自改 API。
