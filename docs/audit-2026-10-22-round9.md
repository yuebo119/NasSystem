# NAS System 第 9 轮审计报告（健壮性与故障路径）

- 日期：2026-10-22
- 主轴：健壮性与故障路径
- 交叉轴：架构与依赖
- 模式：审计并修复
- 预算：无硬上限，按任务书停止条件收口

## 1. 已读清单

- `docs/audit-2026-10-22-exhaustive.md` 与第 1 到 8 轮报告：F-001 到 F-104 全部账本。五项待裁决（资源上限、镜像钉版、转码后台化、F-078 分页、共享缓存保留策略）本轮不擅自改。
- `.ai/lessons.md`、`docs/CONVENTIONS.md`（§1 分层、§4 安全、§9 性能口径）。
- 服务器缓存生命周期：`ThumbnailService` 的 TTL 判定、`FfmpegTranscodeService` 的缓存清理入口、现有三个后台服务。
- 重解析点策略的两处实现：`FileSystemService.IsReparsePoint`、`SafeFileEnumeration.IsReparsePoint`。

## 2. 本轮结论

健壮性主轴确认缩略图缓存只增不减（Medium），架构交叉轴确认重解析点判定两份实现存在漂移风险（Low）。两条全部修复：按既有 TTL 增加后台清理，把判定收敛到 Shared 单一策略。单测从 229 增加到 233，集成保持 328，干净门禁 21 通过 0 失败。

## 3. 发现总表

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-105 | Medium | 健壮性 | ThumbnailService.cs:101 | 缓存 TTL 只在读取时判定，过期文件永不删除，缓存目录只增不减 | 已修 6cd6aa7 |
| F-106 | Low | 架构 | FileSystemService.cs / SafeFileEnumeration.cs | 重解析点判定两份实现，规则漂移会让某条路径重新跟随链接 | 已修 5618a09 |

严重级分布：Medium 1、Low 1。确认 2 条，推测 0 条。

## 4. 发现完整论证

### F-105 缩略图缓存过期文件不清理（Medium，已修）

- 证据：`ThumbnailService.GetThumbnailAsync` 命中缓存时判断 `LastWriteTimeUtc > 源文件` 且未超 `CacheExpirationHours`，超期只是不再命中并重新生成；被覆盖的旧缓存文件没有删除路径。当前新增 `ThumbnailCachePruner.cs` 与每小时执行一次的 `ThumbnailCacheCleanupService`，在 Infrastructure DI 注册。
- 机理：TTL 是"读取时失效"而不是"存储时保留"语义。过期文件被新文件覆盖的前提是同一缓存键再次被请求；文件被删除、目录被清理、图片长期不再访问时，旧缓存永远留在盘上。缩略图档位最多 5 档，量大时缓存目录会持续增长。
- 触发条件：长期运行、频繁浏览不同图片；不访问的旧缩略图不会触发覆盖。
- 最坏影响：缓存目录无界增长，最终挤占数据盘；不丢用户数据，但会放大磁盘满风险（F-104 的 507 路径）。定 Medium。
- 现有测试为何没拦住：`ThumbnailEndpointTests` 只验证命中/未命中与 ETag，没有缓存文件生命周期的测试；`ClearCacheAsync` 是手动入口，测试只覆盖手动清除。
- 自我反驳：缓存 TTL 已有 168 小时配置，用户是否应该手动清理？手动清理是管理员动作，长期运行不能依赖人记得；按既有 TTL 自动清理不引入新的保留策略，只是把"读时失效"补成"写后回收"。删除正在被读取的文件会不会导致请求失败？`File.ReadAllBytesAsync` 打开共享读，Windows 上删除可能因共享冲突失败，pruner 捕获 `IOException` 留给下一轮；即使删除成功，同一请求已持有的文件句柄不受影响（删除只移除目录项）。
- 最小修复：`ThumbnailCachePruner.PruneOnce` 扫描缓存目录顶层 `*.jpg`，删除 `LastWriteTimeUtc < now - CacheExpirationHours` 的文件；`ThumbnailCacheCleanupService` 启动 5 分钟后先清一次，之后每小时一次，异常降级不杀宿主。
- 验证方式：`ThumbnailCachePrunerTests` 两例（过期删除/未过期保留、目录缺失返回 0）。S3 把 cutoff 改成 `DateTime.MinValue` 后第一例转红。
- 回归风险：只删缩略图缓存文件，按需重新生成；首次清理延迟 5 分钟，避免与启动争 IO。缓存目录顶层与生成侧一致（生成文件直接落在 `CacheDirectory`）。
- 静默控制问句：如果清理逻辑被停掉，过期文件仍在，`PruneOnce` 返回 0，用例断言 1 失败。

### F-106 重解析点判定两份实现（Low，已修）

