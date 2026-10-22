# NAS System 第 2 轮审计报告（并发时序）

- 日期：2026-10-22
- 主轴：并发时序
- 交叉轴：健壮性与故障路径
- 模式：审计并修复
- 预算：无硬上限，按任务书停止条件收口

## 1. 已读清单

- `docs/audit-2026-10-22-exhaustive.md` §3/§4/§6/§8/§9/§10 与 `docs/audit-2026-10-22-round1.md`：F-001 到 F-074 已修，F-072/F-073 为部署残余，F-075 到 F-080 在第 1 轮闭环。本轮不重报。
- `.ai/lessons.md`、`docs/CONVENTIONS.md`、`docs/decisions/`：硬约定与已裁决设计，重点核对 §1.5 裸 IO 口径与 §2 异步规则。
- `git log --oneline -40` 与最近提交 diff：第 1 轮四个提交（`bdea25f`/`4152915`/`00e06e7`/`5e8de88`）的改动面已避开。
- `docs/perf/net11-palorm-20261022-123318.md`：第 1 轮 50k 夹具基准，本轮不改性能路径，未重复采集。

## 2. 本轮结论

并发时序主轴确认 5 条新发现：OCR 文本读后写竞态、可移除锁对象的释放竞态、扫描同步无互斥、扫描单目录异常终止整轮加门禁裸 IO 假绿、缩略图源文件错误落 500。5 条全部修复并配锁定测试或探针；单测从 209 增加到 214，集成从 307 增加到 310，干净门禁 21 通过 0 失败。

## 3. 发现总表

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-081 | Medium | 并发 | OcrService.cs:208 | 并发首次识别同一图：都读到无文本都走 INSERT，唯一索引让后者 500 | 已修 cf9a7ee |
| F-082 | Medium | 并发 | FileShareService/StorageService 闸门 | 释放与移除之间的窗口可让两个持有者同时进临界区 | 已修 59db1f9 |
| F-083 | Medium | 并发 | FileService.Sync.cs:33 | 扫描同步无互斥，并发扫描重复全池枚举并撞唯一索引 | 已修 1bdd247 |
| F-084 | Medium | 健壮性 | FileService.Sync.cs:276,380 | 单目录/单文件读取失败终止整轮；两处裸 IO 未被 G16 计入（假绿） | 已修 1bdd247 |
| F-085 | Low | 错误语义 | ThumbnailService.cs:169 | 源文件损坏/超限落 500 而非 400 | 已修 df9cef3 |

严重级分布：Medium 4、Low 1。确认 5 条，推测 0 条（两处竞态的概率未能黑盒复现，见各条的自我反驳）。

## 4. 发现完整论证

### F-081 OCR 文本写入读后写竞态（Medium，已修）

- 证据：`FileOcrTextRow.cs:17` 有 `Unique = true` 的 FileItemId 索引；修复前 `OcrService.cs` 在识别后执行 `GetByFileItemIdAsync` 再决定 `AddAsync` 或 `Update`，两步之间有一次数据库往返。当前 `OcrService.cs:208` 改为 `UpsertAsync`，仓储 `FileOcrTextRepository.cs:56` 用 `INSERT ... ON CONFLICT(FileItemId) DO UPDATE`。
- 机理：两个请求先后读到 null，都走 INSERT；后提交方撞唯一索引，PalORM 转译成业务冲突后由 OCR 的兜底 catch 报 `Ocr.Failed`，端点落 500。
- 触发条件：同一图片并发两次 POST `/files/ocr`。前端有按钮态时不易手工触发，批量识别与单条识别重叠、或 API 直接并发调用可达。
- 最坏影响：一次有效识别请求返回 500，同时浪费一次完整推理；不丢数据。
- 现有测试为何没拦住：`OcrEndpointTests` 是单请求；本轮新增的 8 路并发用例在修复前跑过，未命中窗口（见自我反驳）。
- 自我反驳：修复前用 8 路并发端点测试尝试复现，通过；窗口在"读 null 与 INSERT 提交"之间，实测两路推理耗时差足以让后者读到已存在行，黑盒未复现。判定依据是可证明的代码交错与唯一索引约束，不是观测到的失败；因此按"预防性修复"登记，级别定 Medium 而非 High。
- 最小修复：单条 ON CONFLICT 语句把判定交给数据库，冲突方变成覆盖；保留原 CreatedAt（与 `UpdateText` 语义一致）。
- 验证方式：`OcrUpsertConcurrencyTests` 两例（顺序第二次写入必须覆盖、双作用域并发都成功且只留一行）；`OcrEndpointTests` 8 路并发一例。S3 把 `DO UPDATE` 换成 `DO NOTHING`，顺序覆盖用例转红（1 failed），恢复后绿。
- 回归风险：`AddAsync`/`Update` 仍保留在接口上供其他路径使用，未删除；OCR 写入路径不再做存在性查询，少一次往返。
- 静默控制问句：如果 upsert 退化成 DO NOTHING，文本会停留在首次结果，顺序覆盖用例专门锁定这个差异。

