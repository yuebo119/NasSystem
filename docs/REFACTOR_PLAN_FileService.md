# FileService 拆分执行蓝图（ITM-T1 · 独立重构专项）

> 本蓝图是综合评审报告"可维护性欠账"的执行方案。**状态：待实施**。
> 之所以列为独立专项而非夹带执行：涉及接口签名、端点注入、约 40 个测试方法迁移的级联修改，
> 触发 Runaway Refactor 防线——必须在无其他变更的干净批次中一次性完成。

## 现状

`src/NasSystem.Application/Files/FileService.cs` 约 1000 行、17 个公开操作、六类职责混杂。
历轮审计确认其功能正确性已被 40+ 测试锚定，因此本重构是**纯结构移动**：方法体逐块搬迁、行为零变更。

## 目标形态：按用例域拆分为四个服务

| 新服务 | 接口 | 迁入方法 | 依赖 |
|--------|------|---------|------|
| `FileBrowseService` | `IFileBrowseService` | GetDirectoryContentsAsync / GetFileInfoAsync / SearchAsync / GetStatisticsAsync / BuildBreadcrumbs / GetFileName | IFileItemRepository, IFileSystemService, IHubNotificationService(不需要→移除), IUnitOfWork |
| `FileTransferService` | `IFileTransferService` | CreateFolderAsync / UploadFileAsync / DownloadFileAsync / RenameAsync / MoveAsync / CopyAsync / CopyFileAsync / CopyFolderRecursiveAsync / GenerateUniqueCopyNameAsync / RewriteDescendantPathsAsync / NotifyChangedAsync | 全部现有依赖 |
| `FileRecycleService` | `IFileRecycleService` | DeleteAsync / PermanentDeleteAsync / GetRecycleBinAsync / RestoreAsync / EmptyRecycleBinAsync | IFileItemRepository, IFileSystemService, IUnitOfWork, IHubNotificationService |
| `FileSyncService` | `IFileSyncService` | ScanAndSyncAsync / ScanDirectoryAsync / ScanCounters | IFileItemRepository, IStoragePoolRepository, IFileSystemService, IUnitOfWork |

## 共享构件去置

| 构件 | 去向 | 理由 |
|------|------|------|
| NormalizePath | 已委托 FilePath.Normalize ✓ | 单一真源已完成 |
| CombinePath / GetParentPath / GetPhysicalPath | `Application/Files/VirtualPaths.cs` internal static 类 | 纯函数、多服务共用；GetPhysicalPath 含挂载根拼接语义需集中 |
| MapToDto | `FileItemMapper.Map` static 类（或 DTO 扩展方法） | 与 DTO 同目录同生命周期 |
| ValidateName 调用 | 迁移至 Transfer 服务入口 + 实体内保留 | 双防线不变 |
| NotifyChangedAsync | 各服务私有实现（经构造注入 IHubNotificationService） | 每服务推送自己的域事件 |

## 端点与 DI 迁移

- `FileEndpoints.cs`：各 handler 的注入参数从单一 `IFileService` 改为对应新接口；
  路由路径与响应契约**完全不变**（前端零感知）
- `DependencyInjection`（Application）：注册四个 Scoped 服务
- 门面取舍：**不保留** `IFileService` 聚合门面——双入口会造成调用方歧义；
  如需聚合语义由端点层组合表达

## 测试迁移

- `FileServiceTests.cs`（约 40 例）按 SUT 拆为 `FileBrowseServiceTests` /
  `FileTransferServiceTests` / `FileRecycleServiceTests`；断言不变仅换构造与类型
- `SoftDeleteScanInteractionTests` 等集成测试零改动（走 HTTP 层）

## 执行顺序与验收标准

1. 新建四接口四实现（空壳继承编译通过）
2. 方法逐组搬迁，每搬一组跑全量测试
3. 端点与 DI 切换，删除旧 FileService/IFileService
4. 测试文件重组
5. **验收**：build 0 错误；225+ 测试全绿；五防线 exit 0；grep 确认 `IFileService` 零残留；
   ITM-N32 登记 + CONVENTIONS §1 分层图更新
