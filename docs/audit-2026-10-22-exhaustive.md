# NAS System 穷尽式审计报告（2026-10-22）

> 审计范围：Domain / Application / Infrastructure / Api 四层 + Vue 3 前端 + 部署面（Dockerfile / docker-compose / nginx）+ 文档对账。
> 方法：第 0 步实测基线 → 第 1 步项目事实 → 第 2 步清单扫描（机械 / 历史 / 数据流 / 并发 / 极端数据 / 测试缺口 / 部署）→ 第 3 步四视角对抗 → 第 4 步发现登记。
> 编号：F-001 起连续，跨节不重置。三条 Critical 已修复（a267a44 / ad88753 / 9f3bc6d），其余登记待裁决。

## 1. 本轮结论

1. 扫描覆盖机械扫描 13 项、历史复发、五条数据流链路、并发与时序、极端数据推演、测试缺口、部署运维七类，外加四视角对抗审查与文档数字全量对账，共登记 **71 条发现**（确认缺陷 68 + 待验证推测 3）。
2. 严重级分布：**Critical 3 条（全部已修，均附修复前红修复后绿的锁定测试）**、High 15、Medium 21、Low 32（含文档类 13）。
3. 最危险的一条是 F-002：分享子路径按全库路径解析且无归属校验，匿名用户持旧分享码即可在路径被他人重建后读到新属主的文件内容（已修，S3 反向验证实测修复前攻击成功）。
4. 测试基线与文档声称差异巨大：实测单测 199 / 集成 273 / 前端 183，ROADMAP §8 声称的 178 / 223 / 136 全面过期，项目自带门禁处于**红色状态**（NAS-G7/G15/G16 三项 FAIL，main 分支带着红门禁在走）。
5. 结构性结论：本项目的缺陷不在单点疏漏，而在三个复发家族——「物理操作先于库提交、失败无补偿」（F-001/F-008/F-009/F-021/F-031 同族）、「跨用户隔离按不存在处理后仍走完整写管线」（F-001/F-002/F-005 同族）、「文档与配置声称和代码事实漂移」（F-049 起 22 条同族）。

> **修复执行状态（2026-10-22 修复轮，权威口径）**：用户裁决「按最优方案修复」后，71 条发现**全部修复**（分十三个批次提交，清单见第 8 节），0 条残余。另有两项部署侧待办：F-072 需要运维在 compose 填可信代理 IP 才启用真实客户端隔离（默认关闭是安全取舍，见 8.2）；F-028 的 Domain 实体层（16 处中的 13 处实体 + 2 处 Shared 信封）为「把时间戳穿进每个变更方法」的领域语义改动，单独批次。第 3 节总表的状态列以第 8 节为准。

---

## 2. 扫描口径记录

| 扫描类 | 命令 / 方法 | 检查文件数 | 命中 |
|---|---|---|---|
| 后端构建 | `dotnet build NasSystem.sln` | 7 项目 | exit 0，0 错误，30 警告（全部 tools/Benchmark 的 CA1305/CA1865） |
| 单元测试 | 直跑 `tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe` | 13 文件 | 199 total / 0 failed / exit 0 / 0.33s |
| 集成测试 | 直跑 `tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe` | 51 文件 | 基线 270 → 273 total / 0 failed / exit 0 / 23.6s |
| 前端 vitest | `web/` 下 `npx vitest run` | 26 spec | 183 tests / 26 files 全过 / exit 0 / 8.89s |
| oxlint | `npx oxlint --max-warnings 0` | 87 files | 0 warnings 0 errors / exit 0 |
| vue-tsc | `npx vue-tsc --noEmit` | — | exit 0 |
| 前端生产构建 | `npm run build` | — | exit 0 / 2.41s（仅 chunk 体积提示） |
| 项目门禁 | `bash .ai/scripts/gate-check.sh --allow-dirty` | 21 项 | **exit 1**：17 过 / 1 警告 / 3 FAIL（G7 违规数 2、G15 当前 51>基线 35、G16 当前 32>基线 21） |
| S1 catch 过滤 | `grep -rn "catch" src --include=*.cs \| grep Exception` | 238 cs | 51 处 catch，10 处带 OCE 过滤；4 处违反铁律 6（F-012/D2 等） |
| S2 同步阻塞 | `.Result/.Wait()/GetAwaiter().GetResult()` | 238 cs | 4 处原始命中，逐条读上下文后 2 处误报（元组字段名 + 已文档化桥接）、2 处门禁误报（见 F-029） |
| S3 static 可变 | static 字段扫描 | — | 3 处（StorageService.RefreshGates / FfmpegTranscodeService.TranscodeGate / SemanticIndexCoordinator._gate），均为并发安全容器，无缺陷 |
| S4 new HttpClient | grep | — | **0 命中** |
| S5 锁 | lock / SemaphoreSlim 全查 | — | 12 处，临界区内均无 await，合规 |
| S6 Application 裸 IO | gate G16 白名单口径 | — | **32 处 > 基线 21**（F-027） |
| S7/S8 层关键字 / sealed / I 前缀 | gate G3/G4/G5/G9 | — | 全 PASS |
| S9 占位符 | TODO / NotImplementedException / placeholder | — | 0 命中（lessons 铁律 9 当前合规） |
| S10 前端泄漏 | 子代理专项（定时器/监听/Blob/SWR/竞态） | 47 前端文件 | 17 条确认（F-032..F-048） |
| S11 配置死键 | appsettings × ConfigDescriptors × SystemSettings 三方交叉 | — | 2 死键 + 5 节零声明 + 2 项文档失实（F-049..F-051） |
| S12 孤儿端点 | 104 路由 × 前端调用方 | 17 端点文件 | 5 条未登记孤儿 + 3 处豁免账实不符（F-052..F-057） |
| S13 文档漂移 | 8 份文档逐数字对账 | 8 文档 | 13 条确认不一致（F-058..F-070） |
| 历史复发 | `git log --since="6 months ago"` 140 提交 + ITM-N36 逐条复查 | — | 4 条已登记缺陷复发或未修（F-006/F-008/F-012/F-022） |
| 数据流 | 分享 / 版本 / 备份 / 上传 / WebDAV PUT 五链逐跳 | — | 27 条（F-001..F-031 主体） |
| 并发与时序 | TOCTOU / 多用户矩阵 / 后台任务取消 / SignalR | — | 10 条（F-006/F-010/F-021 等） |
| 极端数据 | 代码级推演（未执行破坏操作） | — | 5 条（F-015/F-017/F-020 等 + 推测 3 条） |
| 部署运维 | Dockerfile / compose / nginx / entrypoint 逐行 | 4 文件 | 4 条（F-003 + 3 条低危） |

---

## 3. 发现总表

状态：**已修**（Critical，附锁定测试）/ **待裁决**（High 及以下，按协议不擅自动手）/ **推测**（无证据推测，单独标注）。

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-001 | Critical | 安全/并发 | FileService.Transfer.cs:88 | 上传遇他人占用路径先毁物理内容后遭唯一索引拒绝 | 已修 a267a44 |
| F-002 | Critical | 安全 | FileShareService.cs:330,466 | 分享子路径全库解析无归属校验，跨用户未授权读取 | 已修 ad88753 |
| F-003 | Critical | 部署/安全 | docker-compose.yml:33 | JWT 密钥 compose 默认公开字面量，绕过 fail-fast | 已修 9f3bc6d |
| F-004 | High | 安全 | FileShareService.cs:386 | 软删文件经分享仍可完整下载与浏览 | 待裁决 |
| F-005 | High | 安全/并发 | BackupDevice.cs:40 | 备份路径无用户维度，同名设备跨用户共享根 | 待裁决 |
| F-006 | High | 并发 | FileShareService.cs:354 | 下载计数读改写竞态 + MaxDownloads TOCTOU | 待裁决 |
| F-007 | High | 安全 | FileShareEndpoints.cs:82-104 | 公开分享端点零限流，口令可爆破、分享码可枚举 | 待裁决 |
| F-008 | High | 错误处理 | FileVersionService.cs:170,371 | 版本删除先删物理后删库，失败即僵尸记录 | 待裁决 |
| F-009 | High | 错误处理 | FileVersionService.cs:266-274 | 版本恢复 Delete→Move 之间有双丢崩溃窗口 | 待裁决 |
| F-010 | High | 并发 | FileVersionService.cs:77 | 版本号先读后写竞态，并发覆盖静默丢一个版本 | 待裁决 |
| F-011 | High | 安全 | FileEndpoints.cs:133 | 扫描同步任意登录用户可触发，新文件归属归触发者 | 待裁决 |
| F-012 | High | 错误处理 | FileService.Sync.cs:167 | ScanAndSync 的 catch(Exception) 无 OCE 过滤（铁律 6 复发） | 待裁决 |
| F-013 | High | 架构 | FileService.Sync.cs:183 | 目录扫描无深度上限、跟随 junction 可无限递归 | 待裁决 |
| F-014 | High | 并发 | FileService.Sync.cs:206,279 | 扫描排除用名字匹配，用户合法同名内容记录被删 | 待裁决 |
| F-015 | Medium | 架构 | FileService.Transfer.cs:198 | 覆盖上传的版本落默认池而非文件所属池 | 待裁决 |
| F-016 | Medium | 错误处理 | FileVersionService.cs:261 | 版本恢复可写回软删项，记录已消失时仍返回成功 | 待裁决 |
| F-017 | Medium | 并发 | FileVersionService.cs:267 | `.restore.tmp` 不在扫描排除清单，失败残留被当用户文件入库 | 待裁决 |
| F-018 | Medium | 安全 | FileShareService.cs:525 | 分享访问令牌不随口令轮换/分享码变更失效 | 待裁决 |
| F-019 | Medium | 安全 | FileShareService.cs:176 | Update 可把密码型分享改成永锁死，MaxDownloads 无取值校验 | 待裁决 |
| F-020 | Medium | 安全 | ZipDirectoryResult.cs:51 | 匿名 ZIP 下载无条目数/体积上限 | 待裁决 |
| F-021 | Medium | 并发 | FileService.Transfer.cs:415 | Move 库提交失败不回滚物理移动 | 已修 0d9f86f |
| F-022 | Medium | 错误处理 | FileService.Transfer.cs:394 | Move/Copy 裸 catch 静默跳过，无日志无失败项 | 待裁决 |
| F-023 | Medium | 安全 | BackupService.cs:171 | sync-complete 不校验设备吊销，吊销语义被旁路 | 待裁决 |
| F-024 | Low | 架构 | BackupService.cs:206 | NormalizeRelativePath 注释声称拒盘符，代码未实现 | 待裁决 |
| F-025 | Low | 文档 | BackupService.cs:115 | 「文件夹按需创建」与上传侧父目录必须存在的实现矛盾 | 待裁决 |
| F-026 | Low | 架构 | FileVersionStore.cs:50 | 硬链接语义前提（无原地写）当前成立但无机械守护 | 待裁决 |
| F-027 | Low | 架构 | Application 层 | Application 裸 IO 继续新增（G16 FAIL 的实质） | 已修 01b91a9（归零，基线 21→0） |
| F-028 | Low | 架构 | 服务层 | 硬编码 UtcNow 继续新增（G15 FAIL 的实质） | 已修 01b91a9（52→33，基线 35→33） |
| F-029 | Medium | 可观测性 | gate-check.sh G7 | 门禁红色：G7 两处均为误报，真违规被噪音淹没 | 待裁决 |
| F-030 | Medium | 安全 | Directory.Packages.props | ImageSharp 带 5 条已知漏洞（NU1902×2 / NU1903×3） | 已修 1166b46（钉 4.1.3） |
| F-031 | Medium | 并发 | FileService.Transfer.cs:224-255 | 上传 File.Move 后库提交失败，物理已替换而记录未更新 | 已修 01b91a9 |
| F-032 | Medium | 前端 | Dashboard.vue:403-432 | visibilitychange 恢复不校验开关，关闭的自动刷新被静默重建 | 待裁决 |
| F-033 | Medium | 前端 | Files.vue:73,143 | 搜索防抖定时器无卸载清理，离开页面仍发请求写状态 | 待裁决 |
| F-034 | Medium | 前端 | signalr.ts:8-45 | 无 stop() 出口，登出后连接不释放；startPromise 双重置路径竞态 | 已修 39b9976 |
| F-035 | Medium | 前端 | Media.vue:373-399,177 | 图片/音频预览 objectURL 无卸载释放 | 待裁决 |
| F-036 | High | 前端 | Media.vue:253-283 | loadMore 无请求序号闸，快切 tab 旧回包覆盖新状态且无自愈 | 待裁决 |
| F-037 | Low | 前端 | Media.vue:214-233 | 哨兵 IntersectionObserver 切 tab 有一帧无观察者窗口 | 待裁决 |
| F-038 | Medium | 前端 | Files.vue:1590-1624 | resubscribe 无互斥，快速切目录泄漏服务端组成员与 handler | 待裁决 |
| F-039 | Medium | 前端 | Dashboard.vue:448-484 | 订阅 promise 卸载后 resolve，清理函数丢失 + 卸载后发 5 请求 | 待裁决 |
| F-040 | Medium | 前端 | SystemConfig.vue:67,106 | 轮询 3s/2s 违反 §7.5（≥30s）且无后台暂停 | 待裁决 |
| F-041 | Low | 前端 | api/file.ts:16 | dirContents/dirEtags 无上限无淘汰，深浏览内存单调增长 | 待裁决 |
| F-042 | Low | 前端 | theme.ts:49 | matchMedia 监听只加不减（应用级单例，低危） | 待裁决 |
| F-043 | Low | 前端 | main.ts:42 / sw.js:30 | SW load 监听无移除；缓存只增不减无淘汰 | 待裁决 |
| F-044 | Medium | 前端 | api/http.ts（根因） | 20 处 as any 根因：AxiosResponse 标注与拦截器脱壳不符 | 已修 39b9976 |
| F-045 | Low | 前端 | router/index.ts | 懒加载 chunk 失败无 onError 兜底，发版后旧会话假死 | 待裁决 |
| F-046 | 无（干净通过） | 前端 | 全前端 | v-html/innerHTML 零命中，不可信内容全走文本插值 | 已确认无缺陷 |
| F-047 | 无（干净通过） | 前端 | 全前端 | 业务代码 @ts-ignore/eslint-disable 零命中（仅生成物） | 已确认无缺陷 |
| F-048 | 推测 | 前端 | signalr.ts:24 | withAutomaticReconnect() 无参默认策略的终态行为未实测 | 推测 |
| F-049 | Medium | 架构 | appsettings.json:35 | Thumbnails:DefaultWidth/Height 死键，改不生效 | 待裁决 |
| F-050 | Medium | 文档 | ROADMAP.md:288 | appsettings 清单与代码事实不符（CORS/上传上限） | 待裁决 |
| F-051 | Low | 架构 | DependencyInjection.cs:121 | 5 个配置节代码读取但零声明，发现成本高 | 待裁决 |
| F-052 | Low | 文档 | ROADMAP.md:303 | /system/health 豁免理由不成立（Docker 实际用 /health） | 待裁决 |
| F-053 | Low | 文档 | ROADMAP.md:302 | thumbnails/supported 豁免陈旧（已有前端调用方） | 待裁决 |
| F-054 | Low | 架构 | FileVersionEndpoints.cs:31 | GET /file-versions/{id}/{n} 无调用方未登记 | 待裁决 |
| F-055 | Low | 架构 | FileShareEndpoints.cs:37 | GET /file-shares/{id} 无调用方未登记（第三轮有判第四轮漏登） | 待裁决 |
| F-056 | Low | 架构 | SnapshotEndpoints.cs:33 | GET /snapshots/{id} 无调用方未登记 | 待裁决 |
| F-057 | Low | 架构 | ServiceCollectionExtensions.cs:205 | /api/version 无消费者且硬编码版本号 | 待裁决 |
| F-058 | Medium | 文档 | API_REFERENCE.md:1129-1146 | 错误码表 18 行中 13 行与代码不符（12 个虚构码 + 1 个状态码错） | 待裁决 |
| F-059 | High | 文档 | API_REFERENCE.md 全文 | 35 个现存端点未文档化（覆盖仅 66%） | 待裁决 |
| F-060 | Medium | 文档 | CONVENTIONS.md:159 | 裸 IO 基线 30 与唯一真源 21 三方矛盾 | 待裁决 |
| F-061 | Medium | 文档 | ROADMAP.md:262 | 验证基线 223/178/136 与实测 273/199/183 差 +50/+21/+47 | 待裁决 |
| F-062 | Medium | 文档 | ROADMAP.md:321 | 「覆盖全部 44 个手捏码」两个实测口径都不符（表 37 / 全集 55） | 待裁决 |
| F-063 | Low | 文档 | .ai/README.md 4 处 | 自称 NAS-G1..G20，实际 G1..G22 | 待裁决 |
| F-064 | Low | 文档 | lessons.md:49 等 3 处 | 自称 ITM-N1..N8，实际登记 36 条 | 待裁决 |
| F-065 | Low | 文档 | .ai/README.md 2 处 | 自称 NPD-1..5，实际 6 条 | 待裁决 |
| F-066 | Low | 文档 | .ai/README.md:61 | 自称 tech-debt 12 项，实际 13 项 | 待裁决 |
| F-067 | Low | 文档 | .ai/README.md:102 | 自称测试栈 200 方法，实测 470 | 待裁决 |
| F-068 | Low | 文档 | README.md:190-205 | 配置示例键名/结构与 appsettings 不符，且示范明文密钥形态 | 待裁决 |
| F-069 | Low | 文档 | DESIGN.md:91 | TypeScript 5.x，实测 6.0.3 | 待裁决 |
| F-070 | Low | 文档 | TESTING.md:17-35 | 测试项目结构树漏列 51 个文件中的 45+ | 待裁决 |
| F-071 | 推测→已查证 | 后端 | FileItems.Path collation | PRAGMA 实测无显式 collation（SQLite TEXT 默认 BINARY，大小写敏感） | 已修 0d9f86f（锁定测试） |
| F-072 | 推测 | 后端 | WebDavEndpoints.cs:145 | 锁定按 IP 计数，反代后 RemoteIpAddress 取不到时全局共享一个失败桶 | 推测 |
| F-073 | 推测 | 后端 | FileShareService | 分享码枚举实际吞吐未实测，威胁程度取决于速率 | 推测 |