### F-082 可移除锁对象的释放竞态（Medium，已修）

- 证据：修复前 `FileShareService.cs` 与 `StorageService.cs` 都是 `ConcurrentDictionary<Guid, SemaphoreSlim>` 加"Release 后 CurrentCount==1 即移除"。当前两处改用 `KeyedGate`（`KeyedGate.cs:34`，固定 64 分片，无移除动作）。
- 机理：请求 B 已经 GetOrAdd 取到实例但尚未进入（CurrentCount 仍为 1）时，A 的移除判定成立并把闸门摘掉；请求 C 再取会创建新实例，B 与 C 同时对同一 key 进入临界区。下载计数用它保护"限流判定 + 自增 + 落库"，重叠会让 MaxDownloads 被并发打穿。
- 触发条件：同一分享或同一存储池的并发请求恰好落在释放与移除之间。窗口是相邻两条语句，正常负荷下概率极低。
- 最坏影响：分享下载上限被绕过（业务不变量破坏）；存储池侧只是多一次 SUM，无正确性后果。
- 现有测试为何没拦住：`ShareHardeningRegressionTests` 的并发下载用例在正常窗口下通过，命中不了亚微秒级交错。
- 自我反驳：生产窗口未放大，没有直接观测到线上破坏；因此证据是模拟探针，不是现场复现。探针 `tmp/probe/GateRaceProbe` 把两侧窗口各放宽 1ms 后，旧模式 5000 次出现 14 次重叠，分片实现 0 次。修复的真实收益是消除原理性窗口并让内存不再随 key 增长，频率低不等于可以留着。
- 最小修复：固定分片闸，同 key 串行、不同分片并行；碰撞只多一次等待。
- 验证方式：`KeyedGateTests` 两例（同 key 64 路并发峰值必须为 1、不同分片不被阻塞）。S3 用探针对比两种模式，见上。
- 回归风险：分片碰撞让无关 key 偶发串行；64 分片在分享/池数量级下碰撞率低，临界区只含一次库更新。
- 静默控制问句：如果把分片闸换回可移除字典，互斥测试仍会通过，只有在放大窗口的探针里才暴露差异；这是本条的诚实边界。

### F-083 扫描同步无互斥（Medium，已修）

- 证据：修复前 `FileService.Sync.cs` 的 `ScanAndSyncAsync` 没有互斥，直接读全池投影、枚举物理树、批量写。当前 `:33` 用 `ScanGate`（`:244` 定义）单飞，拿不到就回 `Scan.InProgress`；`ResultExtensions.cs:45` 映射 409。
- 机理：两个管理员（或双击）并发扫描，两者都从 "库中无此文件" 出发，各自构造 INSERT；后提交方撞 `FileItems.Path` 唯一索引，外层 catch 把整轮标记为 `Scan.Failed`，端点落 500；同时重复一遍全池枚举。
- 触发条件：扫描进行中再次触发扫描。大池扫描耗时长，重叠窗口大。
- 最坏影响：一次扫描整体失败并返回 500，重复消耗 IO 与 CPU；不丢数据。
- 现有测试为何没拦住：没有并发扫描用例；原实现也没有可注入的互斥点。
- 自我反驳：没有黑盒复现"第二个扫描撞唯一索引"（需要同时在扫描窗口内触发）。代码层面两次扫描的写入集合必然重叠，唯一索引存在且已被 F-001/F-021 证明会拒绝；判定可靠。S3 把"立即拒绝"改成"排队等待"后，锁定测试按预期转红，证明用例确实在测量互斥语义而不是别的。
- 最小修复：进程内单飞闸，不排队，明确返回 `Scan.InProgress`（409）。
- 验证方式：`FileServiceTests.ScanAndSyncAsync_WhileAnotherScanRunning_ReturnsInProgress`，用 `TaskCompletionSource` 卡住第一次扫描的库读取，断言第二次在 2 秒内返回 409。S3 改回排队后 1 failed。
- 回归风险：静态闸跨请求共享，进程内单实例部署口径与既有 DirectoryVersion 一致；多实例部署需要换成分布式锁，与项目既有的单实例声明同边界。
- 静默控制问句：如果没有闸，第二次扫描不会报错而是排队或并发写，测试的"2 秒内返回 409"会失败。

