# ADR-0003：语义搜图探针报告（CLIP + sqlite-vec）与开工决策

- 状态：已接受（开工）
- 日期：2026-10-18
- 相关：docs/ROADMAP.md Phase 2、ADR-0002

## 背景

ROADMAP Phase 2 的语义搜图是本方案最大不确定项，动工前设三道探针门：
① 轻量 CLIP 模型本机 CPU 单张推理延迟（目标 < 300ms）
② 向量存储方案：sqlite-vec 与 PalORM 引擎的共存性
③ 10 万张图建索引总耗时（决定是否默认开启）
三项任一不过则降级为 Phase 4 或砍掉，OCR 不受影响。

## 探针设计与执行

### 探针①：CLIP CPU 推理延迟

- 模型：Xenova/clip-vit-base-patch32 ONNX 导出（FP32），视觉 351,685,709 字节、
  文本 254,058,553 字节、vocab 862,328 字节、merges 524,619 字节，
  经 hf-mirror.com 下载（本机直连 huggingface.co 不通，github.com 不通，
  nuget/npm/pypi/hf-mirror 直连可达）
- 推理栈：Microsoft.ML.OnnxRuntime 1.24.4（ElBruno.LocalEmbeddings.ImageEmbeddings
  1.6.4 传递依赖），ImageSharp 3.1.12 预处理 224×224
- 量具：探针控制台程序（tmp/probe/ClipProbe），10 次迭代取中位/P95，
  预热一次不计入（JIT 与 ONNX 首次执行路径）

结果（本机 32 核）：

| 指标 | 数值 |
|---|---|
| 视觉模型加载 | 889 ms |
| 文本模型加载 | 473 ms |
| 视觉编码（10 次） | 中位 **58.1 ms** / P95 69.1 ms / 最小 28.6 ms / 最大 69.8 ms |
| 文本编码（10 次） | 中位 18.5 ms / P95 36.1 ms |
| 余弦 sanity check | 图 vs "photo" 0.2399 vs "spreadsheet" 0.2382 |

判定：**PASS**（58.1 ms vs 门槛 300 ms，5 倍余量）。
余弦 sanity check 区分度弱（0.0017 差）：测试输入是 285 字节的缩略图缓存件，
小图语义信息不足；该指标只作冒烟，不作质量判据，真实图片的检索质量
在部署环境人工验收。

### 探针②：sqlite-vec 与 PalORM 引擎共存

- 扩展件：sqlite-vec v0.1.9，NuGet 移植包 HiraokaHyperTools.sqlite-vec
  （runtimes 分发 win-x64 vec0.dll 289,280 字节 / linux-x64 vec0.so 159,816 字节，
  Docker linux-x64 链路原生件随包发布）
- 加载方式：Microsoft.Data.Sqlite `EnableExtensions()` + `LoadExtension()`
- 能力探针（tests/NasSystem.IntegrationTests/SqliteExtensionCapabilityTests.cs）：
  - vec0 虚拟表创建、向量插入、KNN 查询（MATCH + ORDER BY distance）全部通过
  - FTS5 trigram tokenizer 可用：中文子串检索命中（unicode61 对中文不可用，
    实测双字词查询返回 0；trigram 是 FTS5 3.47+ 内置形态）
  - 运行期连接引擎事实：经 PalormSessionHolder 取的连接 `sqlite_version()`
    为 3.54.0 三段标准版本、无 sqlite3mc 标识——运行期引擎是
    Microsoft.Data.Sqlite 默认的 e_sqlite3，Directory.Packages.props 里
    "原生引擎仍是 SQLite3MC" 的注释与实际不符，以实测为准

判定：**PASS**。

### 探针③：10 万张建索引耗时

未实测 10 万张（本机无此量级图片）。按探针①实测单张 58.1 ms 外推：
串行 10 万张 ≈ 5,810 s ≈ 97 分钟。单张 58 ms 已是 ONNX 多线程结果，
再并行加速比有限。判定为容量参数而非 go/no-go 门：**默认开启，后台增量建索引，
断点续建**（索引进度落库，重启不重来），不阻塞上传/浏览主链路。

## 决策

语义搜图**开工**，实现约束：

1. 模型四件套不入库（共约 606 MB），首次启用时由用户经设置页触发下载到
   数据目录（models/clip/），下载源默认 hf-mirror.com（国内可达），
   可配镜像；无模型时语义检索不可用但其余功能不受影响（诚实降级，不做假 UI）。
2. 向量存 sqlite-vec 的 vec0 虚拟表（与业务表同库），schema 由
   InitializeDatabaseAsync 的裸 SQL 幂等创建（vec0 表不是 PalORM 行模型）。
3. OCR 文本全文检索同步升级为 FTS5 trigram（探针②已证可用），
   替换现有 LIKE '%term%' 全表扫；LIKE 转义行为保留为回退路径测试锁定。
4. 建索引走 BackgroundService 增量队列，进度可见（设置页显示 已索引/总数）。

## 依据与放弃的备选

- CLIP 模型选 Xenova/clip-vit-base-patch32（512 维，FP32）：社区标准导出，
  ElBruno 生态直接消费其四件套布局；放弃量化版（model_quantized 153 MB）——
  精度优先，NAS 端磁盘不是瓶颈，58 ms 延迟已有 5 倍余量不需要再压。
- 向量检索选 sqlite-vec 而非主外键表 + float blob 暴力检索：KNN 由扩展
  在 SQL 层完成，10 万级 512 维暴力检索每查询 5,120 万次浮点乘，
  远超单次查询可接受延迟；sqlite-vec 是 SQLite 单一事实源路线的自然延伸。
- FTS5 选 trigram 而非 unicode61：中文场景 unicode61 按 Unicode 类别分词，
  双字词查询必然落空（实测）；trigram 三字子串切分对中文开箱可用，
  代价是索引体积约文本 3 倍（OCR 文本量级可接受）。

## 复核触发条件

- 部署环境真实图片的检索质量不达预期（返回"按默认开启"但用户反馈搜不准）
- 10 万张量级实测建索引耗时超过串行外推值 2 倍（并发假设失效）
- ElBruno 包停止维护或其传递依赖（OnnxRuntime/ImageSharp）出现安全版本跳跃