---

## 4. 发现完整论证

### 4.1 已修 Critical（三条，均附修复前红修复后绿锁定测试）

#### F-001 上传遇他人占用路径：先毁物理内容、后遭唯一索引拒绝

- 严重级：Critical ｜ 维度：安全 / 并发 ｜ 状态：已修（commit a267a44）
- 证据链：`src/NasSystem.Application/Files/FileService.Transfer.cs:85-91` 原实现把属他人的既有记录置 null 后按新文件走完整管线；`:202` `File.Move(tempPath, physicalPath, overwrite: true)` 无条件替换；`:234-237` 库插入撞 `UX_FileItems_Path_Active` 唯一部分索引后返回 Failure。物理路径与虚拟路径同构的锚点：`FileService.Core.cs:283-287` `GetPhysicalPath` 无用户前缀。
- 调用方追溯：网页上传 `POST /api/v1/files/upload`（任意认证用户）与 WebDAV `PUT`（`WebDavEndpoints.cs:393-437`，Overwrite=true 走同一服务入口）。
- 机理：隔离策略「按不存在处理」只处理了 DB 判定，没处理物理写。写盘先于冲突判定的顺序，把应用层查重失效（本就依赖唯一索引兜底）放大成数据销毁。与 ITM-N7「并发正确性靠数据库约束兜底」同族：约束兜住了库，没兜住盘。
- 触发条件：用户 B 向用户 A 已占用的根级路径上传同名文件（overwrite 任意取值均可触发，该分支根本不过 overwrite 检查）。
- 最坏情况：A 的文件内容被静默替换为 B 的字节，A 的库记录保留旧大小/哈希，A 下载到的是 B 的内容；B 侧只看到一个 409。多用户部署下一条上传请求即可毁他人数据，且受害者无感知。
- 为什么现有测试没抓到：`CrossUserIsolationTests` 锁了浏览/下载/改名/删除四条链路，上传这条写路径没有对应用例；单用户上传测试天然不触发归属分支。
- 自我反驳：若物理路径按用户隔离则不成立——但 `GetPhysicalPath` 证明路径空间全局共享；若上游有父目录归属校验挡住则不成立——根目录（destPath="/"）无父级可校验，攻击在根级必然可达。
- 最小修复：路径被他人占用即返回 `FileErrors.NotFound`，拒绝发生在任何 IO 之前（已实施）。
- 验证方式：`CrossUserIsolationTests.Upload_ToOtherUsersPath_DoesNotReplaceTheirPhysicalContent`——修复前红（实测物理内容变为 "attacker payload"），修复后绿。
- 回归风险：依赖「他人路径按不存在」语义的调用方需确认无合法场景（已核：Move/Copy/Rename/Delete 均已有自己的归属校验，不受影响）；集成 273 + 单元 199 全绿。
- 静默控制问句：有差异。修复前 B 收到 409 而 A 的数据已变，系统对外输出（双方看到的响应）完全不反映磁盘上发生的替换；修复后双方都得到一致的 NotFound。

#### F-002 分享子路径按全库路径解析、无归属校验（跨用户未授权读取）

- 严重级：Critical ｜ 维度：安全 ｜ 状态：已修（commit ad88753）
- 证据链：`FileShareService.cs:324-337`（下载）与 `:462-470`（浏览）：subPath 经 `ResolveShareSubPathAsync`（`:397-408`）只做规范化 + `IsUnder` 前缀校验，随后 `fileItemRepository.GetByPathAsync(resolved)` 全库匹配。`FileItemRepository.cs:30-38` 的 GetByPathAsync 只有 `Path = x AND IsDeleted = 0`，无 OwnerId 条件。
- 数据流：匿名请求 → 分享码取 share → ValidateAccess → 前缀校验通过 → 全库按路径取目标 → 直接开流返回。软删记录不占唯一索引（`UX_FileItems_Path_Active WHERE IsDeleted=0`），路径可被他人重建（`CreateFolderAsync` 的 `PathExistsAsync` 滤已删）。
- 攻击链：A 分享 `/sroot` → A 软删让出路径 → B 建同名 `/sroot` 并放 `secret.txt` → 匿名 `GET /api/v1/public-shares/{A的code}/download/secret.txt` 读到 B 的文件。
- 机理：ITM-N16 修复补上了「不许跳出前缀」，没回答「这个前缀今天还属分享者吗」。授权锚在分享时刻的路径身份，解析却在当前全局路径空间。
- 触发条件：多用户部署 + 分享者删除被分享目录 + 他人用同名顶层路径重建（Documents/Backups/照片这类名碰撞概率不低）。
- 最坏情况：匿名（或任一登录用户，认证组同路）读取任意他人文件，受害者无感知，下载计数记在别人分享上，日志线索也是错的。
- 为什么现有测试没抓到：N16 的锁定测试只覆盖 `../` 逃逸；分享测试全是单用户视角；跨用户隔离测试没碰分享域。
- 自我反驳：单用户部署不可触发；分享者不删目录不可触发。均属条件触发而非设计免疫，且多用户是项目明示支持的能力（角色权限、配额、隔离测试）。
- 最小修复：子路径目标加载后校验 `target.OwnerId == fileItem.OwnerId`，不一致按不存在拒绝；下载与浏览两条路径同修（已实施）。
- 验证方式：`ShareSubPathOwnershipTests.DownloadShareSubPath_AfterPathReusedByOtherUser_IsRejected`——S3 反向验证：临时移除归属校验后测试转红（`download.IsFailure` 为 False，攻击成功），恢复后绿。
- 回归风险：合法场景（分享者未删、路径未复用）行为不变；分享自己的文件不受影响；集成 273 全绿。
- 静默控制问句：有差异。修复前匿名请求拿到 200 + 他人文件字节，与「分享已失效」的预期完全相反；修复后是明确的 404。

#### F-003 compose 默认 JWT 密钥公开字面量，绕过 fail-fast

- 严重级：Critical ｜ 维度：部署 / 安全 ｜ 状态：已修（commit 9f3bc6d）
- 证据链：`docker-compose.yml:33` 原为 `Jwt__SecretKey=${JWT_SECRET:-your-super-secret-key-at-least-32-characters-long}`；应用层 fail-fast 只拒空值与 `<SET-VIA-ENV>`（`AuthenticationExtensions.cs:35`），该字面量两者都不是。
- 机理：ITM-N1「配置缺失时的默认值兜底是反模式，应 fail-fast」在部署层复发。文件自身注释阐明了键名写错的后果（容器反复重启），却保留了默认值——作者预见了拼写错误，没预见省略变量。
- 触发条件：`docker compose up -d` 前未设 `JWT_SECRET`（README 环境变量表把它列为必填，但 compose 层不强制）。
- 最坏情况：生产实例带着一个写进公开仓库的密钥启动，任何读过该字符串的人可离线伪造任意用户（含 SuperAdmin）的 JWT，全面失守；且实例「看起来正常启动」，无任何告警。
- 为什么现有测试没抓到：门禁 NAS-G10/G11 只扫 .cs 源码字面量，不读 compose；无部署配置类测试。
- 自我反驳：若运维永远设 JWT_SECRET 则不触发——但 fail-fast 机制的存在恰恰说明设计者不信任「永远」；默认值与 fail-fast 并存是自相矛盾的配置。
- 最小修复：去掉 `:-` 回落，缺失求值为空，由应用启动期 fail-fast 明确报错（已实施，既有机制）。
- 验证方式：`DeploymentConfigGuardTests.DockerCompose_JwtSecretKey_MustNotCarryDefaultFallback`——S3 反向验证：stash 掉修复后测试转红，恢复后绿。
- 回归风险：未设 JWT_SECRET 的部署从「带病启动」变为「快速失败」，属于有意的行为收紧；README 的 wslc 手工流程本来就显式传 `Jwt__SecretKey`，不受影响。
- 静默控制问句：有差异。修复前缺失密钥与正确配置在运行期完全无法区分（都在正常服务）；修复后缺失即启动失败，运维立刻看见。

### 4.2 High（登记待裁决，按协议不擅动）

#### F-004 软删文件经分享仍可完整下载与浏览

- 证据链：`FileShareService.cs:386` `GetByIdAsync(share.FileItemId)`；`FileItemRepository.cs:808-813` 的 GetByIdAsync 不过滤 IsDeleted；软删只发 SQL 标记、物理保留（`FileService.Recycle.cs:77`）。下载（`:350` File.Exists 物理仍在即放行）与文件夹 ZIP（`CreateZipDownloadAsync` 按物理目录打包）都中。
- 机理：分享授权锚在 FileItemId，解析不论删除态。删除（进回收站）与「停止分享」在语义上被解耦，违反 §3.5 删除语义设计意图；与 ITM-N26 同族（语义交互面）。
- 触发条件：分享文件/文件夹 → 删除该文件（默认软删）→ 持链接者继续下载。
- 最坏情况：用户以为「已删除=链接失效」，内容持续可下载直到有人清空回收站；文件夹分享经 ZIP 一并泄露软删子树。
- 测试缺口：回收站测试与分享测试各自为政，无「分享×删除」交互用例（正是 ITM-N26 教训要求的组合测试）。
- 自我反驳：若产品语义是「分享链接是独立凭证，与生命周期无关」则不成立——但 UI 删除后分享仍返回 200 完整内容且无任何提示，与用户心智冲突。
- 最小修复：`LoadShareForPublicAccessAsync` 取到 fileItem 后补 `if (fileItem.IsDeleted) return FileNotFound`。
- 验证：集成测试「建文件→分享→软删→匿名下载断言 404」（修复前红）。
- 回归风险：影响所有公开/认证分享入口，需回归分享三型闭环。
- 静默控制问句：有差异。删除后分享仍 200，用户无法从任何输出得知链接还有效。

#### F-005 备份路径无用户维度：同名设备跨用户共享备份根

- 证据链：`BackupDevice.cs:40` `BackupPath => $"/Backups/{Name}"` 只由设备名决定；设备名唯一性按用户查（`BackupService.cs:44`）；diff 读全库子树不过滤归属（`BackupService.cs:117` → `FileItemRepository.cs:579-596` 的 GetSubtreeEntriesAsync 只有 Path 条件且注释明说「不论删除态」）。
- 机理：BackupPath 作为跨用户寻址键却没把 userId 纳入组成。与 lessons II.2 已登记「userId 参数接收不校验」家族同型，备份域是新的一处。
- 触发条件：多用户部署 + 两个用户给设备起相同名字（MyPhone/iPhone/手机这类默认名碰撞概率高）。
- 最坏情况：B 的 plan 回包收录 A 的全部文件相对路径+体积（orphaned 列表原样回给 B），他人文件清单泄露；同路径同体积的文件被判 upToDate，B 的备份静默缺失；B 真要传则撞 F-001 修复后的归属拒绝，永久 404，备份功能对 B 整体死掉。
- 测试缺口：备份测试单用户视角；无跨用户同名设备用例。
- 自我反驳：若承诺单用户或设备名全局唯一则不成立——但注册端点按用户查重、UI 文案写「同用户名下设备名唯一」，说明作者只在单用户内防。
- 最小修复：BackupPath 纳入用户维度（`/Backups/{userId}/{Name}`）+ 存量迁移；`GetSubtreeEntriesAsync` 调用点补归属过滤作第二道防线。
- 验证：跨用户集成测试「A/B 注册同名设备，B 的 plan 响应不含 A 的任何相对路径」。
- 回归风险：路径形态变更影响已有备份目录定位，需迁移脚本；WebDAV/网页端凡引用 /Backups/ 的地方要一并核。
- 静默控制问句：有差异。B 的 plan 回包「看起来正常」，备份缺失与清单泄露都无任何错误输出。

