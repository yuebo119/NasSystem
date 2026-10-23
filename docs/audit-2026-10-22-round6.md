# NAS System 第 6 轮审计报告（部署与配置）

- 日期：2026-10-22
- 主轴：部署与配置
- 交叉轴：时延与热点
- 模式：审计并修复（含两项用户裁决落地）
- 预算：无硬上限，按任务书停止条件收口

## 1. 已读清单

- `docs/audit-2026-10-22-exhaustive.md` 与第 1 到 5 轮报告：F-001 到 F-097 全部账本，含 F-072/F-073 部署残余。第 5 轮两项待裁决本轮已获用户答复（F-084 保留库记录、信息面按角色过滤）。
- `.ai/lessons.md`、`docs/CONVENTIONS.md`、`docs/decisions/`。
- 部署面：`docker-compose.yml`、`Dockerfile`、`nginx.conf`、`docker/smb/entrypoint.sh`、`.dockerignore`、`appsettings*.json`。
- 路由权限矩阵与前端路由 meta 对照（第 5 轮扫描面沿用）。
- 转码链路：`FfmpegTranscodeService`、`TranscodeEndpoints`、`FfmpegTranscodeTests`。

## 2. 本轮结论

先落地两项用户裁决（F-084 跳过目录保留库记录、非管理员信息面收敛），再执行部署与配置主轴。合计确认 3 条新发现并修复：信息面越权可见（Medium）、容器默认值缺机械守护（Low）、HLS 体积统计漏子目录（Low）。时延交叉轴实测首请求同步转码是新增长尾来源，给出量化数据与建议，未擅自改架构。单测保持 226，集成从 318 增加到 322，干净门禁 21 通过 0 失败。

## 3. 发现总表

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-084 | High | 健壮性 | FileService.Sync.cs | 跳过目录的库记录被当物理消失删除（第 2 轮登记） | 已修 fbb7afc（用户裁决落地） |
| F-098 | Medium | 安全隔离 | SystemEndpoints.cs / StorageEndpoints.cs | 非管理员可见主机名、库文件路径、网卡地址与物理挂载路径 | 已修 8485c3c（用户裁决落地） |
| F-099 | Low | 部署 | docker-compose.yml / Dockerfile | 容器默认值（镜像标签、非 root、环境、健康检查）无机械守护 | 已修 776f312 |
| F-100 | Low | 可观测性 | FfmpegTranscodeService.cs | HLS 体积只数缓存根目录，日志与清理释放量少报三个量级 | 已修 776f312 |

严重级分布：Medium 1、Low 2（另有 F-084 为第 2 轮已登记项的裁决落地）。确认 3 条，推测 0 条。

## 4. 发现完整论证

### F-084 跳过目录的库记录保留（High，用户裁决落地）

- 证据：第 2 轮实现"单目录读取失败只跳过并计数"后，被跳过目录不在 `physicalPaths` 里，删除集判定把它当"物理消失"删掉库记录。当前 `FileService.Sync.cs` 新增 `skippedPaths` 集合（目录与文件虚拟路径），`ScanDirectoryAsync` 读取失败时登记，删除集过滤 `IsUnderSkippedPath`（精确匹配 + 目录前缀匹配）。
- 机理：不可读是权限或瞬时状态，不等于用户删除了文件。以一次权限抖动为依据删记录不可逆，且 `.versions` 与回收站语义都要求记录在物理内容仍在时保留。
- 触发条件：池内出现无权限子目录（`System Volume Information`、他人 ACL 目录）或扫描途中外部删除目录。
- 最坏影响：用户记录被静默删除，浏览/分享/版本入口消失；物理文件还在但库不再认它。按裁决改为保留后消除该风险。
- 现有测试为何没拦住：第 2 轮用例只断言 `SkippedUnreadable == 1`，没有给该目录造库记录，因此没覆盖删除集交互。
- 自我反驳：保留记录会不会让删除集永远清不掉真正的删除？只有"读取失败"的路径被排除；可读的正常路径照旧进入删除集。前缀匹配以目录边界（`skipped + "/"`）判定，不会把 `/locked2` 误当 `/locked` 的后代。
- 最小修复：收集跳过路径并在删除集过滤；扫描消息补"其库记录保留"。
- 验证方式：`FileServiceTests.ScanAndSyncAsync_UnreadableDirectory_SkipsItAndCompletes` 改为给被跳过目录造一条活跃记录，断言 `BulkDeleteAsync` 从不调用。S3 移除过滤后转红。
- 回归风险：被跳过路径若其实已被用户删除，会留一条记录到下次可读或永久清理；这是"不可读不等于已删除"的预期代价，已在报告与 API 文档写明。
- 静默控制问句：如果过滤被移除，`BulkDeleteAsync` 会被调用一次，`Times.Never` 断言失败。

