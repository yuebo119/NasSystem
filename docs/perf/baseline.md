# NAS 性能基线报告（阶段 1：net10.0 + EF Core 10.0.12）

## 一、报告定位

本文件是 .NET 11 + PalORM 迁移的**基线**，与后续 `comparison.md` 配对阅读。全部数字由
`tools/Benchmark` 生成，可一条命令复现：

```bash
dotnet run --project tools/Benchmark -- seed --rows 10000 --assert
dotnet run --project tools/Benchmark -- data --rows 10000 --warmup 200 --iterations 500 --rounds 3
dotnet run --project tools/Benchmark -- api  --rows 10000 --concurrency 8 --duration 15 --port 6100
dotnet run --project tools/Benchmark -- proc --rows 10000 --coldstarts 10 --port 6200
```

原始记录（含每条样本的 JSON）：`docs/perf/net10-ef-20261011-184714.{md,json}`（数据层）、
`-185025`（API）、`-190829`（进程级）。

| 项 | 值 |
|---|---|
| 目标框架 | .NET 10.0.12 |
| ORM | EF Core 10.0.12 |
| Git SHA | 0fad354 |
| 机器 | DESKTOP-IDCGOGO，Windows NT 10.0.19045.0，32 逻辑核 |
| 种子数据 | 10,000 行 FileItems + 100 用户 + 5 存储池 + 200 分享，固定随机种子 20261012 |
| 采集时间（UTC） | 2026-10-11 18:47 / 18:50 / 19:08 |

## 二、数据访问层（仓储接口级，ORM 臂 vs 裸 ADO.NET 地板臂）

口径：同进程内两臂交错执行，每轮交换起始臂；预热 200 次，测量 500 次 × 3 轮；读用例共享同一份库，
写用例在可复用库上进行、每次迭代前做一次不计入测量的重置。ORM 臂经 `IFileItemRepository` 等
Domain 接口调用，地板臂为等价裸 SQL。

| 用例 | ORM P50 (ms) | 地板 P50 (ms) | 比值 | ORM P99 (ms) | 地板 P99 (ms) |
|---|---:|---:|---:|---:|---:|
| GetByPath | 0.142 | 0.025 | 5.60× | 0.211 | 0.040 |
| GetChildren | 1.495 | 0.842 | 1.78× | 3.442 | 1.856 |
| Search 名称模糊 | 3.740 | 3.588 | 1.04× | 4.709 | 4.281 |
| GetStatistics | 11.792 | 9.197 | 1.28× | 14.116 | 11.092 |
| GetDescendants | 3.426 | 3.295 | 1.04× | 4.319 | 3.979 |
| GetPagedUsers | 0.145 | 0.035 | 4.20× | 0.696 | 0.061 |
| CalculateUserUsedSpace | 0.076 | 0.029 | 2.63× | 0.115 | 0.040 |
| PathExists | 0.049 | 0.010 | 4.67× | 0.075 | 0.015 |
| GetByStoragePool | 7.622 | 5.116 | 1.49× | 11.716 | 8.544 |
| GetDeletedItems | 3.346 | 0.962 | 3.48× | 4.569 | 1.547 |
| CalculateStoragePoolUsedSpace | 1.814 | 1.756 | 1.03× | 2.366 | 2.212 |
| Insert 20 行（写） | 4.423 | 2.980 | 1.48× | 7.798 | 4.013 |
| Update 20 行（写） | 0.661 | 2.606 | 0.25× | 1.200 | 3.567 |
| Delete 20 行（写） | 4.514 | 3.614 | 1.25× | 5.947 | 5.835 |

三点观察：

- **EF 的开销集中在固定成本，不在数据量。** 单行查询类（GetByPath、PathExists、GetPagedUsers、
  CalculateUserUsedSpace）比值 2.6 到 5.6 倍，但这些用例本身只有 0.01 到 0.15 ms；返回多行的用例
  （Search、GetDescendants、CalculateStoragePoolUsedSpace）比值回落到 1.0 到 1.5 倍。
