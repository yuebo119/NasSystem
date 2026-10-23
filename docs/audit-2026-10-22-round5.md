# NAS System 第 5 轮审计报告（安全隔离）

- 日期：2026-10-22
- 主轴：安全隔离
- 交叉轴：部署与配置
- 模式：审计并修复
- 预算：无硬上限，按任务书停止条件收口

## 1. 已读清单

- `docs/audit-2026-10-22-exhaustive.md` 与第 1/2/3/4 轮报告：F-001 到 F-093 全部账本，含 F-072/F-073 部署残余与 F-084 待裁决残留。本轮不重报。
- `.ai/lessons.md`、`docs/CONVENTIONS.md`（§1 分层、§4 安全、§5 时间）、`docs/decisions/`。
- `git log --oneline -40` 与最近提交 diff：避让前四轮改动面（解码、快照、OCR、扫描、外部进程、领域边界、重解析点、访问解析）。
- 部署面：`docker-compose.yml`、`Dockerfile`、`nginx.conf`、`docker/smb/entrypoint.sh`、`appsettings*.json`、`.dockerignore`。
- 路由权限矩阵：全部 `MapGet/Post/Put/Delete` 与 `RequireAuthorization/RequireAdmin` 逐条核对，前端路由 `requiresAdmin` 与页面调用面对照。

## 2. 本轮结论

安全隔离主轴确认 2 条、部署交叉轴确认 2 条：跨用户覆盖回收站物理内容、SMB 默认弱口令、nginx 与上游语义不匹配、时间源门禁假绿。4 条全部修复并配锁定测试或机械门禁；单测保持 226，集成从 315 增加到 318，干净门禁 21 通过 0 失败。

## 3. 发现总表

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-094 | High | 安全隔离 | FileService.Transfer.cs:618 | 写入目标判定只查活跃记录，他人重建同名路径覆盖回收站物理内容 | 已修 0b5a481 |
| F-096 | High | 部署/安全 | docker/smb/entrypoint.sh:38 | SMB sidecar 缺省密码是写进仓库的 nasadmin | 已修 c73594c |
| F-097 | Medium | 部署 | nginx.conf:29,55,106,111,136 | nginx 默认值与上游不匹配（body 10G / HSTS 缺 / 600s 截断长转码 / 全量缓冲上传） | 已修 c73594c |
| F-095 | Low | 门禁/时间 | FileService.Transfer.cs:709 | 复制名兜底用 DateTime.Now，G15 只匹配 UtcNow 报绿 | 已修 `2dfc200`（.ai） |

严重级分布：High 2、Medium 1、Low 1。确认 4 条，推测 0 条。

## 4. 发现完整论证

### F-094 写入目标判定不含回收站记录（High，已修）