### F-084 扫描单目录异常终止整轮与门禁假绿（Medium，已修）

- 证据：
  - 修复前 `FileService.Sync.cs` 用 `new DirectoryInfo(dir).Attributes` 判 junction、`new FileInfo(file).Length` 取大小，两处 System.IO 直调都在 Application 层，且任一处抛 `UnauthorizedAccessException` 或 `IOException` 会冒泡到方法外层 catch，整轮扫描返回 `Scan.Failed`。
  - G16 的匹配模式只覆盖 `new FileStream|StreamReader|StreamWriter|ZipArchive|DriveInfo`，漏掉 `FileInfo/DirectoryInfo`：修复前 `grep -rnE 'new (FileInfo|DirectoryInfo)' src/NasSystem.Application` 命中 2 处，而 G16 报 `0/0 ≤ 基线`，是假绿。
- 机理：挂载点里出现无权限目录（`System Volume Information`、他人 ACL 目录）或扫描过程中文件被移动/删除时，统计属性抛异常；扫描是深度优先递归，一处失败就让管理员看到"扫描失败"而没有任何部分结果。
- 触发条件：存储根存在不可读子目录，或扫描与外部文件增删并发。
- 最坏影响：整轮扫描不可用；已经枚举并收集的新文件因事务回滚而全部丢弃，管理员无法完成一次完整对账。不丢用户数据。
- 现有测试为何没拦住：没有不可读目录用例；G16 口径漏两型，机械门禁给不出信号。
- 自我反驳：Windows 上无权限目录常见吗？挂载点由应用创建，但池根可以被用户或系统放入只读目录，且 Windows 的 `System Volume Information` 在任意卷根都存在但通常对本用户不可读；判定成立。是否只有权限一种原因？中途消失的文件同样触发，概率更高。
- 最小修复：目录与文件处理各自 try/catch `UnauthorizedAccessException`/`IOException`，跳过并累加 `SkippedUnreadable`；属性读取改走 `IFileSystemService.GetDirectoryInfo/GetFileInfo`；G16 模式补两型，CONVENTIONS §1.5 同步类型清单。
- 验证方式：`FileServiceTests.ScanAndSyncAsync_UnreadableDirectory_SkipsItAndCompletes`（mock 抛 `UnauthorizedAccessException`，断言成功且 `SkippedUnreadable == 1`）。S3 把 catch 改回 `throw;` 后 1 failed。门禁侧：补模式后 G16 仍 `0/0`，且改动前同样的命令命中 2 处，证明新模式能看见旧口径漏掉的调用。
- 回归风险：跳过计数进入 DTO 与消息，前端类型与 API_REFERENCE 已同步；被跳过的目录不会进删除集判定，但其物理文件也不在 `physicalPaths` 中，因此不会误删库中活跃记录（跳过目录的库记录会因"物理不存在"被列入删除集！见未决项）。
- 静默控制问句：如果没有 catch，用例断言成功会失败；如果没有跳过计数，管理员看不到"少扫了什么"。

### F-085 缩略图源文件错误落 500（Low，已修）