### F-098 非管理员信息面越权可见（Medium，用户裁决落地）

- 证据：修复前 `/system/info` 仅 `RequireAuthorization`，返回 `machineName`、`operatingSystem.userName`、`database.filePath`、`network.hostName/ipv4Addresses`、`process` 细节；`/storage/pools` 与 `/pools/{id}` 同样仅认证且返回 `mountPath` 物理路径。前端「系统信息」与「存储管理」路由对非管理员不可见，但端点本身没拦。
- 机理：菜单隐藏不是访问控制，直连端点即可绕过；物理路径与主机网络信息属部署拓扑，属普通用户不需要的信息。
- 触发条件：任何登录用户直接请求上述端点。
- 最坏影响：泄露主机名、内网 IP、库文件路径与挂载路径，为横向探测提供输入；不涉及文件内容。定 Medium。
- 现有测试为何没拦住：没有按角色断言响应内容或缺席字段的用例；前端路由 meta 只影响导航。
- 自我反驳：Dashboard 所有用户都要看系统卡，直接收紧 `/system/info` 会破坏它；因此选择"按角色过滤 DTO"而不是整体收紧。`/storage/pools` 的唯一消费方是管理员页面，Dashboard 走 `/storage/overview`（不含挂载路径），可安全收紧。前端已按可选字段渲染，非管理员的敏感卡片行显示占位符。
- 最小修复：`SystemInfoDto` 的敏感字段改可空，端点按角色置空（`WhenWritingNull` 使它们从响应缺席）；`/storage/pools` 与 `/pools/{id}` 加 `RequireAdmin`；前端类型改可选、Dashboard 加可选链与 `?? 0` 兜底、Storage 路由加 `requiresAdmin`。
- 验证方式：`SystemInfoIsolationTests` 三例（普通用户响应不含五处敏感字段、管理员响应含 database/network/process、普通用户取存储池 403）。S3 分别关掉角色过滤与存储池管理员门后对应用例转红。
- 回归风险：普通用户 Dashboard 的系统卡中，数据库/网络/进程三组显示占位值；如需隐藏整卡，属前端展示决策，本轮未改。
- 静默控制问句：如果过滤被关掉，普通用户响应会包含 `"database"` 等键，断言失败。

### F-099 容器默认值缺机械守护（Low，已修）

- 证据：第 5 轮只锁了 compose 的 JWT 密钥与 nginx/SMB 项，没有锁镜像标签形态、运行时用户、环境名、健康检查与重启策略。
- 机理：这些默认值一旦被改成 `latest`、补上 `USER root`、或误设 `ASPNETCORE_ENVIRONMENT=Development`（异常细节会回显客户端），都要到部署后才暴露，属"静默风险"。
- 触发条件：后续维护者改动部署文件。
- 最坏影响：浮动镜像导致构建不可复现；Development 环境泄露异常细节；无健康检查则编排层看不到故障。
- 现有测试为何没拦住：第 5 轮守护测试范围只到 JWT/SMB/nginx 三项。
- 自我反驳：`nginx:alpine` 仍是浮动变体标签，本断言只禁 `latest`，精确钉版/摘要需要查证可用版本（本机无 Docker，见未决项），不能凭空写版本号。
- 最小修复：新增 `ContainerConfig_MustKeepSafeDefaults`，断言镜像行都带显式标签且无 `latest`、Dockerfile 最后一条 `USER` 是 `nasuser`、compose 用 Production 而非 Development、健康检查与 `restart: unless-stopped` 齐备。
- 验证方式：S3 把 `image: nginx:alpine` 改成 `nginx:latest` 后该用例转红，恢复后绿。
- 回归风险：无，纯静态断言；后续若引入需要 `latest` 的场景（不推荐）会显式失败。
- 静默控制问句：如果断言缺失，`latest` 标签不会被任何门禁发现。

### F-100 HLS 体积统计漏子目录（Low，已修）

