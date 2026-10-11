# ADR-0002：数据访问层由 EF Core 迁移至 PalORM

- 状态：已接受
- 日期：2026-10-12
- 相关：ADR-0001、docs/perf/baseline.md、docs/perf/comparison.md

## 背景

项目自 2026-01 起用 EF Core 10 + SQLite。2026-08-25 的 ITM-N8 裁决记录了一条结论：
EF Core 动态查询不兼容 Native AOT，因而移除了 `IsAotCompatible` 的假声明。
2026-10-12 决定把数据访问层换成 PalORM 6.1.0（自研微 ORM，编译期生成），
同时把目标框架从 .NET 10 升到 .NET 11。

动机有三个，按权重排序：拆掉 AOT 的最大阻断、降低数据层运行时开销、
把自研库放进真实项目验证。

## 决策

采用**影子持久化模型**路线：在 `NasSystem.Infrastructure.Persistence` 建 5 个带
`[Table]` 注解的行模型承接映射，Domain 实体保持零外部依赖，只增加 `internal static Restore`
工厂用于从持久化状态恢复。

## 依据

### 为什么不是让领域实体直接挂注解

领域实体是私有 setter 加私有无参构造的 DDD 形状。PalORM 的生成器要求公共无参构造与
可写属性，编译器直接报 CS0310。要直接挂注解就得把 5 个实体改成贫血可写模型，
破坏封装，还要改 `CONVENTIONS §1.1` 与 gate NAS-G1（Domain csproj 零 PackageReference）。
影子模型把这些代价限制在基础设施层内，Domain 与 66 个单测零改动。

### 为什么不用 PalORM 的 `[SoftDelete]`

该特性硬绑定 `deleted_at` 列（规则 PALORM014）。本 schema 是 `IsDeleted` 位加
`DeletedAt` 时间戳，且唯一部分索引写作 `WHERE [IsDeleted] = 0`。
改用 `deleted_at` 会牵动索引定义与全部查询谓词，收益却只是少写一个条件。

### 为什么保留意图队列而不是让仓储各自提交

应用层有 28 处"改几个仓储，最后 SaveChangesAsync 一次"的调用形态，依赖
"一次调用 = 一个隐式事务"。改成每条语句独立提交会让中途失败留下半成品状态，
且 Application 层要大改。`PalormUnitOfWork` 用意图队列保住这个契约，
领域事件也从 ChangeTracker 扫描改为队列自己记录。

### 收益数据

三臂同机对比（详见 comparison.md）：

| 指标 | net10+EF | net11+EF | net11+PalORM |
|---|---:|---:|---:|
| API 吞吐 | 4,718 req/s | 4,470 req/s | 13,944 req/s |
| API P99 | 8.13 ms | 8.03 ms | 1.17 ms |
| 冷启动中位 | 1,151 ms | 1,076 ms | 566 ms |
| 稳态工作集 | 105 MB | 107 MB | 86 MB |
| Release 体积 | 380 MB | 380 MB | 411 MB |

运行时升级（net10 → net11）的影响在噪声量级，收益几乎全部来自换 ORM。
写入类改善最大：Insert 20 行从 4.42 ms 降到 0.72 ms。

## 后果

**得到**：API 吞吐约 3 倍、P99 下降 85%、冷启动减半、稳态内存降 18%。
数据层不再依赖 EF 的变更追踪与表达式树翻译，AOT 的最大阻断拆除。

**付出**：

1. 27 个查询方法从 LINQ 改成手写 SQL 字符串。类型安全从编译期检查退到
   PALORM 的 44 条编译期诊断加人工 review。
2. 多一层影子模型与映射代码，出错面从查询逻辑移到映射逻辑。
3. Release 发布体积增加 31 MB（SQLite3MC 多平台原生包）。
4. `Microsoft.Data.Sqlite` 从 10.0.12 升到 11.0.0-rc.1（PalORM.Sqlite 的依赖）。
5. 集成测试从 EF InMemory 改为每类独立临时 SQLite 文件库，慢了约 0.3 秒。
6. PalORM 总下载量四位数，排错资料少，出问题只有仓库 issues 一条路。

**顺带修掉的存量缺陷**：EF InMemory 不校验唯一索引，藏住了一个测试缺陷——
种子辅助重复建同名目录会撞 `FileItems.Path` 的唯一部分索引。换真库后暴露并已修。

## 放弃的替代方案

| 方案 | 放弃原因 |
|---|---|
| 实体直接挂 PalORM 注解 | 破坏 DDD 封装与 Domain 纯净门禁 |
| 用 `[SoftDelete]` | 需改 schema 与全部索引谓词，收益不抵代价 |
| 仓储各自立即提交，去掉工作单元 | 破坏 28 处调用点的事务契约 |
| 保留 EF，只做 `dotnet ef dbcontext optimize --precompile-queries` | EF AOT 仍是实验性，且拿不到运行时开销的收益 |
| 等 .NET 11 GA 再动手 | GA 在 2026-11-10，等待期无收益；RC1 已可用，GA 后只需重跑基准确认 |

## 复核触发条件

出现以下任一情况应重新评估本决策：

- .NET 11 GA 后基准复跑，收益相对 RC1 缩水超过 20%
- PalORM 出现阻塞性缺陷且一个月内无修复
- 需要接入 SQLite 之外的数据库（当前影子模型按 PascalCase SQLite schema 定制）
- 项目要对外分发且 AGPL-3.0-only 许可成为障碍（PalORM 全系 AGPL）