- 证据：`FileItemRepository.GetByPathAsync`、`PathExistsAsync` 都带 `IsDeleted = 0`；上传（`FileService.Transfer.cs:86` 原逻辑）、建目录（`:35`）、改名（`:365`）、移动（无目标判定）、复制命名（`:626` 起）都只查活跃记录。当前新增 `PathExistsIncludingDeletedAsync`（`FileItemRepository.cs:733`）与统一助手 `CheckDestinationConflictAsync`（`:618`），上传/建目录/改名/移动/复制五处全部改用它。
- 机理：物理路径与虚拟路径同构（`GetPhysicalPath` 无用户前缀），软删只是库标记，物理内容留在盘上。活跃记录为空时才走"新文件"分支，而受害者回收站记录的物理路径正是同一路径；上传用 `MoveFile(overwrite:true)` 落位，先把受害者内容替换掉，之后才插新记录，而部分唯一索引（只管未删）允许新旧同路径共存。
- 触发条件：A 软删一个目录（或文件），B 重建同名目录并上传同名文件。B 需要自己拥有同名父目录，因此典型形态是"整目录删除后重建同名结构"。
- 最坏影响：A 回收站里的内容被 B 的字节覆盖，永久不可恢复（恢复会撞 B 的活跃记录唯一索引，且盘上内容已非 A 的数据）；`FileItem` 记录的旧大小/哈希与磁盘内容错位。属跨用户数据销毁，定 High。
- 现有测试为何没拦住：`Upload_ToOtherUsersPath_DoesNotReplaceTheirPhysicalContent` 只覆盖"他人活跃记录"这一支；`CrossUserIsolationTests` 其余用例都是读路径与活跃写路径，没有软删记录参与的写入用例。
- 自我反驳：B 能不能直接往 A 的活跃目录里传？不能，父目录属主校验会拦。所以缺口只在"父目录也被软删、B 重建"的形态；这不是理论路径，回收站里的整目录被重建是常见操作。是否已有别的防线？没有：扫描同步只对齐物理与库，不阻止写入；唯一索引是部分索引，软删行不参与冲突。S3 停掉助手后新用例转红，证明这条路径此前确实会被打穿。
- 最小修复：目标占用判定扩到含软删记录。他人记录（不论软删与否）一律按不存在处理，不确认存在性；本人软删记录在上传与建目录时视为可写（保持"删除后重用同名路径"的既有行为），在改名/移动/复制时视为占用（这些路径用非覆盖 Move，物理同路径必失败）。复制命名改为按含软删占用生成新名，避免复用回收站路径。
- 验证方式：`CrossUserIsolationTests.Upload_UnderOtherUsersRecycledPath_DoesNotDestroyRecycledContent` 走真实 API：A 建目录与文件、软删目录、B 重建同名目录并上传同名文件，断言 B 的两步都被拒且 A 的物理内容仍是原文。S3 让冲突判定恒返回 null 后该用例转红，恢复后绿。
- 回归风险：上传/建目录对本人软删路径的行为不变；改名/移动/复制遇到回收站同路径从"物理 Move 失败或 500"变为明确的已存在错误，属改善。既有 Active 记录路径的判定语义（他人 NotFound、本人 AlreadyExists）不变。
- 静默控制问句：如果判定仍是只查活跃，B 的 `mkdir` 返回 200 且物理文件内容变成 "intruder"，两处断言都会失败。

### F-096 SMB sidecar 缺省弱口令（High，已修）

- 证据：`docker/smb/entrypoint.sh:34-37` 原为 `NAS_SMB_PASSWORD` 为空时赋 `nasadmin` 并打 WARN；compose 里该变量默认空（`${NAS_SMB_PASSWORD:-}`）。当前 `:38` 改为打印 FATAL 并 `exit 1`；compose 注释同步更新。
- 机理：SMB sidecar 是显式 profile，但启用它的人通常只记得 `--profile smb up -d`，不会注意到日志里的 WARN。445 端口对局域网开放，密码是仓库里的字面量，任何人可读写整个共享卷（该卷同时是主应用的数据目录）。
- 触发条件：`docker compose --profile smb up -d` 且未设置 `NAS_SMB_PASSWORD`。属可选组件，所以不是默认暴露，但一旦启用就是全量暴露。
- 最坏影响：共享卷内所有用户文件可被局域网任意人读写；可通过 SMB 写入的路径绕过应用层配额与软删语义（该 sidecar 的既有边界文档已声明）。
- 现有测试为何没拦住：`DeploymentConfigGuardTests` 只锁了 compose 的 JWT 密钥默认值，没有锁 sidecar 的密码来源。
- 自我反驳：WARN 加注释是否已足够？WARN 埋在容器启动日志里，且"本地体验"与"生产"共用同一份 compose，不能靠人读日志兜底。改为 fail-fast 会不会破坏本地快速体验？启用 smb profile 本身就是显式动作，报错信息给出了设置变量的指引，成本是一次显式设置。
- 最小修复：缺省即拒启动并说明原因；compose 注释改成 fail-fast 口径。
- 验证方式：`DeploymentConfigGuardTests.SmbEntrypoint_MustNotShipDefaultPassword` 断言脚本不含默认赋值、含 FATAL 文案与 `exit 1`。S3 把默认赋值恢复后该用例转红。
- 回归风险：未设置变量的 SMB 部署从"弱口令可用"变为"启动失败"，这是期望的安全默认。
- 静默控制问句：如果恢复默认赋值，脚本含 `NAS_SMB_PASSWORD="nasadmin"`，断言立刻失败。

