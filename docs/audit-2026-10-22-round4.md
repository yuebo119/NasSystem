# NAS System 第 4 轮审计报告（架构与依赖）

- 日期：2026-10-22
- 主轴：架构与依赖
- 交叉轴：安全隔离
- 模式：审计并修复
- 预算：无硬上限，按任务书停止条件收口

## 1. 已读清单

- `docs/audit-2026-10-22-exhaustive.md` 与第 1/2/3 轮报告：F-001 到 F-089 全部账本，含 F-072/F-073 部署残余与 F-084 待裁决残留。本轮不重报。
- `.ai/lessons.md`、`docs/CONVENTIONS.md`、`docs/decisions/`：重点核对 §1 分层、§4 安全、§7 前端契约与 G1..G4 门禁口径。
- `git log --oneline -40` 与最近提交 diff：避让第 1/2/3 轮改动面（解码、快照、OCR、扫描、外部进程、领域边界）。
- 依赖与注册面：`Directory.Packages.props`、各 `DependencyInjection.cs`、全部 Singleton 构造函数依赖。

## 2. 本轮结论

架构主轴与安全交叉轴合计确认 4 条新发现：ZIP/导出跟随重解析点、扫描导入重解析点文件、异常回显物理路径、Api 直用仓储且属主判定分散。4 条全部修复并配锁定测试；单测从 222 增加到 226，集成从 312 增加到 315，干净门禁 21 通过 0 失败。

## 3. 发现总表

| 编号 | 严重级 | 维度 | 位置 | 一句话 | 状态 |
|---|---|---|---|---|---|
| F-090 | Medium | 安全隔离 | ZipDirectoryResult.cs:86 / ExportResult.cs:126 | ZIP 分享与导出直接遍历物理目录，跟随 junction 把池外文件打进包 | 已修 196bc50 |
| F-091 | Medium | 安全隔离 | FileService.Sync.cs:378 | 扫描只跳过重解析点目录，符号链接文件被导入库，池外内容可被下载/分享 | 已修 04a144a |
| F-092 | Low | 安全隔离 | GlobalExceptionHandler.cs:52 | FileNotFoundException 回显异常 FileName，泄漏池挂载点/用户目录 | 已修 4d46137 |
| F-093 | Low | 架构 | ThumbnailEndpoints.cs:61 / TranscodeEndpoints.cs:59 | Api 直用仓储并各自写属主比较；另有死参数与死方法 | 已修 6bed060 |

严重级分布：Medium 2、Low 2。确认 4 条，推测 0 条。

## 4. 发现完整论证

### F-090 ZIP 分享与导出跟随重解析点（Medium，已修）

- 证据：`ZipDirectoryResult.cs:86,109` 与 `ExportResult.cs:126,144,156` 原为 `Directory.EnumerateFiles/EnumerateDirectories` 直接遍历；当前改为 `SafeFileEnumeration`（新文件，`IsReparsePoint` 读取失败也按跳过处理）。
- 机理：两条路径都从物理目录出发，不经过库内记录。扫描层 F-013 早已跳过 junction，但打包/导出是另一条遍历代码，未继承该规则。共享目录里一个指向池外的 junction 会把外部文件打进 ZIP；指向父目录的 junction 会造成重复遍历，ZIP 侧虽有 20000 条目上限，但会产出截断包并浪费 IO。
- 触发条件：共享目录内存在 junction/symlink。谁来创建？本地进程或有权限的用户；应用自身不会创建。NAS 场景里用户把外部目录挂进共享目录并不罕见。
- 最坏影响：公开分享的 ZIP 里出现池外文件，属跨目录数据披露；导出包混入非预期内容。需要本地链接作为前提，因此定 Medium 而非 High。
- 现有测试为何没拦住：`ZipDirectoryResult`/`ExportResult` 只有正常目录与上限测试，没有链接用例；扫描层的 reparse 测试不能覆盖这两条独立遍历路径。
- 自我反驳：Windows 创建 junction 不需要管理员权限吗？普通用户可创建 junction（符号链接才常需权限），所以前提在普通部署里成立。会不会 `Directory.EnumerateDirectories` 自动不跟随？实测会返回 junction 目录，递归会进入目标；S3 禁用检查后两例 ZIP 测试都失败，证明确实跟随。
- 最小修复：新增 `SafeFileEnumeration`，文件与目录枚举都跳过重解析点；空目录判定也把链接排除在外；读取属性失败按跳过处理（文件中途消失/权限不足不abort 整个包）。
- 验证方式：`ZipReparsePointTests` 在共享目录里建指向池外的 junction，分别跑 `ZipDirectoryResult` 与 `ExportResult`，断言包内只有 `normal.txt`、没有 `secret` 条目。S3 让 `IsReparsePoint` 恒返回 false 后两例转红。
- 回归风险：共享目录里的合法符号链接不会被打包/导出，这是期望的安全默认（扫描层同口径）。无权限建链接的环境测试会 `Assert.Skip`，本轮本机实际执行通过。
- 静默控制问句：如果检查被禁用，ZIP 条目里会出现 `leak/secret.txt`，两例断言直接失败。