- 证据：修复前 `ThumbnailService.cs` 对 `ImageDecoder.Decode` 返回 null 抛 `InvalidOperationException`，被通用 catch 转成 `Thumbnail.GenerationFailed`；该错误码不在 `ResultExtensions` 的表内，按默认落 500。当前新增 `ThumbnailDecodeFailedException`（`:19`）与专用 catch（`:169`），错误码 `Thumbnail.DecodeFailed` 映射 400（`ResultExtensions.cs:72`）。
- 机理：源文件损坏、伪装扩展名、或超出第 1 轮引入的 64MP 兜底上限，都是客户端输入问题，返回 500 会让用户与前端把可处理的输入错误当成服务端故障。
- 触发条件：对损坏文件或超大伪图请求缩略图。
- 最坏影响：错误的 HTTP 语义与误导性错误文案；前端无法区分"这张图有问题"和"服务坏了"。
- 现有测试为何没拦住：`ThumbnailEndpointTests` 用 4x4 有效 PNG；缺失文件用例允许 404 或 500，没有损坏文件用例。
- 自我反驳：把 `GenerationFailed` 整体映射 400 是不是更省事？不能，该码也覆盖服务端缩放/编码异常，一律 400 会掩盖真实故障；所以分专用异常与专用码。是否只有第 1 轮才引入？超限拒绝是第 1 轮加入，损坏文件路径一直存在。
- 最小修复：解码失败抛专用异常，端点映射 `Thumbnail.DecodeFailed`（400），其余生成异常仍 `GenerationFailed`（500）。
- 验证方式：`ThumbnailServiceTests` 用 5 字节假 JPEG 断言错误码为 `Thumbnail.DecodeFailed`。S3 改回 `InvalidOperationException` 后 1 failed。
- 回归风险：视频抽帧失败也归入 DecodeFailed；抽帧来自用户视频，归客户端问题合理。
- 静默控制问句：如果异常类型没分叉，测试会拿到 `GenerationFailed` 并失败。

## 5. 扫描口径记录

| 类 | 命令 | 范围 | 命中 |
|---|---|---:|---:|
| 锁与信号量 | `grep -rnE 'SemaphoreSlim\|ConcurrentDictionary\|Interlocked\|lock \(|Task\.Run\|PeriodicTimer\|BackgroundService' src --include=*.cs`（排除 obj/bin） | 185 文件 | 41 |
| 可移除锁对象 | `grep -rn 'GetOrAdd\|TryRemove\|CurrentCount' src` | 185 | 6 |
| 读后写写入 | `grep -rnE 'GetByFileItemIdAsync\|GetByIdAsync' -A 15 src/NasSystem.Infrastructure/Ocr src/NasSystem.Application` | 逐个读 | 1 条（F-081） |
| 长任务互斥 | 逐个读 `FileService.Sync`、`SnapshotBackgroundService`、`SemanticIndexBackgroundService`、`FileVersionService` | 4 | 1 条（F-083） |
| 门禁口径对账 | `grep -rnE 'new (FileInfo\|DirectoryInfo)' src/NasSystem.Application` 对照 G16 输出 | Application | 2 处 vs 基线 0（F-084） |
| 模拟探针 | `tmp/probe/GateRaceProbe`（5000 次 × 8 并行，两侧窗口各放宽 1ms） | 2 实现 | 14 次 vs 0 次重叠 |

## 6. 修复批次与验证

| 批次 | 提交 | 内容 | 验证 |
|---|---|---|---|
| OCR upsert | `cf9a7ee` | F-081 | 新增 3 例并发用例；S3 DO NOTHING 转红 |
| 分片闸 | `59db1f9` | F-082 | KeyedGateTests 2 例；探针 A/B |
| 扫描 | `1bdd247` | F-083/F-084 | FileServiceTests 2 例；S3 两处转红 |
| 缩略图错误码 | `df9cef3` | F-085 | ThumbnailServiceTests 1 例；S3 转红 |
| 门禁口径 | `.ai` `fea231c` | G16 补两型 | 补后仍 0/0，改动前同命令命中 2 处 |

最终验证：构建 exit 0 / 0 错误；单测 214/0；集成 310/0；前端 183/26、oxlint 0、vue-tsc exit 0、生产构建 exit 0；干净门禁 21 通过 / 0 失败 / 0 跳过。

