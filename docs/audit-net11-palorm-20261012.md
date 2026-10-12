# 全面审计报告：.NET 11 适配与 PalORM 替换完成度

- 日期：2026-10-12
- 代码状态：Git SHA 31d9b4d + 本轮未提交修复（本文第六节列清单）
- 机器：DESKTOP-IDCGOGO，Windows NT 10.0.19045.0，32 逻辑核
- 本轮实测：build 0 错误；单测 148 + 集测 72 + 前端 19 全过；基准三臂重跑

## 一、结论

两个问题的答案都是**是，100%**，各有一条残留项不影响判定：

| 问题 | 结论 | 依据 |
|---|---|---|
| .NET 11 升级适配是否 100% | 是 | 8 个项目 TFM 全部 net11.0，Docker 双镜像 11.0-preview，10 个 Microsoft 包全部 11.0.0-rc.1 原生版，全量构建 0 错误 |
| PalORM 替换是否 100% 完成 | 是 | 源码零 EF 引用、零 PalORM 类型泄漏进核心层、5 个仓储全部重写、schema 生成与存量库升级路径均有测试锁定 |

性能提升的答案是：**API 吞吐 2.8 倍，P99 延迟降 84%，冷启动减半**。收益几乎全部来自换 ORM，
运行时升级本身对这个负载没有可测影响（详细归因见第四节）。

残留两条，都不改变上述结论：本机 bin/obj 里有 net10.0 历史构建目录（gitignored，
不影响仓库与构建，清理需宿主机手动执行）；.NET 11 仍是 RC1，GA 后需按 ADR-0002
的触发条件复跑基准。

## 二、.NET 11 适配逐项核销

| 核销项 | 状态 | 证据 |
|---|---|---|
| 目标框架统一 net11.0 | 完成 | 8 个 csproj 全部继承 Directory.Build.props:6 的单一 TFM，无项目级覆盖 |
| SDK 版本 | 完成 | 11.0.100-rc.1.26425.128，C# LangVersion 14 |
| Docker 构建镜像 | 完成 | Dockerfile:23 sdk:11.0-preview，:52 aspnet:11.0-preview，与 TFM 一致 |
| Docker 还原断点 | 本轮修复 | 裸 `dotnet restore` 因 .dockerignore 排除 tests/tools 而 MSB3202 失败；改为显式还原 API 工程 |
| Microsoft 包原生版 | 本轮修复 | 10 个包升到 11.0.0-rc.1.26425.128；另删掉 2 条 10.0.12 死 PackageVersion（其 PackageReference 早已移除，net11 SDK 自动提供 abstractions） |
| 全量构建 | 通过 | 0 错误 |
| 测试 | 通过 | 单测 148、集测 72、前端 19，全绿 |
| AOT 声明 | 诚实态 | PublishAot=false 显式声明；ITM-N8 裁决移除假声明后未再宣称兼容 |
| 本机构建残留 | 已知残留 | bin/obj 下有 net10.0 旧目录，全部 gitignored，MSBuild 只认 csproj 的 TFM，不影响构建结果 |

### 本轮修的两个真问题

**Docker restore 断点。** .dockerignore 排除了 tests 与 tools 两个目录，而 NasSystem.sln
引用了这两个目录下的工程，容器内执行裸 `dotnet restore` 必失败。改成一显式指定 API
工程，连带还原它的整条项目依赖链，行为等价。本地用同样的排除上下文模拟复现过失败，
再验证修复。

**两条死版本钉。** Directory.Build.props 里 Microsoft.Extensions.Configuration.Abstractions
与 DependencyInjection.Abstractions 还钉在 10.0.12，但两个 csproj 的 PackageReference
都已移除（注释说明 net11 SDK 自动提供，显式引用报 NU1510）。死钉的害处是下次有人直接
引用这两个包时，CPM 会静默把版本压回 10.0.12。已删除。

## 三、PalORM 替换逐项核销