- 证据：`DirectorySize` 用 `Directory.EnumerateFiles(directory)`（默认只枚举顶层），分片在 `v{N}/` 子目录里。探针实测 5 秒 1080p 源：`HlsPlaylist.TotalSizeBytes` 报 373 字节，产物目录实际 1,151,356 字节（7 个文件）；同一函数还用于 `ClearCacheAsync` 的释放字节统计。
- 机理：`master.m3u8` 在缓存根，分片与各档子列表在子目录；顶层枚举只数到 playlist。
- 触发条件：每次转码完成日志与每次清缓存返回值。
- 最坏影响：观测数据失真（"HLS 转码完成 373 字节"），清理释放量少报约三个量级；不影响播放与磁盘实际占用。定 Low。
- 现有测试为何没拦住：`FfmpegTranscodeTests` 只断言 `TotalSizeBytes > 0`，373 也满足。
- 自我反驳：这个数有没有被业务逻辑消费？除日志与 `ClearCacheAsync` 返回值外没有调用方，所以不是配额或计费错误，定 Low 而非更高。
- 最小修复：改 `SearchOption.AllDirectories` 递归统计；转码测试补一条"等于递归字节和"的断言。
- 验证方式：断言 `playlist.TotalSizeBytes == 递归字节和`。S3 恢复 `TopDirectoryOnly` 后该用例转红。
- 回归风险：无；`ClearCacheAsync` 的释放量随之变为真实值。
- 静默控制问句：如果还是只数顶层，递归和断言失败。

## 5. 时延交叉轴：首请求同步转码长尾（实测）

- 探针：`tmp/probe/HlsLatencyProbe`（一次性，tmp 已忽略）。同一份 5 秒 1920x1080 testsrc 源，两轮各测一次冷启动（每次前清缓存）与一次缓存命中，本机 32 逻辑核。
- 结果：

| 轮次 | 冷启动（含探测与编码器探测） | 缓存命中 | 产物 |
|---|---:|---:|---:|
| 第 1 轮 | 1349ms | 0.8ms | 7 文件 1,151,356 字节 |
| 第 2 轮 | 835ms | 0.4ms | 同上 |

- 结论：缓存命中在毫秒级，首请求是整条链路的唯一长尾，耗时与源时长近似成正比（本机约 0.17 到 0.27 倍实时；第 1 轮多出的一次约 500ms 是进程内编码器探测缓存未命中的代价）。按此比例 [推断]：2 小时源在本机约 20 到 35 分钟，第 5 轮放到的 nginx 3600 秒能覆盖；但低功耗 NAS 上软件 x264 可能低于 0.5 倍实时，2 小时源会超过 3600 秒。当前设计是同步等待，超时后第 3 轮已实现"取消即杀 ffmpeg"，重试要从零开始。
- 建议（需裁决，本轮未动架构）：三种方向。① 转码后台化，首请求先回 202 加进度，前端轮询或等 SignalR；② 预转码/预热（上传或扫描后按低优先级排队）；③ 保持同步但按源时长给用户明确提示并允许更长超时。推荐先做 ②（改动面小、直接消掉首播等待），① 属架构变更单独排期。

## 6. 扫描口径记录

| 类 | 命令与范围 | 命中 |
|---|---|---:|
| 删除集交互 | 读 `FileService.Sync.cs` 删除集与跳过计数交互 | 1 处（F-084） |
| 权限矩阵 × 页面调用面 | 全部端点授权属性 + 前端路由 meta + Dashboard 调用 | 2 处（F-098） |
| 部署默认值 | `docker-compose.yml`/`Dockerfile`/`nginx.conf` 逐项核对 | 1 组（F-099） |
| 外部进程产物统计 | 探针逐文件核算 vs 服务返回值 | 1 处（F-100） |
| 转码时延 | 一次性探针，5 秒 1080p，两轮冷/热对比 | 见第 5 节 |

## 7. 修复批次与验证

| 批次 | 提交 | 内容 | 验证 |
|---|---|---|---|
| F-084 落地 | `fbb7afc` | 跳过路径保留库记录 | 单测断言 BulkDelete 从不调用；S3 转红 |
| 信息面 | `8485c3c` | F-098 角色过滤 + 存储池收紧 + 前端适配 | 集成 3 例；两处 S3 转红 |
| 部署与体积 | `776f312` | F-099 容器守护 + F-100 递归体积 | 两处 S3 转红 |

最终验证：构建 exit 0 / 0 错误；单测 226/0；集成 322/0；前端 183/26、oxlint 0、vue-tsc exit 0、生产构建 exit 0；干净门禁 21 通过 / 0 失败 / 0 跳过。

