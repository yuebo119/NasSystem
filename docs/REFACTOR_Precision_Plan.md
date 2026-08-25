# 精炼执行方案：逐文件必要性判定与去除清单

> 基于全量 106 个源码文件的引用面扫描 + 领域事件发布/消费矩阵 + DI 注册/实际注入对照。
> 原则：**不是精简是精炼**——去除的是"零调用/纸面设计/过度工程"，保留的是扩展点。

## A 类：确定去除（零风险，零引用死代码）

| # | 文件 | 行数 | 去除理由 | 收益 |
|---|------|:--:|---------|------|
| A1 | `Shared/DependencyInjection.cs` | 21 | `AddSharedServices()` 是空方法——Shared 层无服务可注册，Program.cs 调用后什么都没发生 | -1 文件 |
| A2 | `Shared/JsonSerializerContext.cs` | 26 | 全项目零引用；AOT 声明已移除，JSON 序列化走默认反射 | -1 文件 |
| A3 | `Application/Files/FileService.cs` | 1 | partial 拆分后的残根占位文件 | -1 文件 |
| A4 | `Domain/Services/IDateTimeProvider.cs` | 43 | DI 注册了但**没有任何服务注入它**；全部代码直接用 `DateTime.UtcNow`（35 处基线已登记） | -1 文件 + 1 处 DI 注册行 |

**A 类小计：4 文件 / 91 行 / 1 处 DI 调用**

## B 类：确定去除（领域事件"从未被发布"的死定义）

对全部 27 个事件类定义做了发布方扫描（`AddDomainEvent(new XxxEvent`）：

**有发布的 9 个**：FileCreatedEvent, FileDeletedEvent, FileMovedEvent, FileRenamedEvent, FolderCreatedEvent, StoragePoolCreatedEvent, UserCreatedEvent, UserPasswordChangedEvent, UserRoleChangedEvent

**从未发布的 18 个**（全部去除）：

| # | 文件 | 行数 | 从未发布的事件 |
|---|------|:--:|--------------|
| B1 | `Domain/Events/ShareEvents.cs` | 33 | 全部 5 个（FileShareAccessed/Created/Disabled/Downloaded/Expired） |
| B2 | `Domain/Events/StorageEvents.cs`（部分） | ~20 | StorageLowSpaceWarningEvent, StoragePoolDeletedEvent（保留 StoragePoolCreatedEvent） |
| B3 | `Domain/Events/FileEvents.cs`（部分） | ~16 | FileDownloadedEvent, FileUploadCompletedEvent |
| B4 | `Domain/Events/UserEvents.cs`（部分） | ~12 | UserDeletedEvent, UserLoggedInEvent, UserLoggedOutEvent |

**B 类小计：~81 行死事件定义。保留理由不成立："预留"≠"扩展点"——真正的扩展点是 `IDomainEvent` 接口与 Dispatcher 机制，事件类本身只在有发布方时才有意义。**

## C 类：架构重组（位置不对，不是死代码）

| # | 现状 | 问题 | 动作 |
|---|------|------|------|
| C1 | `Application/Thumbnails/` 整个目录只有 `IThumbnailService.cs` 一个接口 | 一个目录一个接口的过度拆分；缩略图是文件域子能力不是独立用例域 | 移入 `Domain/Services/`（实现已在 `Infrastructure/Files/`），删除空目录 |
| C2 | `FileShareErrors` 定义在 `FileShareService.cs` 尾部（593-677 行） | 与 `ApplicationErrors.cs`（110 行，已有 File/User/Storage 错误）双轨并行 | 合并到 `ApplicationErrors.cs` |
| C3 | `StorageErrors` 定义在 `StorageService.cs` 尾部 | 同上 | 合并到 `ApplicationErrors.cs` |
| C4 | `Result.cs` 含 `Match/MatchAsync/Map/Bind` 函数式组合子（~70 行） | **全项目零调用**（grep 确认）；项目只用 Success/Failure/IsSuccess/IsFailure/Value/Error 六个成员 | 裁减到使用面；保留类型签名完整（不破坏接口） |

**C 类收益：-~100 行过度工程代码 + 错误码单一真源 + 消除一目录一文件的碎片化**

## D 类：评估后保留（有扩展价值）

| 文件 | 保留理由 |
|------|---------|
| `FilePermission` 实体 + Configuration | 有 DbSet 映射 + 迁移历史；虽服务层零使用，但细粒度权限是 NAS 的必然演进方向（NTFS ACL 类比），实体模型是扩展点 |
| `IFilePermissionRepository` + 实现 | 虽当前零调用，但删除后未来接入权限需重建；保留成本极低（CRUD 样板已由 RepositoryBase 消化） |
| `HashValue` 值对象 | 被 `IFileHasher` 接口返回类型引用——接口契约的一部分，非死代码 |
| `ValueObject` 基类 + `Entity` 基类 | DDD 战术模式的根基，去除将破坏全部值对象/实体 |
| `PagedResult` | 分页契约被前端消费 |
| `ByteSizeFormatter` | 单一真源，六处消费方 |
| EF Migrations 三个文件 | 框架生成物，不可手工去除 |

## 总计

| 类型 | 文件数 | 行数 |
|------|:------:|:----:|
| A 类（死文件去除） | 4 | -91 |
| B 类（死事件去除） | 0（行级） | -~81 |
| C 类（重组/裁减） | 0（行级） | -~100 |
| **合计** | **-4 文件** | **~-272 行** |

去除后全量：106 → 102 文件 / 13854 → ~13582 行（**-2.0%**）——去除的不是功能，是零调用的占位与过度工程。