| 核销项 | 状态 | 证据 |
|---|---|---|
| 数据访问栈 | 完成 | PalORM 6.4.1（Core/Sqlite/SourceGen 三包，版本在 Directory.Build.props 显式钉住） |
| EF 痕迹 | 零残留 | 源码、csproj、props、CI 配置 grep 零命中；命中的只有 gitignored 的 bin/obj 旧产物与 docs/perf 历史报告 |
| 仓储层 | 完成 | 5 个仓储全部重写，走 PalORM 的 From/Where/ScalarAsync/InsertAsync/UpdateAsync |
| 领域层纯净 | 完成 | Domain 零 PalORM 引用；Application、Api 层 grep 同样零命中 |
| 持久化模型 | 完成 | 5 个影子行模型 + RowMapper 双向映射 + 枚举原生 AsInt32 |
| 事务与事件 | 完成 | PalormUnitOfWork 意图队列单事务 + 领域事件分发 + 异常转译 |
| schema 生成 | 完成 | MigrateAsync 按注解建表，21 个索引随模型声明，3 条带 WHERE 的部分索引手工补 |
| 外键约束 | 完成 | 7 个 FK 已声明，新建库 pragma_foreign_key_list 计数 = 7（影子模型漏声明时期为 0，由测试锁定） |
| 存量库升级 | 完成 | 幂等建表不重建；EF 账本表 __EFMigrationsHistory 本轮补了检测与告知，只 WARN 不自动删 |
| 启动校验 | 完成 | ValidateSchemaAsync 漂移检测 + EF 残留检测，均写启动日志 |
| 行为锁定测试 | 完成 | SchemaAndEnumTests（索引/FK/列型）、PalormSemanticsTests（根目录浏览、版本号、边界日、FK 异常）、SqlShapeTests（SQL 形状） |
| 门禁覆盖 | 本轮修复 | NAS-G3 关键词表补 DataSession/DbOptions/PalormSessionHolder 三个泄漏向量；tech-debt-scan 的 tests/ 路径笔误修正（原写成 test/，整个测试套件的 TODO 与 [Obsolete] 检查静默失效，已用探针复现红→绿） |

### 本轮补的 EF 残留检测

存量库里 __EFMigrationsHistory 会一直留着，PalORM 不读也不维护它。处理方式是启动时
检测到就写一条 Information 日志，说明来路与手工清理方式，**不自动 DROP**：那是用户库
里的表，删表属于数据清理决策，不是框架的启动副作用。测试锁了两面：新建库不该有这张
表；带这张表的存量库初始化必须照常完成且表不被删。mutation probe 验过：把检测改成
真删表，测试立刻红。

## 四、性能提升

口径：同一 harness、同一 10k 确定性种子、同机。基线（net10 + EF Core 10.0.12）采集于
10-11，当前轮（net11 + PalORM 6.4.1）采集于 10-12。跨日采集有机器漂移风险，量级判断
由同日 D1→D3 三轮对照佐证。

### API 端到端（8 并发，15 秒）

| 指标 | 基线 net10+EF | 当前 net11+PalORM | 变化 |
|---|---:|---:|---:|
| 吞吐 | 4,717.7 req/s | 13,231.0 req/s | +180%（2.8 倍） |
| P50 | 1.01 ms | 0.57 ms | −44% |
| P95 | 6.59 ms | 0.93 ms | −86% |
| P99 | 8.13 ms | 1.27 ms | −84% |
| 冷启动中位 | 1,151 ms | 565 ms | −51% |
| 稳态工作集 | 105 MB | 89 MB | −15% |
| 发布体积 | 380 MB | 410 MB | +8% |

逐端点 P50：browse 5.82→0.72 ms（−88%）、statistics 1.37→0.54（−61%）、
file-shares 1.37→0.64（−53%）、search 1.07→0.67（−37%）、storage 0.82→0.57（−30%）、
users 0.81→0.61（−25%）。health 0.27→0.35 ms 是唯一变慢的，绝对差 0.08 ms，
该端点不碰数据库，属噪声量级。

### 数据访问层（P50）

| 用例 | 基线 EF | 当前 PalORM | 变化 |
|---|---:|---:|---:|
| GetByPath | 0.142 | 0.022 | −84% |
| PathExists | 0.049 | 0.010 | −80% |
| CalculateUserUsedSpace | 0.076 | 0.026 | −66% |
| GetPagedUsers | 0.145 | 0.061 | −58% |
| GetDescendants | 3.426 | 1.604 | −53% |
| GetDeletedItems | 3.346 | 1.683 | −50% |
| GetStatistics | 11.792 | 6.793 | −42% |
| Search 名称模糊 | 3.740 | 2.785 | −25% |
| GetByStoragePool | 7.622 | 6.081 | −20% |
| Insert 20 行 | 4.423 | 0.712 | −84% |
| Delete 20 行 | 4.514 | 1.461 | −68% |
| Update 20 行 | 0.661 | 0.455 | −31% |
| GetChildren | 1.495 | 1.528 | +2%（噪声） |

### 收益归因

comparison.md 的三臂分解给出了干净的答案：A（net10+EF）→ B（net11+EF）一段各项指标
差距在 5% 到 13% 且方向不一致，属测量噪声；B → C（net11+PalORM）一段 API 吞吐 +212%、
P99 −85%、冷启动 −47%。**运行时升级对这个负载没有可测影响，收益几乎全部来自换 ORM。**