### F-091 扫描导入重解析点文件（Medium，已修）

- 证据：`FileService.Sync.cs:276` 原用 `GetDirectoryInfo(dir).Attributes` 只检查目录；文件循环没有检查。当前 `IFileSystemService.cs:83` 新增 `IsReparsePoint`，`FileSystemService.cs:141` 实现，`FileService.Sync.cs:276,378` 目录与文件都走它。
- 机理：符号链接文件在物理上位于池内，内容是池外目标。扫描把它当普通文件导入库后，下载、缩略图、分享、导出都会读目标内容；库内记录让"池外数据"获得了合法的虚拟路径和分享入口。
- 触发条件：池目录内存在文件级符号链接，随后执行扫描同步（管理员动作或定时扫描）。
- 最坏影响：池外文件进入用户库并可被该用户下载/分享；若目标是他人目录，构成跨用户数据披露。需要本地链接作为前提，定 Medium。
- 现有测试为何没拦住：`ScanRobustnessRegressionTests` 覆盖 junction 目录环与深度上限，没有文件级链接；F-013 的修复范围本身只到目录。
- 自我反驳：文件级符号链接在 Windows 上创建需要权限，是否现实？需要 SeCreateSymbolicLinkPrivilege 或开发者模式；但 Linux 部署上 `ln -s` 任意用户可做，项目目标包含 Linux 部署，所以前提成立。扫描跳过文件后，旧的链接记录会被删除集清掉，这是期望的清理方向，需在文档说明。
- 最小修复：接口增加 `IsReparsePoint`（读取失败返回 true，按不安全处理），扫描文件循环在入库前跳过；目录检查改用同一方法。
- 验证方式：`FileServiceTests.ScanAndSyncAsync_ReparsePointFile_SkipsIt` 用真实临时文件保证 GetFileInfo 可读，mock 该路径为重解析点，断言 `AddedFiles == 0`。S3 注释掉检查后转红。
- 回归风险：池内合法的符号链接文件不再入库，与目录侧既有策略一致；删除旧记录是安全收口而非数据丢失（物理链接仍在盘上）。
- 静默控制问句：如果检查缺失，临时文件会被导入，`AddedFiles` 变成 1，用例转红。

### F-092 FileNotFoundException 回显物理路径（Low，已修）

- 证据：`GlobalExceptionHandler.cs` 原分支为 `new ApiError("Error.NotFound", $"文件未找到: {fileEx.FileName}")`；当前 `:52` 改为通用"请求的文件或目录不存在"。
- 机理：内部 IO 抛出的 `FileNotFoundException.FileName` 常是物理路径（池挂载点、用户目录、版本目录布局）。把它写进响应体，等于给客户端一份服务器目录结构样本。
- 触发条件：任何走到该异常的请求，例如读取一个被外部删除的版本文件、缩略图源文件或转码源文件。
- 最坏影响：信息泄露（路径布局、用户名），不涉及文件内容；可作为后续探测的输入。定 Low。
- 现有测试为何没拦住：异常映射测试只覆盖参数绑定与 InvalidOperationException，没有检查响应体内容。
- 自我反驳：有些调用方可能依赖 FileName 做提示？前端错误提示统一用 `message`，不解析路径；暴露物理路径没有产品价值。若确实需要文件名，应由服务层用虚拟路径构造 `Error.NotFound`，而不是透传异常字段。
- 最小修复：异常分支不回显 FileName；客户端只需知道"不存在"。
- 验证方式：`GlobalExceptionHandlerTests.InvokeAsync_FileNotFound_DoesNotLeakPhysicalPath` 用带 `/mnt/secret-pool/...` 的异常，断言响应体不含该路径。S3 恢复回显后转红。
- 回归风险：错误文案变短；状态码仍 404。
- 静默控制问句：如果仍回显 FileName，响应体包含 `/mnt/secret-pool`，用例转红。