本轮新增集成测试 4 例（信息隔离 3、容器守护 1），修改既有用例 2 处（扫描不可读目录、转码体积）；单元测试数不变。

过程备注：集成测试里用共享工厂写 "admin" 账号会与其他测试类并发撞 `Users.Username` 唯一索引，本轮改用唯一名加显式 `SuperAdmin` 角色。测试基线里已有同类教训（共享库唯一键），此处是新增一例。

## 8. 未决项与观察项

- 资源上限（待裁决）：compose 未声明 CPU/内存上限。加限制需要与机器容量匹配的具体值，且本机无 Docker（无 Engine/Desktop），无法验证 `mem_limit`/`cpus` 的 compose 语义。建议方向：加 `NAS_MEM_LIMIT`/`NAS_CPUS` 环境开关，默认 0 表示不限制（保持现状），生产显式设置；落地前先在启用 Docker 的环境跑一次 `docker compose config` 与压测。
- 镜像精确钉版（待裁决）：`nginx:alpine` 与 `node:20-alpine` 是浮动变体标签。钉到具体版本或 digest 需要 `docker manifest inspect` 查证可用标签，本机无 Docker 无法验证，未擅自写版本号。
- 首请求同步转码（待裁决）：见第 5 节建议。
- 缩略图缓存键不含 owner（第 5 轮观察）：当前活跃路径唯一，未找到可复现跨用户命中；若未来引入每用户路径需把 owner 纳入键。
- `AllowedHosts: "*"`（第 5 轮观察）：未收敛为部署域名白名单。
- 重解析点判定仍有两份实现（第 4 轮观察）：`FileSystemService.IsReparsePoint` 与 `SafeFileEnumeration.IsReparsePoint`。
- `SnapshotService.CreateAsync` 读树不在写入事务内（第 3 轮观察）。
- nginx `ssl_ciphers` 只列两条 ECDHE，兼容面偏窄（第 5 轮观察）。

## 9. 下一轮建议

1. 先裁决资源上限、镜像钉版与转码后台化三项，再开「时延与热点」为主轴：把同步转码、首屏与目录浏览三条长尾放在同一轮量级归因。
2. 交叉轴选「健壮性与故障路径」：磁盘满、SQLite 只读、ffmpeg 缺失三条故障注入仍未系统做过。
3. 部署轴收尾项（资源上限与镜像钉版）建议在有 Docker 的环境单独跑一轮，本机无法验证的语义不要写成断言。

## 10. 复现命令

```bash
dotnet build NasSystem.sln
./tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe
./tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe
cd web && npm run test && npm run lint && npm run type-check && npm run build && cd ..
bash .ai/scripts/gate-check.sh
cd tmp/probe/HlsLatencyProbe && dotnet run && cd ../../..
```

## 11. 收尾自检

1. 账本读了吗，有没有重报：读了 F-001 到 F-097 全部账本；F-072/F-073 按部署残余处理，未重报。
2. 基线是本轮实测吗：是。起始 226/318/183、门禁 21/0/0；结束 226/322/183、门禁 21/0/0，均为本轮复跑。
3. 每条发现十项齐全吗：F-098 到 F-100 均含十项；F-084 为第 2 轮已登记项的裁决落地，按原编号记录。
4. 有没有凑数条目：无；转码长尾只登记为观察与建议，没有包装成缺陷。
5. 每个修复是否指认锁定测试，S3 做了吗：F-084、F-098（两处）、F-099、F-100 共 5 处 S3 转红记录。
6. 全量测试与门禁本轮复跑且贴计数行了吗：见第 7 节。
7. 有没有重提已否决方向：没有。
8. 有没有改范围外代码：只改扫描删除集、信息面过滤、部署守护断言与转码体积统计；未改成功响应契约（系统信息只对普通用户收窄字段）。
9. 有没有未经验证的性能或内存声明：转码时延来自本机探针两轮实测，外推到长视频的部分已标 [推断]；无内存声明。
10. 账本编号连续吗：F-098 到 F-100 连续，F-001 到 F-097 未改；F-084 沿用原编号。

## 12. 进度

| 栏位 | 状态 |
|---|---|
| 实现进度 | F-084 落地 + F-098/F-099/F-100 全部已修（4/4） |
| 验证完成度 | 5 处测试级 S3 转红；转码时延有探针实测；全量 226/322/183 与门禁 21/0/0 本轮复跑 |