本轮新增测试 8 个：单元 5 例（KeyedGate 2、扫描 2、缩略图 1），集成 3 例（OcrUpsert 2、OcrEndpoint 并发 1）。

## 7. 未决项与观察项

- F-084 残留：被跳过的目录不进 `physicalPaths`，其库内活跃记录会在删除集判定里被当作"物理消失"而删除。当前取舍是"宁可删记录也不停扫描"，但这会让不可读目录的库记录消失。修复方向是记录被跳过目录的前缀并从删除集中排除（前缀集合已在 `removedFolderPrefixes` 有先例）。本轮未改，属独立批次，需先确认"跳过目录的库记录保留"是期望语义。
- `FileVersionService.CreateAutoVersionAsync` 只重试 2 次：同一文件三路以上并发覆盖时，第三路可能放弃版本。版本是误操作安全网，放弃会留一行 warning 且该次旧内容无版本可回。低概率，登记为观察项。
- `ThumbnailService._inFlightGenerations` 的 `GetOrAdd` 值工厂在竞争下可能执行两次，产生重复生成与一次无害的缓存写失败；无正确性影响。
- 根目录本身的枚举失败仍会让整轮扫描失败（子目录已容错）；池挂载点不可读时失败可接受，未改。
- 第 1 轮的两条观察项仍在：`DirectoryVersion.Versions` 静态字典不淘汰（淘汰会破坏 ETag 语义）、`WebDavAuthGuard` 失败表只按来源 IP 增长。

## 8. 下一轮建议

1. 主轴选「健壮性与故障路径」：先把 F-084 残留明确语义（跳过目录的库记录保留还是删除），再补磁盘满、只读库、ffmpeg 缺失三条故障注入。
2. 交叉轴选「架构与依赖」：G16 假绿说明门禁口径与真源存在漂移风险，值得做一次全 G 表口径对账（模式与 CONVENTIONS 逐条比对）。
3. 并发主轴的下一个候选是 `SnapshotService.CreateAsync` 与文件写入的时序一致性（快照读树不在写入事务内），需要先定义"快照一致性"的验收口径再动手。

## 9. 复现命令

```bash
dotnet build NasSystem.sln
./tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe
./tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe
cd web && npm run test && npm run lint && npm run type-check && npm run build && cd ..
bash .ai/scripts/gate-check.sh
cd tmp/probe/GateRaceProbe && dotnet run && cd ../../..
grep -rnE 'new (FileInfo|DirectoryInfo)' src/NasSystem.Application --include=*.cs
```

## 10. 收尾自检

1. 账本读了吗，有没有重报：读了 F-001 到 F-080 全部账本；F-072/F-073 按部署残余处理；第 1 轮观察项未重报。
2. 基线是本轮实测吗：是。起始 209/307/183、门禁 21/0/0；结束 214/310/183、门禁 21/0/0，均为本轮复跑。
3. 每条发现十项齐全吗：F-081 到 F-085 均含证据、机理、触发、影响、测试缺口、自我反驳、最小修复、验证、回归、静默控制。
4. 有没有凑数条目：无；F-081/F-083/F-084 均指明并发交错的具体两条语句或唯一索引约束。
5. 每个修复是否指认锁定测试，S3 做了吗：F-081/F-083/F-084/F-085 各有 S3 转红记录；F-082 的 S3 是模拟探针 A/B，已在自我反驳中说明黑盒复现边界。
6. 全量测试与门禁本轮复跑且贴计数行了吗：见第 6 节，均为本轮实测。
7. 有没有重提已否决方向：没有。
8. 有没有改范围外代码：只改并发闸、OCR 写入、扫描、缩略图错误码与门禁口径；未改前端业务逻辑（仅类型与文档同步）。
9. 有没有未经验证的性能或内存声明：本轮无性能声明；探针只报重叠次数，不报时延或内存收益。
10. 账本编号连续吗：F-081 到 F-085 连续，F-001 到 F-080 未改。

## 11. 进度

| 栏位 | 状态 |
|---|---|
| 实现进度 | F-081 到 F-085 全部已修（5/5） |
| 验证完成度 | 4 条有测试级 S3 转红记录，F-082 用模拟探针 A/B；全量 214/310/183 与门禁 21/0/0 均本轮复跑 |