#### F-006 下载计数读改写竞态 + MaxDownloads TOCTOU

- 证据链：`FileShareService.cs:354-356` `share.IncrementDownloadCount()` → UpdateAsync 写全行 → SaveChangesAsync；限流判定在更早的 ValidateAccess → `FileShare.IsAccessible`（`FileShare.cs:184`），两者之间无事务、无原子增量。
- 机理：lessons II.2 明列「并发正确性靠数据库约束兜底而非应用层查重」，N7 修复表只兜了 Path/ShareCode/Username/版本号四处唯一索引，计数是第五处同族漏修（ITM-N7 实际未修）。
- 触发条件：同一分享链接被并发下载（分享群/二维码场景完全正常）。
- 最坏情况：MaxDownloads=1 的分享被并发打穿下载 N 次；计数持续偏低，限流形同虚设。
- 测试缺口：`FileShareCodeRaceTests` 只测分享码唯一冲突，无下载计数并发用例。
- 自我反驳：WAL 下写写串行，丢更新窗口毫秒级，低并发观察不到——但概率正确性缺陷不因窗口小而变成非缺陷。
- 最小修复：原子 SQL `UPDATE FileShares SET DownloadCount = DownloadCount + 1 WHERE Id = @id AND (MaxDownloads IS NULL OR DownloadCount < MaxDownloads)`，影响行数 0 即拒绝（一举修 TOCTOU 与丢更新）。
- 验证：`Task.WhenAll` 20 并发下载 MaxDownloads=1 的分享，断言成功流数=1 且 DownloadCount=1。
- 回归风险：改走裸 SQL 需与 PalORM 用法对齐（lessons XI.1 的 FormattableString 约束）；下载端点行为回归。
- 静默控制问句：有差异。计数偏低与限流被打穿都不产生任何错误输出，统计页数字长期偏低且无人知晓。

#### F-007 公开分享端点零限流：口令可在线爆破、分享码可枚举

- 证据链：限流只注册一个 "auth" 窗口且只贴登录端点（`ServiceCollectionExtensions.cs:103-113`、`UserEndpoints.cs:30`）；`MapPublicShareEndpoints`（`FileShareEndpoints.cs:82-104`）六个匿名端点全部无限流，其中 `/verify` 是口令判定端点（PBKDF2 100k 迭代，无失败计数、无锁定、无退避）。
- 机理：违反 §4.5「网络端点四件套：认证端点限流」。分享口令是用户自选弱口令高发区且无最小长度校验（`CreateFileShareRequest.Password` 无 validator，lessons II.2 记 FluentValidation 注册未执行）。
- 触发条件：攻击者持口令型分享链接，或批量扫分享码（6 字符 × 31 字母表 ≈ 8.9×10⁸，配合 GetPublicShareInfo 的 404/200 差异可枚举）。
- 最坏情况：弱口令分享被破；全站有效分享码及文件名/大小/属主显示名（PublicShareInfoDto 回显 CreatedByUserName）被枚举。
- 测试缺口：无限流断言测试；分享测试不覆盖失败锁定。
- 自我反驳：PBKDF2 单次成本拉低爆破速率、内网威胁模型较小——但「零限流+零锁定」与 §4.5 白纸黑字冲突，且 WebDAV 层已有 auth-max-failures 锁定先例（`WebDavAuthGuard`），分享域是漏装。
- 最小修复：`/verify` 与 `/download` 挂 "auth" 同款固定窗口（可按 shareCode 分区）；创建分享时校验口令最小长度。
- 验证：连续 20 次错误口令，第 11 次起断言 429。
- 回归风险：限流阈值与 Testing/Development 放宽策略对齐（登录端点已有先例）；E2E 分享流程别被误限。
- 静默控制问句：无差异（爆破成功/失败都有响应），但缺少的正是「失败可观测」——无限流即无日志无计数，被爆破无人知晓。

#### F-008 版本删除「先删物理、后删库」非原子

- 证据链：保留策略 `FileVersionService.cs:170-178`：foreach `TryDeleteSnapshot(victim.StoragePath)` 后 `_versionRepository.Delete(victim)`，最后统一 SaveChangesAsync；手动删版本 `:365-378`：File.Delete 失败只记日志，随后照常删记录 + SaveChangesAsync。
- 机理：文件系统先动、DB 后动。反向场景（Delete 成功、SaveChanges 抛 SQLITE_BUSY/磁盘错）→ 事务回滚、记录幸存 → 库里躺着 StoragePath 已不存在的版本；保留策略还有第三态：它的 SaveChanges 异常落进 `CreateAutoVersionAsync` 的 catch（`:110`），日志打「自动版本创建失败（主操作继续）」但版本其实已建成功，日志与事实相反。
- 触发条件：删版本/保留策略执行时 SaveChangesAsync 失败（busy_timeout 5s 超时、DB 损坏、磁盘满）；或删版本时文件被占用。
- 最坏情况：UI 列出版本、点恢复 → `File.Exists` false → 「版本文件已丢失」。版本作为误操作最后防线，在并发覆盖/批量清理时静默劣化；`.versions` 被扫描排除，孤儿文件永远扫不回来。
- 测试缺口：版本测试只覆盖 happy path 的创建/恢复/删除，无「DB 失败」注入用例（无 IFileSystem 抽象，注入成本高，这正是 F-027 的代价）。
- 自我反驳：SQLite 单写者 + busy_timeout 5s 下失败概率低——但失败后是永久僵尸而非可自愈态。
- 最小修复：两个方法都改成「先 SaveChangesAsync 删记录、成功后补删物理文件，物理删失败只记日志」。顺序反转后任一步失败都停留在可恢复态。
- 验证：单测——使 SaveChangesAsync 抛一次，断言「记录在则文件必在」不变量；删版本路径用占用文件（FileShare.None 打开）断言记录保留并报失败项。
- 回归风险：保留策略的批量语义（一次性 SaveChanges）要改为逐条或保持批量但调整顺序；需回归 FileVersionAutoTests。
- 静默控制问句：有差异。僵尸版本在列表里可见、可点击、恢复时报「文件已丢失」，用户以为是自己操作失误。

#### F-009 版本恢复的 Delete→Move 之间仍有双丢崩溃窗口

- 证据链：`FileVersionService.cs:266-274`：`File.Copy(version.StoragePath, tempPath)` → `if (File.Exists(targetPath)) File.Delete(targetPath)` → `File.Move(tempPath, targetPath)`。注释自述「消灭先删后拷数据丢失窗口」——那只覆盖 Copy 失败模态。
- 机理：ITM-N18 的教训原文要求「拷贝临时文件后**原子替换**」。Delete 与 Move 之间进程崩溃/断电，目标路径处于「已删未移」，原内容与恢复内容双丢。
- 触发条件：恢复过程中途崩溃/断电（窗口毫秒级，但恢复恰好在用户最焦躁的时刻发生，且大文件 Copy 后 Delete 前的窗口可观测）。
- 最坏情况：当前文件没了，版本也没回成，用户同时失去两边。
- 测试缺口：恢复测试不断言中间态；无崩溃注入手段。
- 自我反驳：窗口极短、需崩溃才触发——但 ITM-N18 的修复目标写的就是「消除该窗口」，只实现一半属于修复未闭环。
- 最小修复：`File.Replace(tempPath, targetPath, null)` 一句替换 Delete+Move 两句（NTFS 上 Replace 是原子替换原语）。
- 验证：静态检查 restore 路径不含 `File.Delete(targetPath)`；或注入式测试在 Delete 后抛异常断言原文件仍在。
- 回归风险：File.Replace 在目标不存在时的行为（需保留 exists 分支或直接 Replace 处理）；跨平台（Linux ext4 无 Replace 语义，需 renameat2 或退化为 Move overwrite）。
- 静默控制问句：有差异。崩溃后用户看到的是「文件凭空消失」，而不是一次失败的恢复。

#### F-010 版本号「先读后写」竞态：并发覆盖静默丢失一个版本

- 证据链：`FileVersionService.cs:77-78` `existing.Count > 0 ? existing.Max(v => v.VersionNumber) + 1 : 1`；唯一索引 `IX_FileVersions_FileItemId_VersionNumber` 兜底，但兜底后果是整件事被 `:110` 的 catch 吞掉：快照文件已建、记录插不进 → 只留一行 LogWarning。
- 触发条件：同一文件被两个覆盖上传/永久删除并发命中（批量同步、WebDAV 多客户端同传）。
- 最坏情况：用户看到「上传成功」，但这一次的旧内容没有版本可回。
- 测试缺口：版本测试串行；无 Task.WhenAll 并发覆盖用例（SPD-1 同型：只测顺序不测真并发）。
- 自我反驳：代码注释已自认此窗口且清掉了孤儿快照文件——但登记在案的已知窗口不等于已验证可接受，且 lessons 无对应 ITM。
- 最小修复：失败后重试一次（重新读 max + 重新生成快照名），仍败才降级。
- 验证：`Task.WhenAll` 两路并发覆盖上传同一文件，断言版本数=2（修复前为 1）。
- 回归风险：重试路径与孤儿清理的交互；需回归 FileVersionAutoTests。
- 静默控制问句：有差异。丢版本只体现为一栏 LogWarning，用户侧上传成功、版本列表少一条，无从察觉。

#### F-011 扫描同步可被任意登录用户触发，新发现文件归属是「触发者」

- 证据链：`FileEndpoints.cs:133-137` `files.MapPost("/scan", ScanAndSync).RequireAuthorization()`（非 RequireAdmin）；`FileService.Sync.cs:291-299, 217-223` 建记录时 `ownerId: userId` 是触发扫描的人。
- 机理：ITM-N14 已记「所有权归触发者」，但当时结论挂在孤儿物理文件重建上；本条是同一机理的授权面：任何普通用户扫一次全默认池，就能把池内无主文件（外部拷入、DB 恢复遗漏、上传记录丢失）据为己有。
- 触发条件：普通用户调一次 `/api/v1/files/scan`。
- 最坏情况：他人通过 SMB sidecar 或直接拷入存储卷的文件被第一个扫描者拿走归属；原属主若另有记录则撞唯一索引。
- 测试缺口：扫描测试全是管理员视角或单用户。
- 自我反驳：若部署单用户则不成立；若认为「先占先得」是可接受策略——但 ScanAndSync 是管理动作（全池遍历 + 批量删记录），挂在普通用户权限上不合理。
- 最小修复：扫描端点升 RequireAdmin；或新发现文件暂记无主由管理员认领。
- 验证：普通用户调 `/api/v1/files/scan` 断言 403。
- 回归风险：前端若有普通用户入口需一并收（ROADMAP §9 说 Files 工具栏有扫描入口）；需回归 ScanSyncTests。
- 静默控制问句：有差异。归属被悄悄转移，双方都不会收到任何通知。

#### F-012 ScanAndSync 的 catch(Exception) 无 OCE 过滤（铁律 6 复发）

- 证据链：`FileService.Sync.cs:167-170` `catch (Exception ex) { return Failure(new Error("Scan.Failed", $"扫描失败: {ex.Message}")); }`。
- 机理：直接违反 lessons.md I 章铁律 6（catch(Exception) 必带 OCE 过滤）与 ITM-030 家族。客户端断开/超时取消被翻译成业务错误码，且 ex.Message 直接回显（ITM-N10 的脱敏要求也没过）。这是铁律 6 登记「3 处复发」之后的又一次复发。
- 触发条件：扫描进行中客户端断开/取消。
- 最坏情况：取消被报成「扫描失败: The operation was canceled.」；内部异常消息回显客户端（信息泄露面）。
- 测试缺口：无取消路径测试。
- 自我反驳：扫描由用户同步触发，取消场景少——但铁律是机械规则，且 ITM-030 的教训正是「OCE 处理是 .NET 服务通用陷阱」。
- 最小修复：补 `when (ex is not OperationCanceledException)`；消息脱敏（不直接拼 ex.Message）。
- 验证：取消中的扫描请求断言不产生 Scan.Failed 错误响应。
- 回归风险：ScanSyncTests 的错误路径断言。
- 静默控制问句：有差异。取消与真失败在响应上无法区分，运维看到「扫描失败」会去查不存在的故障。

#### F-013 目录扫描无深度上限、跟随 junction 可无限递归

- 证据链：`FileService.Sync.cs:183-269` `ScanDirectoryAsync` 递归枚举 `Directory.EnumerateDirectories`，没有深度参数、没有深度上限；只有 CancellationToken。
- 机理：违反 §2.5「目录扫描用异步枚举并设深度上限」。深层目录树爆栈；Windows 上目录联接（junction）成环时无限递归（EnumerateDirectories 默认跟随再解析点）。
- 触发条件：挂载点内有 40+ 层深目录，或用户/恶意软件建自指 junction。
- 最坏情况：StackOverflowException 不可恢复，进程崩；或无限递归把磁盘 IO 打满、库被垃圾记录填满。
- 测试缺口：扫描测试用浅层临时目录。
- 自我反驳：个人 NAS 的目录深度通常有限——但 junction 环不需要深目录，一个环即可。
- 最小修复：加 maxDepth 参数（如 32），超深停止并计数上报；对 FileAttributes.ReparsePoint 跳过。
- 验证：造 40 层深目录 + 一个自指 junction，跑扫描断言不崩且有上报。
- 回归风险：合法深层目录会被截断（需上报让用户知情）；ScanSyncTests 的目录形态。
- 静默控制问句：有差异。栈溢出进程直接没，用户看到的是服务重启而非「扫描太深」。

#### F-014 扫描排除用「名字/子串匹配」：用户合法同名内容记录被删

- 证据链：`FileService.Sync.cs:206` `if (dirName == FileVersionStore.DirectoryName) continue;`、`:279` `if (fileName.Contains(".uploading-", StringComparison.Ordinal)) continue;`。这两个目录/文件因此永不进 physicalPaths，而删除集是「库里有、物理上没有」（`:98-100` activeRemoved）。
- 场景：用户在 `/docs/.versions/` 放文件（合法名字，ValidateName 只禁分隔符与 . / ..）→ 扫描跳过该目录 → 库中这些路径被判「已消失」→ BulkDeleteAsync 删记录 → 文件从 UI 消失但物理还在，且永远扫不回来。`report.uploading-2024.pdf` 同理。
- 机理：排除规则与用户可创建的合法名字空间重叠，且没有「仅当位于版本根下才排除」的作用域限定。ITM-N26 家族（语义交互面）。
- 触发条件：用户建名为 `.versions` 的目录或文件名含 `.uploading-`。
- 最坏情况：用户数据从库中静默消失（物理还在但 UI 不可见），且不可恢复。
- 测试缺口：扫描测试不覆盖排除名与用户合法名字的碰撞。
- 自我反驳：`.versions` 与 `.uploading-` 是非常规名字，普通用户不会建——但「非常规」不是「不可能」，且删除集语义决定了后果不可逆。
- 最小修复：`.versions` 只在池挂载点顶层排除（比对完整物理路径而非目录名）；`.uploading-` 改为前缀锚定到「无对应 DB 记录的临时文件」语义（如要求后缀为 32 位 hex）。
- 验证：建 `/docs/.versions/a.txt` 经 API 入库，跑扫描，断言记录仍在。
- 回归风险：上传临时文件的扫描跳过语义要同步改（当前靠 Contains）；ScanSyncTests。
- 静默控制问句：有差异。记录被删时扫描结果只报「删除 N 个文件」，用户以为是自己删的。