### F-097 nginx 默认配置与上游语义不匹配（Medium，已修）

- 证据：`nginx.conf` 原 `client_max_body_size 10G`（:55 区域）、全局 `proxy_read_timeout 600s`、`/api/` 无 `proxy_request_buffering off`、安全头无 HSTS、`http` 块无 `server_tokens off`。当前 `:29` 关闭版本号、`:55` 改 512m、`:106` 加 HSTS、`:111` 新增 `/api/v1/media/stream/` 独立 location（读写 3600s、buffering off）、`:136` 上传流式直通。
- 机理：四条各自独立，列举如下。
  1. body 10G 与 Kestrel 的 500MB（`UploadLimit` 常量）不一致：超限请求会先被 nginx 全量缓冲到盘上，再被 Kestrel 拒绝，白付一份磁盘与时间。
  2. 全局 600s 读超时覆盖同步转码：HLS 首请求同步等待 ffmpeg，长视频远超 10 分钟；超时断流后应用第 3 轮已实现"取消即杀 ffmpeg"，进度作废，重试从零开始，长视频永远播不了。
  3. 缺少 HSTS 与 `server_tokens`：TLS 部署少了防降级与版本隐藏。
  4. nginx 默认缓冲请求体，抵消应用侧 multipart 流式化（2026-10-17 已改 `MultipartReader` 单趟写盘），500MB 上传变成"网络到 nginx 临时文件再到 Kestrel"的双趟。
- 触发条件：分别对应超限上传、长视频转码、TLS 生产部署、大文件上传，均为常规使用。
- 最坏影响：长视频通过 nginx 无法播放（反复超时与作废）；上传多一份磁盘写入与延迟；缺少 HSTS 的首次访问可被降级。定 Medium：均有可用绕法（直连、短视频），但生产路径上会踩。
- 现有测试为何没拦住：部署配置守护只覆盖 compose 的 JWT 项；nginx 语义与上游常量的关系没有机械断言。
- 自我反驳：600s 是不是应用有意的上限？应用侧没有总请求超时，转码也没有内部超时；上限只来自 nginx，看不出设计意图。`proxy_request_buffering off` 会不会让慢客户端占用 Kestrel 线程更久？Kestrel 有 `RequestHeadersTimeout` 与连接级限制，且这正是上游为流式上传设计的形态。HSTS 在证书轮换时可能造成锁定：注释已写明回退方式。
- 最小修复：body 512m、HSTS、server_tokens off、`/api/` 关闭请求缓冲、HLS 单独 3600s 超时。
- 验证方式：`DeploymentConfigGuardTests.NginxConfig_MustAlignWithUpstreamLimitsAndLongTranscode` 机械断言五项，其中 stream location 内的 `proxy_read_timeout 3600s` 单独解析块内文本。S3 把该超时改回 600s 后用例转红。
- 回归风险：body 上限收紧后，>512MB 的请求由 nginx 拒绝（此前由 Kestrel 拒绝），语义一致；HSTS 影响后续 HTTP 访问（期望行为）。
- 静默控制问句：如果 stream 超时仍 600s，块内断言失败；如果 body 仍是 10G，对齐断言失败。

### F-095 门禁漏 DateTime.Now 与本地时间混用（Low，已修）