### F-093 Api 直用仓储与属主判定分散（Low，已修）

- 证据：修复前 `ThumbnailEndpoints.cs` 与 `TranscodeEndpoints.cs` 各自注入 `IFileItemRepository`，各自写 `item.OwnerId != userId`；`SemanticEndpoints.GetStatus` 注入仓储但从不使用；`ExportEndpoints` 有未使用的 `ClaimsPrincipal user` 与 `GetUserId`。当前 `IFileAccessResolver.cs` 新增解析器，`ThumbnailEndpoints.cs:61` 与 `TranscodeEndpoints.cs:59` 改注入它；死参数/死方法已删。
- 机理：属主判定是横切安全规则，分散在端点里意味着新增端点要记得重复同一条比较；漏写就是越权。Api 直接依赖仓储也让端点越过 Application 用例层。
- 触发条件：架构层面持续存在；当前 3 处判定都是正确的，没有现存漏洞。
- 最坏影响：未来新增按路径取文件的端点时漏写属主检查，造成跨用户读取；当前无运行时影响。
- 现有测试为何没拦住：没有"端点不得直接注入仓储"的机械门禁；`CrossUserIsolationTests` 只覆盖已有端点。
- 自我反驳：只改两个端点算不算过度设计？属主规则此前在 Thumbnail（404 伪装）与 Transcode（403 明示）两处已有不同表现，正说明规则与表现应分离；解析器把"查库 + 软删过滤 + 属主判定"收成一处，端点只保留状态映射。Export 是管理员全库导出，按设计不做 per-owner 过滤，保持直用仓储并在注释说明；Semantic Search 是按 Id 批量取详情，不适合单路径解析器，保留。
- 最小修复：`IFileAccessResolver` 返回 `Ok/NotFound/Forbidden` 与物理路径；Thumbnail 把非 Ok 一律映射 404，Transcode 保留 NotFound 404 / Forbidden 403 的既有契约；删除死参数与死方法。
- 验证方式：`FileAccessResolverTests` 3 例（本人、非本人、缺失）；既有 `TranscodeEndpointTests` 的非 owner 403 与 Thumbnail 端点测试全过。S3 删掉解析器里的属主判定后非本人用例转红。
- 回归风险：Transcode 的非 owner 行为保持 403，Thumbnail 保持 404；物理路径只在 Ok 分支暴露。
- 静默控制问句：如果解析器漏掉属主判定，非本人会拿到 Ok 与物理路径，`ResolveAsync_NotOwner_ReturnsForbiddenWithoutPath` 转红。

## 5. 扫描口径记录

| 类 | 命令与范围 | 命中 |
|---|---|---:|
| 物理目录遍历 | `grep -rn "Directory.Enumerate" src/NasSystem.Api`，逐条核对是否经过扫描层 | 2 条路径（F-090） |
| 重解析点 | `grep -rn "ReparsePoint\|CreateHardLink\|symlink\|junction" src` | 3 处（扫描目录、ZIP、导出） |
| 异常信息面 | 读 `GlobalExceptionHandler` 全部分支 + `grep -rn "FileName" src` | 1 处回显（F-092） |
| Api 依赖图 | `grep -rn "IFileItemRepository\|IFileShareRepository\|IStoragePoolRepository" src/NasSystem.Api/Endpoints` | 5 处，逐一分类（F-093） |
| 属主判定 | `grep -rn "OwnerId != \|OwnerId == " src/NasSystem.Api` | 3 处，全部核对 |
| DI 生命周期 | 20+ 注册的构造函数依赖逐一核对（第 3 轮已做，本轮复核无新增 Singleton） | 0 处俘虏依赖 |

## 6. 修复批次与验证

| 批次 | 提交 | 内容 | 验证 |
|---|---|---|---|
| ZIP/导出 | `196bc50` | F-090 | ZipReparsePointTests 2 例；S3 转红 2 |
| 扫描链接 | `04a144a` | F-091 | FileServiceTests 1 例；S3 转红 |
| 路径回显 | `4d46137` | F-092 | GlobalExceptionHandlerTests 1 例；S3 转红 |
| 访问解析 | `6bed060` | F-093 | FileAccessResolverTests 3 例；S3 转红；既有端点测试全过 |

最终验证：构建 exit 0 / 0 错误；单测 226/0；集成 315/0；前端 183/26、oxlint 0、vue-tsc exit 0、生产构建 exit 0；干净门禁 21 通过 / 0 失败 / 0 跳过。