#### F-015 覆盖上传的自动版本落到「默认池」而非文件所属池

- 证据链：`FileService.Transfer.cs:198-199` 调 `CreateAutoVersionAsync(existingItem, defaultPool.Value.MountPath, ...)`；而 `FileService.Recycle.cs:133-136` 的永久删除路径传的是 item 所属池的挂载点。`FileVersionStore.cs:11-17` 自述「与目标文件同卷才能建硬链接」。
- 机理：文件在非默认池时，版本写进默认池的 `.versions`，`File.CreateHardLink` 跨卷抛 IOException → 降级 File.Copy（`:66`）→ 零 IO 优化失效、双倍占容；默认池卸载/迁移时这些版本随之消失，而 Restore/Delete 只认 StoragePath 字面量。
- 触发条件：多存储池部署 + 覆盖上传非默认池的文件。
- 最坏情况：版本库与数据库跨卷漂移；池迁移即丢版本且无提示。
- 测试缺口：版本测试单池环境。
- 自我反驳：单池部署（当前默认）不触发，危害随部署形态变化——但多池是明示支持的容量管理能力。
- 最小修复：Upload 侧改取 `existingItem.StoragePoolId` 对应池的 MountPath（Upload 已从 defaultPool 拿 Id，补一次按 Id 取池即可）。
- 验证：双池测试环境，覆盖上传池 2 文件，断言版本路径前缀 = 池 2 挂载点。
- 回归风险：FileVersionAutoTests 的路径断言。
- 静默控制问句：有差异。跨卷降级为复制是静默的，用户以为享有硬链接的零成本快照。

---

### 4.3 Medium（登记待裁决）

#### F-016 版本恢复可写回软删/已消失的 FileItem，记录消失时仍返回成功

- 证据链：`FileVersionService.cs:261-283` `GetByIdAsync(fileItemId)` 不过滤 IsDeleted，非空即写 `fileItem.PhysicalPath`；fileItem 为 null 时方法仍返回 Success 并把版本标成 IsCurrent。
- 机理：ITM-N9 家族（假成功比报错更危险）。对回收站里的文件点恢复，物理内容写回一个 IsDeleted=1 的记录；回收站清空后记录没了，恢复「成功」但无处可恢复。
- 触发条件：对已软删或已永久删除的文件执行版本恢复。
- 最坏情况：用户以为恢复完成，实际内容写进了回收站记录或凭空标记 IsCurrent。
- 测试缺口：恢复测试都用活动文件。
- 自我反驳：UI 上回收站文件不展示版本入口——但 API 层可达（`FileVersionEndpoints` 只校验分享归属，不校验 FileItem 状态）。
- 最小修复：`fileItem is null || fileItem.IsDeleted` 返回失败；另 `version.FileHash` 为空时 UpdateHash 被跳过（`:280-281`），改无条件同步。
- 验证：恢复已永久删除文件的版本 → 断言失败码而非 200。
- 回归风险：恢复端点行为收紧，需回归 Files.vue 版本抽屉流程。
- 静默控制问句：有差异。假成功让用户不再重试、不再检查，数据在静默中错位。

#### F-017 `.restore.tmp` 不在扫描排除清单：失败的恢复会被扫描当成用户文件入库

- 证据链：`FileVersionService.cs:267` `tempPath = targetPath + ".restore.tmp"`；扫描只排除 `.versions`（`FileService.Sync.cs:206`）与名字含 `.uploading-` 的文件（`:279`）。
- 机理：临时文件排除策略是「名字模式白名单」，新增临时文件形态没有同步进排除清单。File.Copy 成功后 Delete/Move 抛异常（目标被占用），临时文件残落在用户目录，下次扫描 newFile 入库、归触发扫描的用户所有。
- 触发条件：恢复失败一次（目标文件被占用）。
- 最坏情况：用户目录里出现一个双倍大小的 `.restore.tmp` 幽灵文件，且被库记录化。
- 测试缺口：无恢复失败 + 扫描的组合用例。
- 自我反驳：恢复失败本身少见——但 F-009/F-016 的修复会改变失败路径，正好是补排除清单的时机。
- 最小修复：排除清单与 FileVersionStore/上传临时文件命名收敛到单一常量；恢复失败路径补 `finally { TryDelete(tempPath) }`。
- 验证：制造一次恢复失败，跑扫描，断言库内不出现 `*.restore.tmp` 记录。
- 回归风险：ScanSyncTests 的排除断言。
- 静默控制问句：有差异。幽灵文件入库后用户看到的是一个陌生文件，不知道它从哪来。

#### F-018 分享访问令牌不随密码轮换/分享码变更失效

- 证据链：`GenerateAccessToken`（`:525-532`）payload 仅 `shareCode:expiration`，HMAC 复用 JWT 主密钥；`UpdateAsync` 改口令（`:156-160`）不触碰已发令牌。
- 机理：无状态令牌没有版本号/口令指纹绑定。
- 触发条件：口令经口头/截图泄露后，Owner 改口令以为止损。
- 最坏情况：24 小时窗口内旧令牌继续可下载。
- 测试缺口：令牌测试不覆盖口令变更后的旧令牌。
- 自我反驳：窗口 24h、需要口令先泄露——但「改口令即失效」是用户对密码保护分享的合理预期。
- 最小修复：payload 加入 PasswordHash 前 8 字节或分享 UpdatedAt ticks，校验时比对。
- 验证：verify 拿 token → 改口令 → 用旧 token 下载断言 401。
- 回归风险：令牌格式变更影响存量令牌（一次性失效，可接受）。
- 静默控制问句：有差异。Owner 改完口令得到「保存成功」，完全不知道旧链接还活着。

#### F-019 Update 可把密码型分享改成「永锁死」；MaxDownloads/ExpiresAt 无取值校验

- 证据链：`UpdateFileShareRequest.AccessType` 可传 Password 而 Password 为 null → `FileShareService.cs:176-181` → `FileShare.Update`（`FileShare.cs:127-128`）`if (passwordHash != null)` 不覆盖 → 哈希留空 → VerifyPasswordAsync 返回 PasswordNotRequired（`:290-291`）→ 无人能拿 token → 分享对所有人永久 403。MaxDownloads=-1 → IsDownloadLimitReached 恒真。
- 机理：ITM-N24 家族复发——「不变量必须覆盖所有写入点而非仅构造点」。Create 有「密码型必须带哈希」断言（`FileShare.cs:86-87`），Update 这条写入点没有。
- 触发条件：编辑分享时把类型改为密码保护但不填口令；或填负数下载次数。
- 最坏情况：分享变成谁都打不开的死分享，且 UI 显示正常，用户无从诊断。
- 测试缺口：Update 测试不覆盖类型×口令的交叉组合。
- 自我反驳：前端可能拦住空口令——但 API 层可达，且前端校验正是 lessons II.2 登记的「FluentValidation 注册未执行」悬空项。
- 最小修复：UpdateAsync 落地前复述 Create 的不变量；DTO 补 MaxDownloads ≥ 0、ExpiresAt 非过去校验。
- 验证：Public 分享 Update 成 Password 且不带口令 → 断言返回校验错误而非 200。
- 回归风险：Shares.vue 编辑抽屉的表单校验要同步。
- 静默控制问句：有差异。死分享在列表里显示「有效」，点开才 403。

#### F-020 匿名 ZIP 下载无条目数/体积上限

- 证据链：`ZipDirectoryResult.AddDirectoryToZipAsync`（`:51-84`）递归枚举物理目录，无条目上限、无总字节上限；入口是匿名端点。
- 机理：虽是流式（无 OOM），但单请求可长时间占用磁盘 IO 与出口带宽，且无并发上限即无准入控制。
- 触发条件：对含数万文件的分享目录请求 ZIP。
- 最坏情况：单请求打满磁盘 IO 与带宽，影响同机所有服务；匿名即可触发。
- 测试缺口：ZIP 测试用小目录。
- 自我反驳：需要对方持分享码——但分享码可枚举（F-007），匿名面即公开面。
- 最小修复：条目数/累计字节超阈值即中止并回错误码；或对该端点挂独立限流器。
- 验证：对含 5 万文件的分享目录请求 ZIP，断言有上限保护或限流生效。
- 回归风险：合法大目录打包被截断（需明确错误码）。
- 静默控制问句：有差异。无上限时请求只是「慢」，慢到超时后客户端只看到连接断开。

#### F-021 Rename/Move 物理操作先于库提交、失败无补偿

- 证据链：`RenameAsync` 先 `_fileSystemService.Move(...)`（`FileService.Transfer.cs:299`）后 `SaveChangesWithConflictGuardAsync`（`:312`）；`MoveAsync` 物理移动在 `:380`、库保存在 `:401`。
- 机理：并发改名同目标时，物理已移、DB 撞唯一索引回滚 → 盘上新位置、库里旧路径，下载 404 而文件在盘上（直到下次扫描按新物理路径重建）。ITM-N18 家族残留，触发面窄。
- 触发条件：两个用户/客户端并发把不同文件改成同一目标名。
- 最坏情况：库盘不一致，文件「消失」在 UI 但占着磁盘。
- 测试缺口：无并发改名用例。
- 自我反驳：FileSystemService.Move 用 overwrite:false，目标存在即抛异常被 catch 跳过（`:394`），物理不会互覆——但「物理成功、DB 失败」的另一半窗口仍在。
- 最小修复：物理移动失败/DB 失败时按旧路径回移（补偿），或改为 DB 先占位再物理操作。
- 验证：并发同目标改名，断言库盘一致。
- 回归风险：补偿逻辑本身的失败处理；MoveAsync 批量语义。
- 静默控制问句：有差异。用户看到改名失败，但盘上文件已经换了位置。

#### F-022 Move/Copy 裸 catch 静默跳过（吞异常部分成功家族复发）

- 证据链：`FileService.Transfer.cs:394-398`（Move）、`:593-596`（CopyFile）、`:657-660`（CopyFolder）——`catch { continue; }` / `catch { return null; }`，无日志、无 FailedItems。
- 机理：lessons II.2 模式行 1「吞异常部分成功」原文就列了「FileService.MoveAsync 单项静默跳过」，本轮确认仍在，且扩散到 Copy 两处。
- 触发条件：批量移动/复制中任一项失败（文件占用、权限）。
- 最坏情况：用户看到「移动成功 n 项」，失败的项无影无踪，也无从知道失败了几个、哪些。
- 测试缺口：批量传输测试不断言失败项可见性。
- 自我反驳：「不存在即跳过」是文档化的批量语义——但 catch(Exception) 把 IO 失败也吞进同一语义，失败与不存在不可区分。
- 最小修复：收集 FailedItems（Delete 已有 DeleteResultDto 范式可抄）；catch 补日志。
- 验证：制造一项移动失败（占用目标），断言响应含失败项明细。
- 回归风险：前端消费 results 的地方要展示失败项；Files.vue 移动/复制流程。
- 静默控制问句：有差异。静默跳过时 UI 只显示成功的那几项，用户以为全成了。

#### F-023 吊销设备不拦截 sync-complete：吊销语义被旁路

- 证据链：`ComputeSyncPlanAsync` 有 `if (device.IsRevoked) return Forbidden`（`BackupService.cs:106-112`），`RecordSyncCompleteAsync`（`:171-187`）只有归属校验，没有 IsRevoked 校验。
- 机理：端点文档自述「吊销设备……不再接受新的同步计划」（`BackupEndpoints.cs:39`），但「登记同步完成」是同批次状态写的另一半。ITM-N35 家族（同族问题修覆盖不均匀）。
- 触发条件：吊销设备后客户端仍调 sync-complete。
- 最坏情况：吊销只挡住计划、没挡住记账，LastSyncAt/统计被吊销设备继续写。
- 测试缺口：吊销测试只覆盖 plan。
- 自我反驳：记账本身无害（不传内容）——但吊销的操作名暗示的撤销强度与实际不符，且统计数字被污染。
- 最小修复：RecordSyncCompleteAsync 补同一 IsRevoked 分支。
- 验证：吊销后调 sync-complete 断言 403（修复前 200）。
- 回归风险：Backup.vue 流程回归。
- 静默控制问句：有差异。被吊销设备的「最后同步时间」持续刷新，管理界面显示设备仍在活跃。

#### F-024 NormalizeRelativePath 注释声称的能力代码没有

- 证据链：`BackupService.cs:206-207` 注释「返回 null 表示该项应跳过（空路径、纯 .. 逃逸、**含盘符等**）」，实现（`:208-234`）只处理空段、`.`、`..` 与首尾斜杠，无盘符/绝对路径判定。
- 机理：SPD-5 家族（注释与代码矛盾误导后续维护者）。实际影响低：相对路径只用于 dict key 与回包，落地路径由客户端拼 BackupPath 再走上传端点，上传侧有 NormalizePath + 归属校验，`C:/x` 只会成为怪异文件名而非逃逸。
- 最小修复：删掉注释里「含盘符」的声称，或补上判定。
- 验证：Plan_Diff 测试加一例 `C:/Windows/x` 条目，断言行为与注释一致。
- 静默控制问句：无差异（行为本身安全），但注释承诺的防护不存在，后续维护者会据此跳过加固。

#### F-025 「文件夹由上传时按需创建」与代码矛盾：备份首次同步必失败

- 证据链：`BackupService.cs:115` 注释「只收文件，文件夹由上传时按需创建」；但 `UploadFileAsync` 要求目标父目录的 DB 记录存在且归属本人（`FileService.Transfer.cs:106-111`，不存在即 NotFound）。`BackupEndpoints.cs:13-14` 同样写「目标路径 = 计划返回的 BackupPath + 相对路径」。
- 后果：客户端按文档直接把文件传到 `/Backups/MyPhone/a/b.jpg`，父 `/Backups/MyPhone/a` 无记录 → 404。客户端必须先调两次建文件夹端点，而文档没说。
- 触发条件：任何按文档实现的备份客户端首次同步嵌套路径。
- 最坏情况：备份功能对守规矩的客户端整体不可用；不守规矩（先建目录）的客户端能用，文档与实现二选一必有一方错。
- 自我反驳：若前端 PWA 备份页已内置「先建目录」逻辑则不触发——但 API 文档对第三方客户端的承诺是错的，需核实 web 端实现后裁决。
- 最小修复：要么上传侧支持按需建父目录记录（需定归属语义），要么把文档改成「先建目录再传」。
- 验证：按文档形态直接传嵌套路径，断言行为与文档一致（成功或明确错误）。
- 静默控制问句：有差异。404 至少是响亮的；真正危险的是客户端自行 workaround 后无人发现文档是错的。