- 证据：`FileService.Transfer.cs:709` 原为 `DateTime.Now:yyyyMMddHHmmss`（复制名兜底）；G15 模式只匹配 `DateTimeOffset.UtcNow|DateTime.UtcNow`（`.ai/scripts/gate-check.sh:359` 区域）。当前代码改用 `_dateTime.UtcNow`，门禁模式补 `DateTimeOffset.Now|DateTime.Now`。
- 机理：CONVENTIONS §5.2 明令禁止本地时间与 UtcNow 混用，但门禁只覆盖 UtcNow，于是唯一一处 `DateTime.Now` 年年报绿。
- 触发条件：一次目录内 1000 次重名碰撞才会走到该兜底分支（极端但可达：批量复制的名字模式），此后生成的文件名带服务器本地时间，跨时区语义不一致。
- 最坏影响：文件名时间戳口径错（本地时间），且门禁对该家族永久失明；不丢数据。
- 现有测试为何没拦住：该分支需要 1000 次碰撞，没有用例；门禁是唯一机械防线，而它有口径缺口。
- 自我反驳：`DateTime.Now` 是否只是显示用途？它是文件名的一部分，属用户可见输出，§5.2 明确要求统一 UtcNow。补门禁会不会误报？`DateTime.Now` 在 src 下仅此一处，补后计数不增；变异探针验证了它能红能绿。
- 最小修复：改用注入的 `_dateTime.UtcNow`；G15 模式补 `Now` 两种形态，基线维持 16。
- 验证方式：门禁实跑 `16/16` 绿；变异探针（临时文件写 `DateTime.Now`）实跑 `FAIL 17 > 16`，删除后恢复 `PASS 16/16`。
- 回归风险：无；模式收紧只影响未来新增。
- 静默控制问句：如果模式没补，探针文件不会让 G15 变红，`--verify` 的机械防线就仍是假绿。

## 5. 扫描口径记录

| 类 | 命令与范围 | 命中 |
|---|---|---:|
| 目标路径判定 | `grep -rn "PathExistsAsync\|GetByPathAsync\|GetByPathIncludingDeletedAsync" src/NasSystem.Application` | 5 处写入目标（F-094） |
| 路由权限矩阵 | 全部端点 `RequireAuthorization/RequireAdmin` 逐条核对 + 前端 `requiresAdmin` 对照 | 2 处不一致（观察） |
| 外部进程与部署默认值 | 读 `docker-compose.yml`/`Dockerfile`/`nginx.conf`/`docker/smb/entrypoint.sh` | 4 处（F-096/F-097） |
| 时间源门禁口径 | `grep -rn "DateTime\.Now\|DateTimeOffset\.Now" src` 对照 G15 模式 | 1 处（F-095） |
| 缓存键与归属 | 读 `ThumbnailService` 缓存键、`DirectoryVersion`、`FfmpegTranscodeService.HashKey` | 1 处观察（缩略图键不含 owner） |

## 6. 修复批次与验证

| 批次 | 提交 | 内容 | 验证 |
|---|---|---|---|
| 回收站写入 | `0b5a481` | F-094（含 F-095 代码侧） | CrossUserIsolationTests 新例；S3 转红 |
| 门禁口径 | `.ai` `2dfc200` | F-095 模式补 Now | 变异探针 FAIL/PASS |
| 部署加固 | `c73594c` | F-096/F-097 | DeploymentConfigGuardTests 2 例；S3 各转红 |

最终验证：构建 exit 0 / 0 错误；单测 226/0；集成 318/0；前端 183/26、oxlint 0、vue-tsc exit 0、生产构建 exit 0；干净门禁 21 通过 / 0 失败 / 0 跳过。

本轮新增测试 3 个（集成：回收站写入 1、SMB 口令 1、nginx 配置 1）；单元测试无新增，两个既有用例按新判定改桩。

## 7. 未决项与观察项