- 证据：`FileSystemService.IsReparsePoint` 与 `SafeFileEnumeration.IsReparsePoint` 各自维护相同的 `File.GetAttributes` 与三个 catch。当前抽出 `NasSystem.Shared/IO/ReparsePointPolicy.cs`，两处都委托它。
- 机理：这是安全策略而不是普通工具函数。扫描层跳过链接（F-013/F-091），ZIP/导出也跳过（F-090）；两份实现任何一份改了默认（例如读取失败改为 false），对应路径就会重新跟随链接，而另一条路径仍然安全，测试也未必覆盖到。
- 触发条件：未来维护者只改其中一处；架构层面持续存在。
- 最坏影响：池外内容经某条路径重新进入库或分享包；属安全回归风险，不是当前漏洞。定 Low。
- 现有测试为何没拦住：F-091 的单测 mock 了 `IFileSystemService.IsReparsePoint`，F-090 的集成测试走 `SafeFileEnumeration`，两边各测各的，没有"两处同源"的断言。
- 自我反驳：为两段十几行的重复代码抽一个 Shared 助手是否过度？这条规则同时被扫描、分享、导出三条安全路径依赖，且 Shared 是 Api 与 Infrastructure 都能引用的零依赖层，收敛成本低、漂移代价高。Shared 放 IO 工具是否越界？它只做 BCL 属性判断，不依赖基础设施实现，符合 Shared 的定位。
- 最小修复：新增 `ReparsePointPolicy.IsReparsePoint`（读取失败按 true），`FileSystemService` 与 `SafeFileEnumeration` 委托；删除两份 try/catch。
- 验证方式：`ReparsePointPolicyTests` 两例（缺路径返回 true、普通文件/目录返回 false）。S3 把策略改恒 false 后缺路径用例转红；既有 F-090/F-091 测试保持全绿。
- 回归风险：行为与原来逐字一致；两处调用签名不变。
- 静默控制问句：如果策略恒 false，缺失路径不再按跳过处理，缺路径用例失败。

## 5. 交叉轴其它项复核

- 转码缓存保留策略（待裁决）：`FfmpegTranscodeService` 的 HLS 缓存只有手动清理入口，没有 TTL 或容量上限。缩略图能按既有配置清理，转码缓存没有现成配置，需要产品定义保留时长/容量后再实现，本轮不擅自加。
- WebDAV 失败锁定表按来源 IP 增长（第 2 轮观察）：仍只在失败/成功时清理，无全局扫描；单条小，保留观察。
- 通用转码失败返回 501（第 8 轮观察）：`Transcode.Failed` 与 ffmpeg 缺失共用 501，语义偏"未实现"；改动会动客户端约定，未做。
- 五项待裁决：第 6 轮资源上限、镜像钉版、转码后台化；第 1 轮 F-078 分页；共享缓存保留策略（转码部分）。

## 6. 修复批次与验证

| 批次 | 提交 | 内容 | 验证 |
|---|---|---|---|
| 缓存清理 | `6cd6aa7` | F-105 | 2 个单测；S3 转红 |
| 策略收敛 | `5618a09` | F-106 | 2 个单测；S3 转红 |

最终验证：构建 exit 0 / 0 错误；单测 233/0；集成 328/0；前端 183/26、oxlint 0、vue-tsc exit 0、生产构建 exit 0；干净门禁 21 通过 / 0 失败 / 0 跳过。

本轮新增单元测试 4 例（缓存清理 2、重解析点策略 2）；集成测试数不变。后台清理服务首次执行在启动 5 分钟后，不影响集成测试时长。

## 7. 未决项与观察项

- 转码缓存保留策略（待裁决）：需要 TTL 与容量上限的产品定义；缩略图已按既有配置清理。
- 第 6 轮三项待裁决（资源上限、镜像钉版、转码后台化）与 F-078 分页仍在。
- 前几轮观察项仍在：缩略图缓存键不含 owner、`AllowedHosts: "*"`、快照一致性、WebDAV 失败表增长、nginx `ssl_ciphers` 兼容面、通用转码失败 501。
- 真实只读库/磁盘满沙箱（第 8 轮环境缺口）仍未执行。

## 8. 下一轮建议

1. 先裁五项待裁决，再开新轮；本轮两条都是低争议的收尾项，未决面没有再扩大。
2. 若继续，优先把转码缓存保留策略与 F-078 分页一起定，它们都涉及用户可感知的磁盘与响应行为。
3. 故障注入沙箱安排在有 Docker 的机器上单独跑，不要用构造异常替代端到端验证。

## 9. 复现命令

```bash
dotnet build NasSystem.sln
./tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe
./tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe
cd web && npm run test && npm run lint && npm run type-check && npm run build && cd ..
bash .ai/scripts/gate-check.sh
```

## 10. 收尾自检

1. 账本读了吗，有没有重报：读了 F-001 到 F-104 全部账本；五项待裁决未动、未重报。
2. 基线是本轮实测吗：是。起始 229/328/183、门禁 21/0/0；结束 233/328/183、门禁 21/0/0，均为本轮复跑。
3. 每条发现十项齐全吗：F-105 与 F-106 均含证据、机理、触发、影响、测试缺口、自我反驳、最小修复、验证、回归、静默控制。
4. 有没有凑数条目：无；转码缓存保留策略只登记为待裁决，没有顺手加无配置依据的清理。
5. 每个修复是否指认锁定测试，S3 做了吗：F-105 与 F-106 各有一处 S3 转红。
6. 全量测试与门禁本轮复跑且贴计数行了吗：见第 6 节。
7. 有没有重提已否决方向：没有。
8. 有没有改范围外代码：只加缩略图缓存清理与重解析点策略收敛；未改前端业务逻辑与成功响应契约。
9. 有没有未经验证的性能或内存声明：本轮无性能/内存声明；缓存清理只删过期文件，按需再生。
10. 账本编号连续吗：F-105 到 F-106 连续，F-001 到 F-104 未改。

## 11. 进度

| 栏位 | 状态 |
|---|---|
| 实现进度 | F-105/F-106 已修（2/2） |
| 验证完成度 | 两处修复共 4 个单测与 2 处 S3 转红；全量 233/328/183 与门禁 21/0/0 本轮复跑 |