- **GetStatistics 是全项目最重的单点**（11.8 ms），它一次发 5 条聚合 SQL 加 1 条分组查询，
  地板臂同形态也要 9.2 ms，说明成本主要在 SQLite 服务端而非 ORM。
- **Update 是唯一 EF 明显更快的一档**（0.25 倍）。EF 把 20 条 UPDATE 打包成单条命令一次往返，
  地板臂逐条执行付了 20 次往返。

## 三、API 端到端（真实 Kestrel 子进程）

口径：`dotnet` 直接起 API 进程（非 TestServer），登录取 JWT 后 8 个并发 worker 打 7 个端点，
持续 15 秒。库为 10k 种子库副本，只读。

- 总请求 70,827，错误 8（0.011%），吞吐 **4,717.7 req/s**
- 整体延迟：P50 1.01 ms，P95 6.59 ms，P99 8.13 ms，max 144.99 ms

| 端点 | 请求数 | P50 (ms) | P95 (ms) | P99 (ms) |
|---|---:|---:|---:|---:|
| `/api/v1/system/health` | 10,172 | 0.27 | 0.44 | 0.84 |
| `/api/v1/users` | 10,170 | 0.81 | 1.43 | 2.01 |
| `/api/v1/storage/overview` | 10,168 | 0.82 | 1.34 | 1.86 |
| `/api/v1/files/search` | 10,166 | 1.07 | 1.81 | 2.34 |
| `/api/v1/file-shares` | 10,167 | 1.37 | 2.36 | 2.98 |
| `/api/v1/files/statistics` | 10,167 | 1.37 | 2.36 | 2.98 |
| `/api/v1/files/browse/*` | 10,165 | 5.82 | 8.18 | 9.31 |

`/files/browse` 是最重端点（P50 5.82 ms），它要取目录内容并组装列表；`/files/statistics`
在 HTTP 层只排到 1.37 ms，说明其 11.8 ms 的服务端聚合成本在当前并发下被连接复用与缓存摊薄。

## 四、进程级

| 指标 | 值 |
|---|---|
| 冷启动到首个健康检查通过（10 次中位） | 1,151 ms |
| 稳态工作集 | 105 MB |
| 托管堆 | 3 MB |
| Release 发布体积 | 380 MB（187 个文件） |

冷启动包含 EF 首次模型构建与迁移历史检查，是迁移后最可能变化的指标之一。

## 五、口径与已知限制

- 绝对值仅在本机本轮有效，跨机器只比较比值；跨轮比较需同机同 SDK。
- 写用例的可复用库方案：每次迭代前重置（Insert 删掉自己插入的行，Delete 补回 20 行），
  重置不计时。表规模保持恒定，两臂对称。
- 数据访问层不含 DI 与 HTTP 管线；端到端差异看第三节。
- 地板臂为裸 ADO.NET 等价 SQL，不构造领域对象，是上限参照而非竞品。
- **本机 `dotnet test` 的 MTP 宿主集成是坏的**（存量问题，与本次改动无关，已用 stash 验证）：
  报"运行了零个测试"、退出码 5。直接跑测试可执行文件则全部通过：
  单元 148 个 + 集成 54 个，0 失败。
- 种子库的 Guid 一律写**大写** D 格式。Microsoft.Data.Sqlite 绑定 Guid 参数时产出大写，
  而 SQLite 的 TEXT 比较大小写敏感；若种子写小写，ORM 侧外键检查会匹配不上父行，
  表现为 `SQLite Error 19: FOREIGN KEY constraint failed`。这是本项目存量库的格式事实，
  也是替换 ORM 时必须保持的约定。

## 六、下一步

按方案进入阶段 2：只把 `TargetFramework` 改到 net11.0（EF Core 不动），复跑同一套 harness，
得到 B 臂（隔离运行时影响）；再做阶段 3 的 PalORM 替换得到 C 臂。三臂差值即收益归因。