本轮新增测试 7 个：单元 4 例（扫描链接 1、解析器 3），集成 3 例（ZIP 2、路径回显 1）。

## 7. 未决项与观察项

- F-084 残留（第 2 轮登记，仍待你裁决）：被跳过的不可读目录其库内活跃记录会在删除集判定里被当作"物理消失"删除。本轮未动。
- Api 直用仓储剩余 2 处（均为有意保留）：`ExportEndpoints` 是管理员全库导出，按设计不做 per-owner 过滤；`SemanticEndpoints.Search` 是按 Id 批量取详情并做归属/删除过滤，不适合单路径解析器。后续若新增单路径端点，应走 `IFileAccessResolver`。
- 重解析点判定有两份实现（`FileSystemService.IsReparsePoint` 与 `SafeFileEnumeration.IsReparsePoint`）：前者在 Application 侧经接口调用，后者在 Api 的直接遍历里用静态助手。两处规则必须同步；后续可考虑抽到 Shared 的静态策略，但 Shared 零依赖的约束下收益有限。
- `SnapshotService.CreateAsync` 读树不在写入事务内（观察）：快照与并发写入的一致性仍无验收口径，未定义前不动手。
- `TranscodeEndpoints` 与 `ExportEndpoints` 的错误形态仍不走 `ApiResponse` 信封（观察）：状态码正确，前端只按状态码处理时无影响，建议随 Api 层收口统一。
- 第 1/2/3 轮观察项仍在：`DirectoryVersion` 静态字典不淘汰、`WebDavAuthGuard` 失败表按 IP 增长、`ThumbnailService._inFlightGenerations` 工厂重入、FileVersionService 两次重试上限。

## 8. 下一轮建议

1. 主轴选「安全隔离」：本轮把物理遍历与属主判定收口，下一轮可做跨用户隔离的全端点矩阵（含 WebDAV、分享子路径、备份、导出）与路径规范化的机械检查。
2. 交叉轴选「部署与配置」：F-072 的 `KnownProxies` 仍是运维待办；可把 compose/nginx 的默认值与文档一致性做成门禁断言。
3. 若先裁决 F-084 残留语义，安全隔离轮可以顺手把"跳过目录的库记录保留"纳入删除集规则，避免语义反复。

## 9. 复现命令

```bash
dotnet build NasSystem.sln
./tests/NasSystem.UnitTests/bin/Debug/net11.0/NasSystem.UnitTests.exe
./tests/NasSystem.IntegrationTests/bin/Debug/net11.0/NasSystem.IntegrationTests.exe
cd web && npm run test && npm run lint && npm run type-check && npm run build && cd ..
bash .ai/scripts/gate-check.sh
grep -rn "Directory.Enumerate" src/NasSystem.Api --include=*.cs
grep -rn "IFileItemRepository" src/NasSystem.Api/Endpoints --include=*.cs
```

## 10. 收尾自检

1. 账本读了吗，有没有重报：读了 F-001 到 F-089 全部账本；F-072/F-073/F-084 按既有状态处理，未重报。
2. 基线是本轮实测吗：是。起始 222/312/183、门禁 21/0/0；结束 226/315/183、门禁 21/0/0，均为本轮复跑。
3. 每条发现十项齐全吗：F-090 到 F-093 均含证据、机理、触发、影响、测试缺口、自我反驳、最小修复、验证、回归、静默控制。
4. 有没有凑数条目：无；F-093 明确标注当前无运行时漏洞、属架构债。
5. 每个修复是否指认锁定测试，S3 做了吗：四条都有 S3 转红后恢复的记录。
6. 全量测试与门禁本轮复跑且贴计数行了吗：见第 6 节。
7. 有没有重提已否决方向：没有。
8. 有没有改范围外代码：只改打包/导出遍历、扫描链接策略、异常文案与文件访问解析；未改前端业务逻辑、未改 API 成功响应契约。
9. 有没有未经验证的性能或内存声明：本轮无性能声明。
10. 账本编号连续吗：F-090 到 F-093 连续，F-001 到 F-089 未改。

## 11. 进度

| 栏位 | 状态 |
|---|---|
| 实现进度 | F-090 到 F-093 全部已修（4/4） |
| 验证完成度 | 4 条均有测试级 S3 转红记录；全量 226/315/183 与门禁 21/0/0 本轮复跑 |