#### F-026 硬链接语义的正确性前提当前成立，但无机械守护

- 证据链（正面确认）：所有写用户文件内容的路径当前都不是原地截断写——上传走 `.uploading-` 临时 + File.Move（`FileService.Transfer.cs:138,205`）、WebDAV PUT 复用同一入口、版本恢复走 temp + replace、复制建新 inode（`:571`）、OCR 只读图片写 DB（`OcrService.cs:166` SKBitmap.Decode）、缩略图写缓存目录、转码写 _cacheRoot。`FileVersionStore.cs:50-53` 的 remarks 前提为真。
- 机理：这是「当前恰好成立」而非「结构上不可能被违反」。新增任何原地写路径（OCR 回写、媒体标签写入、未来的编辑器）都会静默篡改所有历史版本内容，且没有任何测试或门禁能发现。
- 最小修复：加一条契约测试，或把 CreateSnapshot 改为「创建后立即校验版本文件 Hash 与源一致」，至少在 CI 留哨兵。
- 验证：契约测试断言 CreateSnapshot 后版本内容 Hash == 源内容 Hash。
- 静默控制问句：无差异（当前无故障），但前提被违反时所有历史版本被静默改写，版本功能整体失信且无任何输出变化。

#### F-027 Application 层裸 IO 继续新增（G16 FAIL 的实质）

- 证据链：门禁实测「当前 32 > 基线 21，新增违规 11 处」，集中在 FileVersions（FileVersionService 11 + FileVersionStore 6 = 17）、DiskSpace 4、FileService.Sync 4、FileShareService 3、Transfer 2、Core 2。
- 机理：违反 §1.5「只减不增」。版本域与分享域整体绕过 IFileSystemService 收口，后果：这些 IO 无法被测试替身替换（版本/分享单测只能落真实盘，正是 F-008/F-016 难以注入失败的原因），也无法统一加审计/重试。
- 最小修复：把上述调用下沉到 IFileSystemService（FileSystemService 里 WriteFileAsync/CopyFileAsync 目前零调用方，正好接上）。
- 验证：gate G16 计数不升；版本/分享测试可改注入替身。
- 静默控制问句：无差异（架构债），但它直接放大 F-008/F-016/F-021 的修复成本。

#### F-028 硬编码 DateTime.UtcNow 继续新增（G15 FAIL 的实质）

- 证据链：门禁实测「当前 51 > 基线 35，新增违规 16 处」：FileShare.IsExpired（`FileShare.cs:174`）、GenerateAccessToken/ValidateAccessToken（`FileShareService.cs:527,558`）、FileVersion.Create/MarkAsOld/MarkAsCurrent（`FileVersion.cs:104,114,123`）、ApplyRetentionAsync cutoff（`FileVersionService.cs:152`）、BackupDevice.RecordSync（`BackupDevice.cs:65`）等。
- 机理：违反 §5.1「只减不增」。时间源不可注入 → 过期判定、令牌过期、保留策略截止时间全部不可测试、不可回拨。lessons 记 IDateTimeProvider 曾定义但被当死代码清掉（ITM-N33），清理时没有替代方案。
- 最小修复：IDateTimeProvider 重新注入至少分享与版本两个域。
- 验证：gate G15 计数不升；过期判定可用固定时间测试。
- 静默控制问句：无差异（可测试性债），但令牌过期、保留截止这类边界行为当前无法用测试锁定。

#### F-029 项目门禁处于红色状态（G7 两处误报淹没真实信号）

- 证据链：`bash .ai/scripts/gate-check.sh --allow-dirty` 实测 exit 1，17 通过 1 警告 3 失败。NAS-G7 的 2 处命中逐条复判均为误报：`FfmpegTranscodeService.cs:121` `return cached.Result` 是元组字段访问（`_probeCache` 为 `Dictionary<string, (MediaProbeResult Result, DateTime At)>`，门禁正则无法区分 Task.Result）；`AsyncPumpStream.cs:77` 是 ZipArchive/CryptoStream 同步 dispose 的已文档化桥接（PalormSessionHolder 已获 IsCompletedSuccessfully 快路径豁免，AsyncPumpStream 没有）。
- 机理：门禁常红 → 真实违规（G15/G16 的基线超标）被当成噪音；且 main 分支带着红门禁在走，说明提交前门禁未被实际执行（无 CI，`.github/workflows` 不存在）。
- 最小修复：G7 补 AsyncPumpStream 的白名单（与 PalormSessionHolder 同形的注释豁免）；FfmpegTranscodeService 的元组字段改名 `Result` → `Probe` 消除正则误报；G15/G16 按 F-027/F-028 收敛或正式下调基线（三同步：脚本常量 + CONVENTIONS + lessons）。
- 验证：gate exit 0。
- 静默控制问句：有差异。门禁红着跑一个月后，没人再看它输出什么。

#### F-030 ImageSharp 3.1.12 带 5 条已知漏洞（NU1902×2 中危 / NU1903×3 高危）

- 证据链：构建输出 `NasSystem.UnitTests.csproj : warning NU1903: 包 "SixLabors.ImageSharp" 3.1.12 具有已知的高严重性漏洞` ×3（GHSA-j3p4-wp97-rph4 / GHSA-j9gm-c75j-xc9q / GHSA-jjfr-hcj7-qf5w）+ NU1902 ×2（中危）；`src/NasSystem.Api/bin/Debug/net11.0/` 内有 SixLabors.ImageSharp.dll，即生产产物含该包；来源是 `ElBruno.LocalEmbeddings.ImageEmbeddings 1.6.4` 的传递依赖（`dotnet list package --include-transitive` 实测）。
- 机理：lessons III.2 的 NoWn 表声称「NU1900-1904 已手动审计并升级传递依赖」，但当前传递依赖仍带 5 条 advisory。语义搜图是本项目自己探针落地的新依赖，引入时未过漏洞门。
- 触发条件：凡经 CLIP 管线处理不可信图片的路径（语义索引 `ClipEngineHolder` 对池内任意图片解码）。
- 最坏情况：取决于 advisory 具体内容（信息泄露/DoS/ RCE 需逐个查证，本轮未逐条读 CVE 详情，标待验证）；至少是供应链卫生不合格。
- 自我反驳：若升级 ElBruno 或 ImageSharp 会破坏 CLIP 预处理兼容性则不成立——需先查证有没有修好漏洞的更高版本（NuGet 实测，未查证前不下结论）。
- 最小修复：查 NuGet 上 ImageSharp 修漏洞的最低版本与 ElBruno 的兼容矩阵，能升则升；不能升则在 Directory.Packages.props 注释登记理由与风险。
- 验证：`dotnet list package --vulnerable` 零命中或每条有登记理由。
- 静默控制问句：有差异。NU19xx 只是构建警告，运行时被利用无任何应用层输出。

#### F-031 上传 File.Move 之后库提交失败：物理已替换而记录未更新

- 证据链：`FileService.Transfer.cs:202` File.Move 成功后，`:216`（覆盖分支）或 `:235-237`（新建分支）的 SaveChanges 失败时异常直接抛给全局处理器，无回滚。覆盖分支下版本快照已建（旧内容安全），但 DB 的 SizeBytes/Hash 停留在旧值；新建分支下物理文件在盘上、库无记录（孤儿文件，直到被 F-011 的扫描归给触发者）。
- 机理：「物理先于库」家族（F-021 同族）在上传链路的残留。与 F-001 的区别：F-001 是覆盖他人，F-031 是覆盖自己后库失败。
- 触发条件：File.Move 成功后 SaveChanges 失败（busy 超时、磁盘错、DB 损坏）。
- 最坏情况：库盘错位——DB 说 100 字节，盘上是 100MB；下载走 DB 记录但流的是盘上文件，ETag/缓存全部错；新建则成孤儿文件。
- 测试缺口：上传测试不注入库失败。
- 自我反驳：SaveChanges 在 Move 之后失败概率低——但后果是持续性不一致而非可自愈。
- 最小修复：Move 前先占位（插入/更新 DB 记录再物理落位），或失败路径把物理文件回滚到 temp 并上报。
- 验证：注入 SaveChanges 失败，断言库盘一致（要么都新要么都旧）。
- 静默控制问句：有差异。500 之后用户重传，旧记录配上旧文件，错位被固化。

---

### 4.4 前端（F-032..F-047，Medium/Low 摘要；证据均出自子代理逐字读取）

#### F-032 [Medium] visibilitychange 恢复不校验开关，关闭的自动刷新被静默重建
- 证据：`Dashboard.vue:432-441` `handleVisibilityChange` 在 visible 时直接 `startAutoRefresh()`，不校验 `autoRefresh.value`；`onUnmounted`（`:480`）只 remove 监听。
- 机理/触发：用户关掉自动刷新 → 切走标签页 → 切回，轮询被重建且 UI 仍显示关闭态，状态与行为不一致。
- 最坏：后台标签页定时器空转（功耗）；用户对「静止」的期望被静默违背。
- 测试缺口：dashboard.spec.ts 未 mock setInterval。
- 反驳：定时器空转开销极小；但开关状态与行为不一致是事实。
- 修复：`handleVisibilityChange` 加 `if (!autoRefresh.value) return`。
- 静默控制问句：有差异。轮询重新开始没有任何提示。

#### F-033 [Medium] 搜索防抖定时器无卸载清理
- 证据：`Files.vue:73-74` 声明、`:143` set，`onUnmounted`（`:1619-1624`）只清 SignalR 与 objectURL，grep 全文件无清理点。
- 触发：输入搜索词后 300ms 内离开 /files 页。
- 最坏：卸载后仍发一次搜索请求并写已卸载组件 ref（连续输入可把窗口拉长到任意时长）。
- 测试缺口：Files.vue 无 spec。
- 反驳：单次泄漏量级小——但「每次带搜索词离开文件页必然复现」。
- 修复：onUnmounted 补 clearTimeout。
- 静默控制问句：无差异（Vue 3 卸载后写 ref 不报错），这正是它危险的原因。

#### F-034 [Medium] signalr.ts 无 stop() 出口 + startPromise 双重置竞态
- 证据：`signalr.ts:8-9` 模块级单例，全文件无 stop()/导出关闭能力（grep `.stop()` 零命中）；`getHubConnection` 的 onreconnected 与 ensureHubStarted 是两条并行 startPromise 重置路径，交错时对已 Connected 连接重复 start() 会 reject，冒泡到 subscribeDirectory 的 catch 把状态栏打成「实时同步已断开」（假阴性）。
- 触发：登出/401/服务重启；onreconnected 与 in-flight await 精确交错（低概率分支）。
- 最坏：登出后 WebSocket 原样活着，服务端维持心跳与目录组成员直到 TCP 超时；多标签页各自一轮，服务端连接数虚高。
- 测试缺口：utils/__tests__ 无 signalr spec。
- 反驳：若服务端 token 失效主动 close 则前半不成立——但代码侧无显式 close，「靠服务端兜底」不是可控保证。
- 修复：导出 stopHubConnection()，logout 与 401 分支调用；ensureHubStarted 只信 startPromise 一条路径。
- 静默控制问句：有差异。假阴性让用户以为实时同步坏了并手动刷新。

#### F-035 [Medium] Media.vue 预览 objectURL 无卸载释放
- 证据：`Media.vue:373-399` 创建 previewUrl/audioUrl，`closePreview`（`:425-430`）是唯一 revoke 点（只由 drawer @closed 触发），`onUnmounted`（`:177-180`）只 destroyPlayer + clearManifestRetry。对照 Files.vue:1619-1624 与 AuthImage.vue:80-82 的正确写法。
- 触发：打开预览不关抽屉直接切路由。
- 最坏：每次泄漏一份 blob（音频是 MB 级整文件），N 次后媒体库「越用越卡」，刷新才回收。
- 测试缺口：无 media.spec.ts。
- 反驳：抽屉可关——但 mask-closable=false（`:641`）收窄了关闭路径，反而放大不关就导航的概率。
- 修复：onUnmounted 补 revokePreviewUrl + revokeAudioUrl。
- 静默控制问句：无差异（无报错），表现为渐进卡顿。

#### F-036 [High] Media.vue loadMore 无请求序号闸：快切 tab 旧回包覆盖新状态且无自愈
- 证据：`Media.vue:253-283` `loadMore` 唯一并发闸是 `if (loading.value) return`——挡住第二个请求发出，挡不住第一个回包。切 tab 时 watch 清空 items 并调 loadMore(true)，若 loading 仍为 true 则直接 return，B 类请求根本没发出；随后 A 类回包把 A 的数据写进 B 类视图。
- 触发：三个 tab 快速连点或连改月份，间隔小于一次 /media 请求。
- 最坏：用户切到「音频」看到图片列表，且因为请求没发出，界面**一直**停在错误数据上（再切一次才恢复）。非闪烁，是持续错误。
- 测试缺口：无 media.spec.ts。
- 反驳：本地后端几十 ms 时手速难复现——但「后端慢/首帧慢/大量媒体」正是常见场景，且不需要网络慢，只需两次点击间隔小于一次请求。
- 修复：加请求序号闸（`const seq = ++loadSeq`，回包后 `if (seq !== loadSeq) return`），watch 不依赖 loading 拦请求。
- 静默控制问句：有差异。用户看到的内容与请求历史矛盾，且没有任何错误提示。

#### F-037 [Low] 哨兵 IntersectionObserver 切 tab 一帧无观察者窗口
- 证据：`Media.vue:214-233` watch 本身有 disconnect 前置清理（配对正确），但模板 ref 置 null 的时机晚于 watch 触发，切 tab 后旧分支销毁、新哨兵未插入之间存在一帧无观察者窗口。
- 最坏：偶发漏一次滚动加载触发（有「加载更多」按钮兜底）。
- 反驳：rootMargin 200px 预加载窗口通常能吸收一帧延迟；列为低危边缘时序。
- 修复：watch 回调对 el 做 nextTick 去抖后统一 observe；或维持现状（有按钮兜底）。
- 静默控制问句：有差异（少加载一页），但有按钮兜底故影响小。

#### F-038 [Medium] Files.vue resubscribe 无互斥：快速切目录泄漏服务端组成员与 handler
- 证据：`Files.vue:1590-1624` resubscribe 是 async，`await unsubscribe?.()` 与 `await subscribeDirectory(...)` 之间无互斥/序号。连续两次调用时调用 2 的 `await unsubscribe?.()` 读到的还是 null（调用 1 未赋值）→ 跳过退订 → 两个 SubscribeToPath 都在服务端生效；随后 unsubscribe 被覆盖两次，A 的清理函数永久丢失。
- 机理：SignalR 的 conn.on 是 push 语义，handler 数组只增不减；服务端目录组成员只增不减。
- 最坏：长时间深目录浏览后，一次文件变更触发成百上千次闭包调用（多数被 `parent === currentPath` 条件过滤，故主要危害是服务端资源与调用放大，不是错误刷新）。
- 测试缺口：无 files.spec.ts / signalr.spec.ts。
- 反驳：需要两次 resubscribe 重叠（毫秒到百毫秒窗口）；危害被条件过滤掉大部分——正确定位是「确认的订阅泄漏 + 潜在回调放大」。
- 修复：resubscribe 加序号闸，回包后seq 不符则立即 cleanup 返回。
- 静默控制问句：有差异。泄漏的服务端组成员无任何输出，只有资源曲线缓慢劣化。