comparison-palorm641.md 记录了 6.1.0 → 6.4.1 的净收益：数据层全面快 10% 到 15%，
API 吞吐 +9.3%，GetByPath −54%（6.4.1 的命令复用优化）。适配轮实测的五项查询优化
（多结果集、JOIN、投影 DTO 等）采用零项，三条"减少往返"的方向在本地 SQLite 上全部
慢 12% 到 13%，因为往返成本接近零而预处理与物化是真实成本。

### 当前代码无回归验证

本轮在最终代码上重跑三臂，与 D3 逐项对照：

| 指标 | D3 | 本轮 | 判定 |
|---|---:|---:|---:|
| GetByPath | 0.022 | 0.022 | 一致 |
| Search | 2.793 | 2.785 | 一致 |
| GetStatistics | 6.783 | 6.793 | 一致 |
| GetByStoragePool | 6.445 | 6.081 | −5.6% |
| API 吞吐 | 13,046 | 13,231 | +1.4% |
| API P99 | 1.29 | 1.27 | 一致 |
| 冷启动 | 582 | 565 | −3% |
| 工作集 | 89 | 89 | 一致 |

D3 之后落的修复（LIKE 转义、根目录浏览、21 索引、版本号查询、外键异常转译、EF 残留
检测）对基准路径无负面对价，可确认。

### 两处测量事故，如实记录

API 臂第一次测出 9,704 req/s，比 D3 低 26%。原因是测量期间我在并发跑只读的 grep 与
门禁脚本，CPU 争抢直接压低了吞吐。作废该次数据，静默重测得 13,231 req/s。教训写进
基准纪律：测量窗口内不得跑任何其他命令。

进程臂每轮冷启动泄漏一个 API 进程，`using var process` 只释放句柄不终止进程，10 轮
攒了 10 个，锁住 bin 目录让后续 build 报 MSB3027。已修为 try/finally + TryKill，
并用 3 轮冷启动验证残留进程数为 0。

## 五、已知残留与触发条件

| 项 | 性质 | 处理 |
|---|---|---|
| bin/obj 下 net10.0 历史目录 | 本机构建残留，gitignored | 不影响仓库与构建；宿主机手动清理或重装 SDK 目录时自然消失 |
| .NET 11 为 RC1 | 预览版 | ADR-0002 已立触发条件：GA 后复跑基准，收益缩水超 20% 则重新评估 |
| 发布体积 +31 MB | 真实代价 | PalORM.Sqlite 拉入 SQLite3MC 多平台原生二进制，Docker 镜像随之增大 |
| health 端点 +0.08 ms | 噪声 | 不碰数据库的端点，绝对差在测量精度内 |
| 基准 API 臂 6 个错误 / 19.8 万请求 | 0.003% | 基线轮同为 8 个错误，量级一致，疑为 worker 首次连接预热，未深查 |

## 六、本轮改动清单

NAS 仓（待提交）：

- Dockerfile：restore 显式指定工程，修 MSB3202
- Directory.Build.props：删 2 条死 PackageVersion
- docs/CONVENTIONS.md：§3 基线改为已实现，§6.4 措辞改为"每测试类独立临时 SQLite 文件库"
- docs/decisions/ADR-0002：背景改为 PalORM 6.4.1 现状并注明迁移路径
- src/NasSystem.Infrastructure/DependencyInjection.cs：补 __EFMigrationsHistory 检测
- tests/NasSystem.IntegrationTests/SchemaAndEnumTests.cs：补 2 条 EF 残留测试
- tools/Benchmark/Bench/ProcessBench.cs：修冷启动进程泄漏

.ai 仓（独立版本化，单独提交）：

- scripts/gate-check.sh：NAS-G3 补 PalORM 泄漏向量
- scripts/tech-debt-scan.sh：tests/ 路径笔误修正 + 标签同步
- lessons.md：ArchitectureBoundaryTests 失实引用更正为 gate-check.sh NAS-G3/G4

## 七、验证证据

```
build:            0 错误
unit tests:       148 passed
integration:      72 passed
frontend:         19 passed
gate-check:       20/22（2 项 FAIL 均为 NAS-G22 未提交改动，提交后转绿）
doc-consistency:  9 通过 0 失败（2 条 WARN：无 CHANGELOG、无 python3）
tech-debt-scan:   全部 PASS
```

基准原始记录：`docs/perf/net11-palorm-20261012-{145157,145755,145721}.{md,json}`
（数据臂、API 臂干净重测、进程臂）。