- F-084 残留（第 2 轮登记，仍待你裁决）：被跳过的不可读目录其库内活跃记录会在删除集判定里被当作"物理消失"删除。
- 非管理员可见的系统与存储信息（待裁决）：`/system/info` 任何登录用户可读，含主机名、网卡 IP、进程与数据库版本；`/storage/pools` 与 `/storage/pools/{id}` 也是仅认证，返回 `MountPath` 物理路径。前端对应页面里「系统信息」路由已 `requiresAdmin`，但 Dashboard（所有用户）会调 `/system/info`，所以收紧端点会破坏 Dashboard。选项：给 `/system/info` 做按角色过滤的 DTO（普通用户只见 CPU/内存等非敏感项），或拆分只读端点；`/storage/pools` 可先收紧为管理员，Dashboard 用的 `/storage/overview` 不含物理路径。需要产品决定。
- 主应用端口默认对外（待裁决）：compose `ports: "6050:6050"` 在所有网卡上发布 HTTP；未启用 nginx profile 时，JWT 与登录口令走明文。选项：默认绑 `127.0.0.1:6050:6050` 并把直连访问写进文档覆盖项，或提供单独的 prod compose。改动会破坏"不用 nginx 直连"的默认体验，需裁决。
- 缩略图缓存键不含 owner（观察）：键为 `HashPath(虚拟路径)-宽x高`，`GetThumbnailAsync` 收 userId 但没进键。当前活跃记录对同一虚拟路径是唯一的（部分唯一索引），因此未找到可复现的跨用户命中；若未来引入每用户独立路径或放宽同路径共存，必须把 owner 纳入键。`ClearCacheAsync` 按路径前缀清理，加 owner 时需同步。
- 其它部署观察：compose 无 CPU/内存上限（转码或 OCR 异常时可能拖垮宿主）；`AllowedHosts: "*"` 未收敛；基础镜像用 `11.0-preview` 与 `nginx:alpine` 浮动标签，未钉 digest；`ssl_ciphers` 只列两条 ECDHE，兼容面偏窄。
- 重解析点判定仍有两份实现（`FileSystemService.IsReparsePoint` 与 `SafeFileEnumeration.IsReparsePoint`，第 4 轮观察）。
- `SnapshotService.CreateAsync` 读树不在写入事务内（第 3/4 轮观察）。

## 8. 下一轮建议

1. 先裁决 F-084 残留与"非管理员信息面"两项，再开「部署与配置」为主轴的一轮，把 compose/nginx/Dockerfile 的默认值、资源上限、镜像固定与文档一致性做成门禁断言。
2. 交叉轴选「时延与热点」：转码同步等待在 nginx 侧刚放宽到 3600s，但"首请求同步转码"本身仍是长尾来源，可评估后台化或预热。
3. 若继续安全隔离轴，下一个候选是 WebDAV 与 SMB 两条旁路的权限边界复核（SMB 已声明绕过应用层语义，需回报边界文档是否覆盖新发现）。

## 9. 复现命令

```bash
dotnet build NasSystem.sln
./tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe
./tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe
cd web && npm run test && npm run lint && npm run type-check && npm run build && cd ..
bash .ai/scripts/gate-check.sh
grep -rn "GetByPathAsync\|PathExistsAsync" src/NasSystem.Application --include=*.cs
grep -rn "DateTime\.Now\|DateTimeOffset\.Now" src --include=*.cs
```

## 10. 收尾自检

1. 账本读了吗，有没有重报：读了 F-001 到 F-093 全部账本；F-072/F-073/F-084 按既有状态处理，未重报。
2. 基线是本轮实测吗：是。起始 226/315/183、门禁 21/0/0；结束 226/318/183、门禁 21/0/0，均为本轮复跑。
3. 每条发现十项齐全吗：F-094 到 F-097 均含证据、机理、触发、影响、测试缺口、自我反驳、最小修复、验证、回归、静默控制。
4. 有没有凑数条目：无；缩略图缓存键与信息面均只登记为观察/待裁决，没有包装成确认缺陷。
5. 每个修复是否指认锁定测试，S3 做了吗：F-094/F-096/F-097 有测试级 S3 转红；F-095 用门禁变异探针红/绿双验。
6. 全量测试与门禁本轮复跑且贴计数行了吗：见第 6 节。
7. 有没有重提已否决方向：没有。
8. 有没有改范围外代码：只改目标路径判定、时间源、SMB 口令与 nginx/部署文件；未改前端业务逻辑与成功响应契约。
9. 有没有未经验证的性能或内存声明：本轮无性能声明；nginx 缓冲与超时均按配置语义陈述，未做吞吐测量。
10. 账本编号连续吗：F-094 到 F-097 连续，F-001 到 F-093 未改。

## 11. 进度

| 栏位 | 状态 |
|---|---|
| 实现进度 | F-094 到 F-097 全部已修（4/4） |
| 验证完成度 | 3 条有测试级 S3 转红，F-095 有门禁变异探针；全量 226/318/183 与门禁 21/0/0 本轮复跑 |