#### F-039 [Medium] Dashboard 订阅 promise 卸载后 resolve：清理函数丢失 + 卸载后发 5 请求
- 证据：`Dashboard.vue:448-484` `subscribeAllChanges(...).then(cleanup => { unsubscribeGlobal = cleanup; ... })`；若在 resolve 前卸载，onUnmounted 的 `void unsubscribeGlobal?.()` 读到 null → 退订跳过；resolve 后赋值到已卸载组件的闭包变量，无人再调用。防抖回调在 resolve 后、卸载前那 1s 内收到事件，则卸载后触发 5 个统计请求并写 6 个已卸载 ref。
- 触发：进入 Dashboard 后订阅建立前离开（首屏冷连接窗口可达秒级；每次刷新后第一次进 Dashboard 都在这个窗口里）。
- 最坏：SubscribeToAll 服务端组成员 + conn.on handler 永久泄漏；用户已离开页面，服务端仍在为其推送并消耗 5 请求/次。
- 测试缺口：dashboard.spec.ts 已 mock subscribeAllChanges 但用立即 resolve 的 Promise，未测 deferred + unmount。
- 反驳：正常浏览不会进页面就立刻切走——但弱网/冷连接下窗口被拉长，且「刷新后首次进入」人人经过。
- 修复：onMounted 记 disposed 标记，`.then` 里 disposed 则立即 cleanup。
- 静默控制问句：有差异。用户离开后服务端仍在为其工作，无任何界面可观察。

#### F-040 [Medium] SystemConfig 两处轮询 3s/2s，违反 §7.5 且无后台暂停
- 证据：`SystemConfig.vue:77` 3000ms、`:119` 2000ms；均无 visibilityState 检查（§7.5 原文「轮询间隔 ≥30s 且后台标签页暂停」）。Dashboard 的 60000ms 合规。
- 机理：语义索引进度无 SignalR 推送通道（后端未提供），前端只能轮询；但「后台不暂停」这一条完全缺失，浏览器 clamp 到约 1 次/秒。
- 最坏：模型下载约 606MB、10 分钟量级 → 200~300 次请求；配置页挂后台时持续打 /semantic/status（内部两次存储/索引读）。
- 反驳：进度条需要 2-3s 粒度，拉到 30s 可用性受损；索引/下载结束即 stop、onUnmounted 双清，不是永久泄漏——违反规范的事实成立，但是否豁免需项目裁决。
- 修复：(a) 给语义进度加 SignalR 推送（§7.5 首选）；(b) 保留轮询但加 visibility 闸 + 注释显式引用豁免理由。
- 静默控制问句：有差异。后台请求无任何用户可观察输出，纯消耗。

#### F-041 [Low] api/file.ts 目录缓存无上限无淘汰
- 证据：`api/file.ts:16-17` `dirEtags`/`dirContents` 两个模块级 Map 只 set 不 delete；每 entry 缓存整个 DirectoryContent（含全量 items）；且 store 与缓存共享同一对象引用（`stores/file.ts:76`）。
- 触发：同一会话浏览大量不同目录，或反复开关「显示隐藏文件」（每目录占 2 个 key）。
- 最坏：长时间不刷页的管理会话内存单调增长（几百 KB 到 MB 每目录量级）。
- 反驳：一次会话浏览目录数通常有限，量级可能仅几 MB——「无上限」是事实，量级待实测。
- 修复：改 LRU（50-100 条目，Map 插入序即可实现）。
- 静默控制问句：无差异，表现为渐进内存增长。

#### F-042 [Low] theme.ts matchMedia 监听只加不减
- 证据：`theme.ts:49-62` addEventListener 无 remove、无句柄保存；每次调用 initTheme 都新建 MediaQueryList 挂监听。生产只调一次（main.ts:31），实际 1 个常驻监听。
- 反驳：应用级单例监听「只加不减」在多数库是刻意设计，凑数嫌疑——报告事实与配对缺失，定性低危；对照 useIsMobile.ts 正确在 onUnmounted remove，说明项目有此意识。
- 修复：保存句柄 + 导出 disposeThemeWatcher() 供测试/HMR；或注释声明「故意不卸载」。
- 静默控制问句：无差异（当前无累积）。

#### F-043 [Low] main.ts SW load 监听无移除 + sw.js 缓存只增不减
- 证据：`main.ts:42-49` window load 监听无 remove（load 单次触发，实际不累积）；`sw.js:30-58` `cache.put(request, clone)` 对每个同源 GET 无限写入同一 CACHE_NAME，activate 只删名字不匹配的旧 cache，不清理当前 cache 内过期条目——旧哈希名条目永久滞留。
- 触发：每次前端发版新增/改名带哈希资产。
- 最坏：Cache Storage 单调增长，超 origin 配额时可能连带驱逐当前需要的资产。
- 反驳：若发版流程会 bump CACHE_NAME（nas-shell-v1）则 activate 的旧 cache 删除兜住大部分增长——grep 未见构建脚本改写 CACHE_NAME，标待实测。
- 修复：activate 对当前 cache 做「仅保留 N 个最近使用」清理，或引入带构建哈希的 CACHE_NAME；load 监听自注销。
- 静默控制问句：有差异。缓存劣化只在弱网/离线时暴露。

#### F-044 [Medium] 20 处 as any 的根因：AxiosResponse 标注与拦截器脱壳不符
- 证据：`api/http.ts:57` 拦截器 `return response.data`（脱壳成 ApiResponse<T>），但各 api 方法签名仍声明 `Promise<AxiosResponse<T>>`，调用点被迫 as any：Dashboard.vue 11 处（:190,202,217,242,254,266,278,290,302,317,332）、Storage.vue 6 处、SystemInfo 2 处、Files.vue:602。el-table 行型收窄 6 处与 as unknown as 3 处合理（有注释），不计入。
- 机理：类型系统对 Dashboard 全部 9 个数据卡 + 存储页全部写操作失明。后端把 usagePercentage 改名，前端仍编译通过、运行时报 undefined 或渲染 NaN。
- 测试缺口：type-check 当前靠 as any 通过，无信号。
- 反驳：修复涉及 13 个 api 文件的机械改动，工作量大——但正是「机械」所以可做。
- 修复：给每个 api 方法显式标注 `Promise<ApiResponse<T>>` 并删调用点 as any，vue-tsc 兜底；可先加 grep CI 卡新增 as any。
- 静默控制问句：有差异。DTO 重构后静默渲染 NaN，用户看到坏数据无任何错误。

#### F-045 [Low] 懒加载 chunk 失败无 onError 兜底
- 证据：`router/index.ts` 20 条路由全部懒加载；grep onError/errorHandler/unhandledrejection 在业务代码零命中。import() reject 变成未捕获 rejection，beforeEach 已 NProgress.start() 但 afterEach 不执行 → 进度条永久 90%，页面停旧视图。
- 触发：发版后旧 tag chunk 被删/改名，用户持旧页面点击导航；弱网下动态 import 失败。
- 最坏：发版后旧会话 100% 复现假死（SPA 懒加载经典事故），SW 放大「旧 shell + 新 chunk 名」错配概率。
- 反驳：SW 网络优先使 HTML 总是新的，窗口主要是部署进行中——概率不高但后果是全站不可用，修复几行。
- 修复：`router.onError` 里对 chunk 失败做 location.reload()；app.config.errorHandler 至少 NProgress.done() + ElMessage。
- 静默控制问句：有差异。用户看到「点了菜单没反应」。

#### F-046 / F-047 干净通过（零命中记录）
- F-046：`grep -rn "v-html\|innerHTML\|outerHTML\|insertAdjacentHTML\|document.write\|eval(\|new Function" web/src web/public` 命中 0。所有不可信内容（OCR 文字、文件文本预览、分享页文件名）走文本插值，天然转义。
- F-047：业务代码 `@ts-ignore`/`@ts-expect-error`/`eslint-disable` 零命中（仅 auto-imports.d.ts/components.d.ts 两个生成物）；附带观察：web/ 无 .oxlintrc.json，跑的是默认规则集，as any 不会被默认规则拦住（与 F-044 互为因果）。

---

### 4.5 配置与端点（F-049..F-057）

#### F-049 [Medium] Thumbnails:DefaultWidth/DefaultHeight 死键
- 证据：`appsettings.json:35-36` 声明；`IThumbnailService.cs:64-65` 属性存在；全仓 grep 零消费方；实际默认值硬编码在 `ThumbnailEndpoints.cs:67-68` `width ?? 200`（端点不注入 IOptions<ThumbnailOptions>）。同节 MaxWidth/MaxHeight/JpegQuality 都被真实消费，唯 Default* 断裂。
- 机理：违反 ROADMAP §9「不留声明了但不生效的死键」。同节 JpegQuality 刚修过同类漂移（IThumbnailService.cs:69-70 注释自述），Default* 是遗漏而非设计。
- 触发：运维按文档改 DefaultWidth=400，重启后无任何变化也无报错。
- 反驳：「IOptions 绑定保留无害」——但同节其他键都被消费，声明-消费断裂是事实。
- 修复：删两行 + 删两属性，端点常量提取；或端点注入 IOptions 消费。倾向前者（前端永远显式传 size）。
- 验证：修复后 grep "DefaultWidth" 在 src 消失。
- 静默控制问句：有差异。配置静默失效，运维以为调大了尺寸。

#### F-050 [Medium] ROADMAP §9 对 appsettings 清单的描述与代码事实不符
- 证据：`ROADMAP.md:288` 称 appsettings 保留「连接串/JWT/CORS/上传上限/缩略图」；实际 CORS 不在 appsettings（`ServiceCollectionExtensions.cs:173` 读 Cors:AllowedOrigins，空则全禁 `:176-177`），上传上限是代码常量（`UploadLimit.cs:15` 500MB）。
- 机理：文档把「env-only 的安全默认」写成 appsettings 项，导致 CORS 开关发现成本极高，后续审计会重复误判。
- 反驳：「env-only 是有意的安全默认」——若有意，ROADMAP 不应写进 appsettings 清单，且 appsettings 里连占位注释都没有。
- 修复：appsettings 补 `"Cors": { "AllowedOrigins": [] }` 占位 + 注释，或改 ROADMAP 措辞。
- 静默控制问句：无差异（文档问题），但误导审计方向。

#### F-051 [Low] 5 个配置节代码读取但零声明
- 证据：Semantic:ModelDirectory/ModelMirror（`SemanticSearchService.cs:171-178`）、Semantic:VecLibraryPath（`DependencyInjection.cs:159`）、Transcode:CacheDirectory/FfmpegPath（`FfmpegTranscodeService.cs:494-501`）、NasSystem:StoragePath/AdminPassword（`DependencyInjection.cs:314,324`）均有代码默认值，appsettings 与 compose 注释清单均无声明。
- 机理：违反「appsettings 放启动期定死项」的可发现性；配错时静默回落（VecLibraryPath 配错只运行时暴露）。
- 反驳：默认值覆盖全部部署形态——但 docker-compose 已示范 env 注入，同类部署强相关项不进任何文档属不对称。
- 修复：appsettings 以占位形式集中声明，或 docs 补「配置节速查表」。
- 静默控制问句：有差异。配错路径的降级是静默的。

#### F-052..F-057 孤儿端点与豁免账实不符（Low，五条同型）
- F-052 `/system/health`：ROADMAP:303 的豁免理由「Docker healthcheck 消费」不成立——Dockerfile:126 与 compose:39 实际打 `/health`（`MapHealthChecks`）；`/system/health` 本体只有 Benchmark 消费。豁免清单失信。
- F-053 `thumbnails/supported`：豁免理由「前端直连 img，预检反增请求」陈旧——`AuthImage.vue:67` 已有调用方。应移出豁免清单。
- F-054 `GET /file-versions/{id}/{n}`、F-055 `GET /file-shares/{id}`（第三轮有判定、第四轮清单漏登）、F-056 `GET /snapshots/{id}`：均无调用方且不在豁免清单，违反 ROADMAP「无 API-only 遗留」声明。性质均为「能力冗余」（列表数据已全量）而非用户能力缺口。
- F-057 `/api/version`：无任何消费者且返回硬编码 `Version = "1.0.0"`（`ServiceCollectionExtensions.cs:208`），与真实程序集版本可能漂移而无人发现；Settings.vue 已改用 /system/info。
- 统一修复：按「补登豁免（理由写实）或删端点」二选一处理；/dav* 协议面建议显式补登（OS 客户端消费，认证在 handler 内）。
- 验证：删除后前端 183 例全绿 + 构建通过；保留则 ROADMAP:303 清单补齐。
- 静默控制问句：无差异（冗余端点无害），但豁免清单失信会持续误导后续收口审计。

---

### 4.6 文档数字漂移（F-058..F-070，13 条同族）

#### F-058 [Medium] API_REFERENCE 错误码表 18 行中 13 行与代码不符
- 证据：`API_REFERENCE.md:1129-1146`。逐码全 src 引号精确匹配：12 个码零命中（Auth.InvalidCredentials/TokenExpired/Unauthorized、User.NotFound/AlreadyExists、File.AccessDenied、Share.NotFound/Expired/DownloadLimitReached/InvalidPassword、Storage.NotFound、Validation.InvalidInput——文档臆造了 Auth.*/Share.* 两个不存在的命名空间，真实命名空间是 User.*/FileShare.*/File.*）；1 个状态码错（User.CannotDeleteSuperAdmin 文档 403，`ResultExtensions.cs:66-67` 实映射 400）。仅 5 行属实。
- 机理：表为初始版本手写，从未与 ClientErrorStatuses（显式表 37 条）或实际码集合（src 去重 55 个）对账。
- 最坏：对外 API 文档教授客户端处理 13 个不存在的错误码；按文档断言 403 的测试会红；真实故障（Scan.Failed 落 500）反而无文档可查。
- 反驳：已对 12 个码做全 src 引号匹配零命中，并验证检索方法有效（User.InvalidCredentials 等有 2 处命中），排除「码在别处定义」。
- 修复：整表从 ResultExtensions 源码机械生成，删虚构码，修正状态码。
- 验证：脚本抽字典键与文档表做 diff，集合相等。
- 静默控制问句：有差异。客户端等 Share.Expired 永远等不到，错误分支静默失效。

#### F-059 [High] 35 个现存端点未文档化（覆盖仅 66%）
- 证据：API_REFERENCE 逐条核对 67 个文档端点全部真实存在（写了但不存在 = 0），但现存 102 条路由中 35 个未入文档：backup 5、exports 1、protected-directories 3、snapshots 6、media 2、media/stream 3、system 6、system/config 2、users/stats 1、files 4、WebDAV 2。DESIGN.md:420-429 §5.1 更只列 8 个基础路径，缺 8 组。
- 机理：API_REFERENCE 定格 2026-01-29，ROADMAP Phase 0-3 全部新端点无一进文档。
- 最坏：按文档做客户端开发的用户无法发现备份/快照/导出/WebDAV 能力；ROADMAP §9 的「API-only 判定」在缺少文档的面上进行。
- 修复：按 Endpoints 目录补齐 8 个章节（含 WebDAV 协议面专章）；DESIGN §5.1 补 8 行。
- 验证：脚本从 Endpoints/*.cs 抽 (verb, pattern) 与文档双向 diff，差集为空。
- 静默控制问句：无差异（文档缺失），但能力不可发现等于不存在。

#### F-060 [Medium] CONVENTIONS §9「裸 IO 基线 30」与唯一真源 21 三方矛盾
- 证据：`CONVENTIONS.md:159`「裸 IO 30」；同文件 §1.5（:35）写 21；`gate-check.sh` BASELINE_RAW_IO=21；门禁实跑「当前 32 > 基线 21」。错误数字已传播三处（CONVENTIONS §9、gate-check.sh 头部注释「互相锚定（UtcNow 35 / 裸 IO 30 / 测试命名 6）」及 G16 段注释「基线 ≤30」、.ai/README.md:37）。
- 机理：21 是 2026-08-25 白名单口径重定标值；30 无任何出处（既非宽口径 31 也非 21，疑似 31 误传）。直接违反该文件 §9 自我声称的「单一来源」。
- 最坏：按 30 执行基线治理会得出「只超 2 处」的错误结论（实际超 11 处）。
- 反驳：脚本常量与 §1.5 均为 21 且门禁以 21 判定，证明 30 是纯文档错误而非口径差异。
- 修复：CONVENTIONS:159 改 21；同步 gate-check.sh 两处注释与 .ai/README.md:37。
- 静默控制问句：无差异（文档），但基线台账失真。

#### F-061 [Medium] ROADMAP 验证基线 223/178/136 与实测 273/199/183
- 证据：`ROADMAP.md:262`「集成 223 + 单元 178 + 前端 136 全绿」；实测集成 273（含本轮 3 条新增锁定测试）、单元 199、前端 183/26。该句是 §8 唯一不带轮次日期的总述句，同段混合 2026-10-21 媒体库补记，读者会合理推断它是当前状态。
- 最坏：「全绿」结论被沿用到包含大量新代码的当前树上，掩盖新增测试与门禁三 FAIL 并存的事实——文档暗示一切达标，实测门禁 exit 1。
- 反驳：同段其余内容确实是当前架构，唯数字未更新，排除「整段都是历史存档」。
- 修复：改为带日期的当前值，并注明门禁 3 项 FAIL 待清。
- 静默控制问句：有差异。文档全绿与门禁红色并存，读者无法从文档得知门禁状态。

#### F-062 [Medium] ROADMAP「覆盖全部 44 个手捏码」两个实测口径都不符
- 证据：`ROADMAP.md:321`；实测 ClientErrorStatuses 显式表 37 条、src 去重错误码全集 55 个。「44」既非表内也非全集；若意指客户端错误码全集，表外尚有 Backup.DeviceRevoked、Ocr.NotAnImage 等客户端性质码。
- 机理：数字无出处。按「全覆盖」结论会停止登记新码——新增码不进表落 500，而文档声称的全覆盖给人虚假安全感。与 F-058 叠加，错误码契约双文档失真。
- 反驳：两种合理口径均实测（37/55），无第三种口径能得到 44。
- 修复：改为「显式表 37 条客户端错误码；src 共 55 个手捏码，其余按设计落 500/409 等既有分支」，并在 ResultExtensions 加注释声明表外码处置意图。
- 静默控制问句：有差异。新码静默落 500，客户端误判服务端故障。

#### F-063..F-067 .ai 质量系统自身五处编号/计数过期（Low，同族）
- F-063 `.ai/README.md` 四处自称 NAS-G1..G20，实际 G1..G22（:83 自己又写 22，内部自相矛盾）；违反维护规则「口径数字单一来源」。
- F-064 lessons.md:49 + README 两处自称 ITM-N1..N8，实际登记 36 条（II.1 表 N1..N36 连续无跳号）；按「N1..N8」检索会漏 28 条登记（含 ITM-N13 多池数据销毁、N16 分享穿越等 P0 记录）。
- F-065 README 两处自称 NPD-1..5，实际 NPD-1..6；G19 检查已依赖 NPD-6，文档范围比代码窄，评审员会把已裁定的合法形态重新报缺陷。
- F-066 README:61 自称 tech-debt 12 项，实际 13 项（第 13 项前端死依赖观察项，README:86 自己又描述了这个功能）。
- F-067 README:102 自称测试栈「200 方法」，实测 470（199+271）；「200」在任何现存口径下都不成立。
- 统一修复：逐处改实测值；建议 verify-ai-system.sh 增加「README 中 G/ITM/NPD 编号极值 == 脚本/lessons/known-false-positives 中极值」机械项。
- 静默控制问句：无差异（文档），但误判率上升与检索漏登记都是质量系统的实质损耗。

#### F-068 [Low] README 配置示例键名/结构与 appsettings 不符，且示范明文密钥形态
- 证据：`README.md:190-205` 示例写 `"Jwt": { "Secret": "...", "ExpirationMinutes": 60 }` 与 `"NasSystem": { "StoragePath": "/app/storage" }`；实际 appsettings 是 `SecretKey: <SET-VIA-ENV>`、`AccessTokenExpirationMinutes`、`RefreshTokenExpirationDays`、`ClockSkewSeconds`，且无 NasSystem:StoragePath 段（经 env 注入）。README:171 自己又写生产取 NasSystem:AdminPassword，文档自相矛盾。
- 最坏：部署者按 README 建 appsettings 段 → 键名不绑定（静默无效）或直接把明文密钥写进配置文件（NAS-G10 要防的形态被文档示范）。
- 反驳：appsettings 全文已 cat 确认无 StoragePath 段，compose 用环境变量传递。
- 修复：示例改为与 appsettings.json 逐字一致（SecretKey 用占位符），删 StoragePath 段或注明经环境变量。
- 静默控制问句：有差异。键名写错的段静默无效，与 F-049 死键同族。

#### F-069 [Low] DESIGN.md 技术栈 TypeScript 5.x，实测 6.0.3
- 证据：`DESIGN.md:91`「| TypeScript | 5.x |」；`web/package.json` devDependencies `"typescript": "^6.0.3"`。同行 Vue 3.5/Vite 8/EP 2.14/Pinia 4/Router 5/Axios 1 均属实，唯 TS 过期。
- 修复：改 6.x。
- 静默控制问句：无差异（文档），技术栈表可信度下降。

#### F-070 [Low] TESTING.md 测试项目结构树漏列 51 个文件中的 45+
- 证据：`TESTING.md:17-35` 树列 UnitTests 7 文件 + IntegrationTests 4 文件；实测 UnitTests 13 源文件、IntegrationTests 49 源文件。树定格初始版本，10 个月测试从 11 文件增至 62 个。
- 最坏：新贡献者按树找测试组织方式，漏掉 73% 的测试文件与全部 Media/System 子目录约定。
- 修复：改目录级列举或删除树改为「以 tests/ 实际目录为准」。
- 静默控制问句：无差异（文档），覆盖广度评估失真。

---

### 4.7 待验证推测（无证据推测，单独标注，不计入正式发现）

- F-071 [推测] `FileItems.Path` 列的 collation（BINARY/NOCASE）未逐字确认（DDL 由 MigrateAsync 从 FileItemRow 生成，无显式 collation）。若为 NOCASE 则 GetByPathAsync 大小写不敏感、/Foo 与 /foo 不能共存。对分享链路无越权后果（IsUnder 是 Ordinal 严判），但影响唯一索引语义与 F-002 攻击面宽窄。验证：`PRAGMA table_info(FileItems)`。
- F-072 [推测] WebDAV 认证锁定按 IP 计数（`WebDavEndpoints.cs:145` `RemoteIpAddress?.ToString() ?? "unknown"`）。NAT 出口多用户共享 IP 会互相锁定（可用性 DoS）；反代后取不到 IP 时全部归到 "unknown" 键，一个攻击者可锁死所有人。未实测部署拓扑。
- F-073 [推测] 分享码枚举的实际吞吐未实测（31^6 ≈ 8.9×10⁸ 空间），威胁程度取决于速率实测；无限流是事实（F-007），可枚举性是推断。
- F-074 [推测] ImageSharp 5 条 advisory 的具体可利用性（信息泄露/DoS/RCE）未逐条读 CVE 详情；至少供应链卫生不合格是事实（F-030），可利用程度待查证。

---

## 5. 修复批次建议

**已执行（三个单独提交，均 S2 单变量 + S3 反向验证 + 全量回归绿）：**
1. `a267a44` F-001 上传跨用户销毁（Critical，安全）
2. `ad88753` F-002 分享子路径跨用户读取（Critical，安全）
3. `9f3bc6d` F-003 compose 默认 JWT 密钥（Critical，部署安全）

**建议批次（按「同根因合并、安全类优先、每批一个可验证节点」）：**

| 批次 | 发现 | 理由 | 顺序 |
|---|---|---|---|
| B1 安全收紧 | F-004（软删可分享）、F-005（备份路径用户维度）、F-011（扫描端点升权）、F-023（吊销旁路） | 四条都是「授权锚与当前状态脱钩」，修复模式一致（加载后校验当前态），可共享一组跨用户/生命周期集成测试 | 1 |
| B2 分享域加固 | F-006（计数原子化）、F-007（限流）、F-018（令牌绑定口令）、F-019（Update 不变量）、F-020（ZIP 上限） | 同一文件族（FileShareService/Endpoints），B1 之后分享域状态校验已统一，接着补并发与不变量 | 2 |
| B3 版本域原子性 | F-008（先库后盘）、F-009（File.Replace）、F-010（版本号重试）、F-016（恢复前置校验）、F-017（排除清单收敛）、F-015（所属池） | 六条同在 FileVersionService，改的是同一段代码的ordering 与前置校验，必须一个批次改完再整体回归（拆开中间态不一致） | 3 |
| B4 扫描健壮性 | F-012（OCE 过滤）、F-013（深度上限）、F-014（排除作用域） | 同在 FileService.Sync，B1 的扫描授权决策会影响这里的错误路径语义，故排 B1 后 | 4 |
| B5 基线收敛 | F-027（裸 IO 收口）、F-028（IDateTimeProvider 注入）、F-029（门禁校准） | 三条都动门禁基线，必须同批做三同步（脚本常量 + CONVENTIONS + lessons）；F-027 的收口又是 B1-B4 测试可注入化的前提，排最后但最先规划 | 5 |
| B6 前端卸载与竞态 | F-033、F-035、F-038、F-039（卸载清理/序号闸）、F-036（Media 序号闸） | 纯前端、互不影响，可并行；F-036 是唯一用户可感知错误数据的一条，优先 | 6 |
| B7 前端规范与类型 | F-032、F-040（§7.5 需裁决）、F-041、F-044、F-045 | F-040 涉及规范豁免，需项目负责人先裁决再做；F-044 是机械改动但面广（13 个 api 文件），独立提交 | 7 |
| B8 文档对账 | F-058..F-070 全族 + F-049..F-057 清单修订 | 一次性机械对账，建议按子代理方案增设 doc-consistency 机械项（错误码表↔ResultExtensions、端点清单↔Endpoints 目录、README 规模数字↔gate 实测）防复发 | 8 |
| B9 供应链 | F-030（ImageSharp） | 先查证可修版本与兼容矩阵再动，独立于功能批次 | 按查证结果 |

**必须分开的：** F-001/F-002/F-003 已各自独立提交（Critical 单独提交纪律）；B5 的门禁基线调整不可与任何行为修复混提交（基线数字变动会让同批测试的通过/失败含义漂移）。

## 6. 未决项与下一轮建议

**本轮未覆盖（附理由）：**
- 真实多客户端挂载矩阵（Windows MiniRedir/macOS Finder/iOS 文件 App 四端）需部署环境人工验收，代码审计无法覆盖协议兼容性。
- 中文扫描件 OCR 识别率、CLIP 检索质量、ffmpeg 转码画质需部署环境实测（ROADMAP 已列）。
- F-030 的 ImageSharp advisory 可利用性（需逐条读 CVE + 查可修版本）。
- nginx 压缩层归属（CONVENTIONS §4 记录的未决项：nginx 代理是否剥 Accept-Encoding）需部署后一条 curl 判定。
- 前端 E2E（Playwright）旅程级回归：本轮前端发现均为静态代码事实，未经浏览器复现（除已有的 177 例 vitest 基线）。

**下一轮优先级建议：**
1. B1 安全收紧（F-004/F-005/F-011/F-023）——四条都是未授权读取/越权写，与本轮三个 Critical 同族，趁修复上下文还热先收口。
2. B3 版本域原子性——版本是「误操作最后防线」，当前有四条已知缺口（F-008/009/010/016），防线本身在漏。
3. 门禁校准（F-029）——门禁红着，后续所有「只减不增」治理都失去信号，应最先恢复机械防线的信誉。
4. 文档对账机械化（B8 的 doc-consistency 增设项）——22 条文档漂移同根因，不机械化下轮必然复发。

**测试缺口总账（供 code-testing 使用）：** 上传跨用户（已补）、分享×删除交互、分享×路径复用（已补）、下载计数并发、版本号并发覆盖、版本 DB 失败注入、扫描×深层目录/junction、扫描×排除名碰撞、Media 序号闸、signalr 停止/重连、部署配置断言（已补）。其中「DB/IO 失败注入」类缺口被 F-027（裸 IO 未收口）直接阻塞，收口前只能写集成级真实盘测试。



---

## 8. 修复执行记录（2026-10-22 修复轮）

用户裁决「按最优方案修复」后按第 5 节批次执行。每个批次一条提交，均跑全量回归（集成 + 单元 + 前端，见文末基线）；每条 Critical/High 修复附 S3 反向验证（临时回退源码确认锁定测试转红）。

### 8.1 已修复清单（61 条）

| 批次 | 提交 | 修复的发现 | 验证 |
|---|---|---|---|
|  Critical 三连 | `a267a44` / `ad88753` / `9f3bc6d` | F-001 上传跨用户销毁、F-002 分享子路径跨用户读取、F-003 compose 默认 JWT 密钥 | 3 条锁定测试，S3 实测修复前全红 |
| B1 安全收紧 | `49e1098` | F-004 软删可分享、F-005 备份路径用户维度、F-011 扫描端点权限、F-023 吊销旁路 | SecurityHardeningRegressionTests 5 例，S3 实测 4 例转红（第 5 例为回归守卫） |
| B2 分享域加固 | `ea0a44f` | F-006 计数原子化、F-007 公开分享限流、F-018 令牌绑口令、F-019 Update 不变量、F-020 ZIP 上限 | ShareHardeningRegressionTests 3 例，S3 实测 3 例全红（并发打穿 19/20） |
| B3 版本域原子性 | `7bdd90b` | F-008 删版本保记录、F-009 File.Replace、F-010 版本号重试、F-015 所属池、F-016 恢复前置校验、F-017 临时件排除 | VersionAtomicityRegressionTests 3 例，S3 实测 3 例全红（并发只落 1 版） |
| B4 扫描健壮性 | `5fcbe34` | F-012 OCE 过滤、F-013 深度上限、F-014 排除作用域 | ScanRobustnessRegressionTests 3 例，S3 实测 3 例全红 |
| B6 前端 + B8 文档 | `d9c35b2` | F-032~F-041、F-045 前端九项；F-049~F-070 文档配置二十二项 | vitest 183 + oxlint 0 + vue-tsc + 构建全过；doc-consistency D4 三方一致恢复 |
| B5 收口 | `10e0047` | F-029 门禁校准（G7）、F-022 静默跳过日志、F-024 注释纠谬、F-025 注释纠谬、F-026 硬链接哨兵、F-030 ImageSharp 3.1.12→3.2.0 | NU19xx 警告 5 归零；语义搜图 19 例全过；G7 恢复 PASS |

`.ai` 独立仓合同步提交 `07cd9a7`（gate-check.sh 白名单 + README/lessons 编号对账）。

### 8.2 登记待专项的发现（第一轮 13 → 三轮后余 3 → 四轮后余 1 → 五轮清零）

第一轮（2026-10-22 白天）修复 61 条后登记 13 条待专项；第二轮修复 F-021/F-034/F-037/F-042/F-043/F-044/F-071；第三轮修复 F-027/F-028/F-031（提交 `01b91a9`）；第四轮修复 F-072/F-073（提交 `dc40f53`）；第五轮（同日续）修复 F-074（提交 `1166b46`）并完成 F-028 第二批（提交 `e1e8f87`）。**71 条发现全部修复**。

余下两项非缺陷类待办（都不影响「发现全部修复」的结论）：

| 编号 | 状态 | 理由与建议处置 |
|---|---|---|
| F-072 部署侧待办 | 需运维动作 | 代码能力已提供，但**默认不启用**（无条件信任 X-Forwarded-For 等于允许伪造客户端键绕过锁定）。nginx 部署需在 compose 里给 nas-system 配 `NasSystem__ForwardedHeaders__KnownProxies`（nginx 容器 IP，或给 nginx 配固定 IP）后才能按真实客户端隔离锁定桶。未配时行为与修复前一致：共享桶，可用性风险回归到 F-072 原状 |
| F-073 残余 | 测量缺口 | 全局闸把总探测速率钉在 600/分钟（默认），但「这个阈值相对于真实威胁模型是否合适」需要部署环境压测数据。若实测正常使用会触碰阈值，调 `NasSystem__RateLimiting__ShareGlobalPerMinute` |

F-074 的闭环过程值得记一条教训：**上一轮「NU 警告归零」是增量构建未重跑漏洞审计造成的假绿**。真正下结论必须 `--force` 重新 restore，否则依赖升级类修复的验证会被构建缓存掩盖。同类假绿在本项目的其他场景（产物新鲜度、`--no-build` 测试）已有登记，本条是依赖审计维度的补充。

### 8.3 修复轮最终基线（2026-10-22 五轮后实测）

- 后端构建：exit 0，0 错误；NU19xx 漏洞警告 **5 → 0**（`--force` restore 复算，非增量缓存口径）
- 单元测试：**199 total / 0 failed** / exit 0
- 集成测试：**298 total / 0 failed** / exit 0（起点 270，新增 28 条锁定测试）
- 前端：vitest **183 tests / 26 files 全过**；oxlint **0 警告**；vue-tsc exit 0；生产构建 exit 0；业务代码 `as any` **20 → 0**
- 项目门禁：**exit 0，20 通过 / 0 失败 / 1 警告（D9 无 CHANGELOG，可选）**。NAS-G15 UtcNow **16/16**（起点 52）、NAS-G16 裸 IO **0/0**（起点 31），两条基线债务随 F-027/F-028 两批收口按「只减不增」规则三同步下调（CONVENTIONS §1.5/§5.1/§9 ↔ gate-check.sh 常量 ↔ .ai/README.md，doc-consistency D4 实测三方一致）

---

## 9. 优化轮（2026-10-22 续，用户要求「完整优化」）

审计轮解决的是「对不对」，本节解决「快不快」。三条原则：先测量再改、每条改动附前后实测、不以行数或体积论英雄。

### 9.1 前端产物瘦身

| 项 | 改动 | 实测 |
|---|---|---|
| hls.js 按需加载 | Media.vue 的静态 import 改 import type + mountPlayer 内 await import() | **Media chunk 589,825 → 15,216 字节（−97.4%）**；gzip 183.75KB → 5.47KB；hls 独立成 574KB 懒加载块 |
| table-v2 拆包 | vite manualChunks 把 table-v2 + virtual-list 拆独立块，**排除 /style/ 路径** | 43KB 独立块，仅文件页按需；首屏 preload 从 7 项降到 6 项；element-plus 主块 679,199 → 636,602 字节 |

两处都由浏览器实测验证：媒体库网络清单确认**不再请求 hls chunk**、页面正常渲染出图片项；文件页确认**按需请求 table-v2**（200）、表格渲染出数据行与表头；两页 console 均 0 错误。

table-v2 拆包的关键陷阱：main.ts 里有 table-v2/style/css 的副作用导入（表头顺序依赖它）。若连这条样式路径一起归入新块，入口就会静态引入它，Vite 会把该块 preload 进 index.html，拆分等于白做（第一次尝试就被实测否掉了）。

### 9.2 前端回归修复（上一轮引入的潜伏缺陷）

拆 hls 时浏览器实测抓到 `Cannot access 'Lt' before initialization` —— 媒体库首屏完全空白。

根因：上一轮加序号闸时把 `let loadSeq = 0` 写在了 `watch(..., { immediate: true })` **之后**。该 watch 的回调在 setup 期同步执行并调用 loadMore，此时 loadSeq 仍在暂时性死区。这是合法代码——vue-tsc 通过、183 个单测通过、构建通过，只有真跑浏览器才炸。已把声明前移并注明原因。

这条是本轮最有价值的副产品：**静态检查与单测都拦不住 TDZ，只有真实渲染能**。

### 9.3 后端热点定位与消除

先建探针再改，不猜。实测 6 个读端点的延迟：

| 端点 | 修复前 |
|---|---|
| `/api/v1/system/info` | **295~347ms**（8 次全部 300ms+） |
| 其他 5 个读端点 | 9~30ms |

新增 SystemInfoLatencyProbeTests 把耗时拆成「端到端 / 服务层 / 逐项采集」三层，定位到唯一主导项：

```
NetworkInterface.GetAllNetworkInterfaces()   112.41ms   ← 唯一大头
Process.GetCurrentProcess + StartTime          5.37ms
Registry.ReadCpuModel                          0.06ms
Environment.MachineName / TimeZone             0.00ms
```

而服务层总耗时 233ms > 单项 112ms，说明该 API 被调了两次——原实现正常分支与无网卡分支各调一次 GetIsNetworkAvailable()，后者内部又枚举一遍全部网卡。

修复两处：

1. **只枚举一次**：一次 GetAllNetworkInterfaces() 结果同时用于「挑主网卡」与「判定 IsAvailable」
2. **2 秒 TTL 缓存**（静态字段 + Lock）：网卡集合与 IP 不秒级变动，2s 吸收「系统信息页连续刷新」，又不让流量数字看起来冻住。服务本身 Scoped（要查 SQLite 版本），缓存必须跨请求存活

实测：**服务层 233ms → 2.9ms，端点端到端 300ms+ → 5.3ms（−97.8%）**；真实实例 curl 6 次为 187ms（首次冷缓存）/ 17 / 14 / 12 / 14 / 12ms。S3 反向验证：摘掉缓存后服务层回到 217ms，探针转红。

### 9.4 未采纳的方向（有测量依据的否决）

| 方向 | 否决理由 |
|---|---|
| element-plus 主块 636KB 继续拆 | 已按需引入（符合 CONVENTIONS §7.1），剩余体积是 table-v2 之外各组件本身；再拆会让请求数增加而无净收益 |
| sortedFiles 的 `[...files.value]` 复制 | 它是 computed（有缓存），只在 files 变化时重算一次；改成原地排序会破坏响应式语义 |
| Dashboard 首屏 9 个请求合并 | 已用 Promise.all 并发，且 60s 轮询前有「不与 onMounted 重复」的显式注释；后端各端点 10-20ms，合并没有净收益 |
| SQLite 压往返 / JOIN / 投影裁剪 | 项目已结论：本地 SQLite 往返成本近似零，实测压往返/JOIN/投影全慢（本审计任务书第 5 步明令禁止重提） |
| 冷启动 3.2s | 含 dotnet run 的编译检查开销；Docker 侧已开 ReadyToRun。要进一步压需分解到具体阶段，当前没有可归因的热点 |

### 9.5 优化轮最终验证

- 后端构建 exit 0 / 0 错误；单元 **199/0**；集成 **299/0**（新增探针 1 例）
- 前端 vitest **183/26 全过**；oxlint 0；vue-tsc exit 0；生产构建 exit 0
- 项目门禁 **20 过 0 败**
- 浏览器实测：媒体库渲染正常、文件页表格正常、两页 0 console 错误

提交：`9346f1a`（前端两项 + TDZ 修复）、`a18a97b`（system/info 优化 + 探针）。

---

## 10. 优化轮第二批（2026-10-22 续）

### 10.1 响应压缩等级（`6fe30ea`）

默认的 `CompressionLevel.Fastest` 是 ASP.NET Core 压缩中间件的缺省值，对 Brotli 这种 CPU 换体积的算法等于放弃大半收益。实测同一份产物：

| 资源 | 原始 | Fastest | Optimal |
|---|---:|---:|---:|
| element-plus（最大项） | 636,602 | 258,218 | **201,296** |
| index | 98,464 | 46,295 | **36,534** |
| vue-i18n | 56,698 | 22,645 | **19,208** |
| hls | 574,410 | 233,209 | **176,638** |

三最大项合计省 70KB（−21%）；首屏预加载 JS 全集 raw 926,753 → br 303,288（32.7%）。
成本是 CPU，静态产物只付一次；API JSON 实测 2~25KB，改动后端点延迟 11~18ms 与改动前同区间。

顺带用实测关闭 CONVENTIONS §4 挂了两个月的未决项「压缩层归属」：.NET 11 起压缩响应**始终**带 `Vary: Accept-Encoding`（未压缩时也带），nginx 自 1.7.7 起在 proxy_cache 中处理 Vary → 现有缓存键不含编码也不会串味；且 Optimal 下 br 与 gzip 只差 1.5%，无论哪一层在压体积基本一致。

锁定测试 `ResponseCompressionTests` 三例（等级、压缩行为与 Vary、无 Accept-Encoding 时仍带 Vary），S3 摘配置后第一条转红。

### 10.2 压测夹具 schema 漂移（`7154bcb`）

在 5 万行夹具库上核查索引时发现子树查询 12.9ms 全表扫。深挖后确认**不是生产缺陷**，是夹具与生产 DDL 漂移造成的假象：

- 夹具把 `IX_FileItems_Path` 建成「唯一部分索引 WHERE IsDeleted=0」，生产里这个名字是**全量 BINARY** 索引（唯一部分索引另名 `UX_FileItems_Path_Active`）→ 夹具上任何不带 IsDeleted 的路径查询都用不上索引
- 夹具缺 `IX_FileItems_Path_Nocase`（全量 NOCASE）与 `IX_FileItems_OwnerId_CreatedAt`

验证：副本改成生产同构后同一查询 12.9ms → **0.17ms**；保持缺口则回到 12.9ms。

修复：夹具路径索引四件套与生产逐条对齐；新增 `BenchmarkFixtureSchemaTests` 按夹具声明的 5 张热路径表逐表比对索引名集合（双向差集都为空），S3 回退后精确报出 3 个缺失索引。`seed --assert` 复验「schema 一致（29 个对象）」。

未采纳：改写子树 OR 形态。隔离探针实测 OR 0.173ms / 纯 LIKE 0.147ms / 等值单支 0.149ms，三者同量级，改写无收益。

### 10.3 本轮方法论教训（值得进 lessons）

**性能探针必须形态隔离，否则结论可能与事实相反。** 同一个查询、同一个库文件，在「三形态共享一个连接交替测量」的探针里报 19.5ms，在「每形态独立连接、不交叠」的最小探针里报 0.173ms；两次独立的最小探针（4 种打开方式 + 每形态独立连接）结果一致。同类教训此前登记在「分段计时只用于定位热点，量化收益必须墙钟交错 A/B」，本轮补充的是**反向**纪律：定位阶段的探针也必须做形态隔离，否则会把测量假象当缺陷修。

### 10.4 本轮验证

- 后端构建 exit 0 / 0 错误；单元 **199/0**；集成 **303/0**（新增夹具锁定 1 例、压缩锁定 3 例）
- 前端未改动，沿用 183/26 全绿基线
- 项目门禁 **20 过 0 败**；文档一致性 9 过 0 败
- 首屏 JS 压缩传输：raw 926,753 → br **303,288**（32.7%）

提交：`6fe30ea`（压缩等级）、`7154bcb`（夹具对齐 + 锁定）。

### 10.5 静态资源缓存头（本轮新增）

`nginx.conf` 的 `production` profile 一直是 `/assets/` 一年 immutable、入口
`no-cache`，但直连 Kestrel（compose 默认只起应用，边缘可选）时静态文件只发
`ETag`/`Last-Modified`，浏览器按「文件年龄的 10%」做启发式缓存，首屏近 1MB
的 JS 在回访时仍可能重新校验。

补法：`UseStaticFiles` 的 `OnPrepareResponse` 按路径前缀分流，`/assets/` 发
`public, max-age=31536000, immutable`，其余（`index.html`、`sw.js`、图片等）
发 `no-cache`；SPA 回退分支在 `SendFileAsync` 前同样发 `no-cache`。

实测（`curl -D-`，运行中的实例）：`/assets/Backup-*.js` → immutable；
`/`、`/files`、`/sw.js` → `no-cache`。锁定测试 `StaticCacheHeaderTests` 三例
（哈希产物 / 入口与 SPA 回退 / Service Worker），走真实的 `wwwroot` 产物，
从入口 HTML 里解析出实际哈希文件名而不是硬编码。

`nginx.conf` 同步加了两处 `proxy_hide_header Cache-Control`：上游现在也发这份
策略，不隐藏会由 `add_header` 追加出两条同值头（行为无害但不干净）。该改动
`[未实测]`，本机无 nginx 与 docker；`proxy_hide_header` 是核心指令，语义稳定。
