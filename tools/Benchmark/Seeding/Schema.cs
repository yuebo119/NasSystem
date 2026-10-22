// =============================================================================
// NasSystem.Benchmark - Schema.cs
// 种子库 DDL：逐字复制自"当前迁移链在空库上生成的 schema"
// =============================================================================

namespace NasSystem.Benchmark.Seeding;

/// <summary>
/// 建表与建索引 DDL。来源是 2026-10-12 用项目自身
/// <c>DependencyInjection.InitializeDatabaseAsync</c> 在空库上跑完整迁移链后
/// 从 <c>sqlite_master</c> 导出的原文（含两个部分唯一索引与 Path 前缀 NOCASE 索引）。
/// </summary>
/// <remarks>
/// 为什么种子库不用 ORM 自带的 EnsureCreated/MigrateAsync 建：
/// 种子库必须对两个 ORM 完全对称，任何一方自建 schema 都会引入模型差异。
/// 这里用裸 DDL，两个臂看到的库逐字节同构。
/// </remarks>
public static class Schema
{
    public static readonly string[] Tables =
    [
        """
        CREATE TABLE "FileItems" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_FileItems" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "Path" TEXT NOT NULL,
            "PhysicalPath" TEXT NOT NULL,
            "ItemType" INTEGER NOT NULL,
            "SizeBytes" INTEGER NOT NULL,
            "MimeType" TEXT NULL,
            "Extension" TEXT NULL,
            "Hash" TEXT NULL,
            "StoragePoolId" TEXT NOT NULL,
            "OwnerId" TEXT NOT NULL,
            "ParentId" TEXT NULL,
            "IsDeleted" INTEGER NOT NULL,
            "DeletedAt" TEXT NULL,
            "IsHidden" INTEGER NOT NULL,
            "IsSystem" INTEGER NOT NULL,
            "IsReadOnly" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL,
            CONSTRAINT "FK_FileItems_FileItems_ParentId" FOREIGN KEY ("ParentId") REFERENCES "FileItems" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_FileItems_StoragePools_StoragePoolId" FOREIGN KEY ("StoragePoolId") REFERENCES "StoragePools" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_FileItems_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
        )
        """,
        """
        CREATE TABLE "FileShares" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_FileShares" PRIMARY KEY,
            "ShareCode" TEXT NOT NULL,
            "FileItemId" TEXT NOT NULL,
            "AccessType" INTEGER NOT NULL,
            "PasswordHash" TEXT NULL,
            "Permission" INTEGER NOT NULL,
            "ExpiresAt" TEXT NULL,
            "MaxDownloads" INTEGER NULL,
            "DownloadCount" INTEGER NOT NULL,
            "CreatedByUserId" TEXT NOT NULL,
            "IsEnabled" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL,
            CONSTRAINT "FK_FileShares_FileItems_FileItemId" FOREIGN KEY ("FileItemId") REFERENCES "FileItems" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_FileShares_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
        )
        """,
        """
        CREATE TABLE "FileVersions" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_FileVersions" PRIMARY KEY,
            "FileItemId" TEXT NOT NULL,
            "VersionNumber" INTEGER NOT NULL,
            "StoragePath" TEXT NOT NULL,
            "FileSize" INTEGER NOT NULL,
            "FileHash" TEXT NOT NULL,
            "Comment" TEXT NULL,
            "IsCurrent" INTEGER NOT NULL DEFAULT 0,
            "CreatedByUserId" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL,
            CONSTRAINT "FK_FileVersions_FileItems_FileItemId" FOREIGN KEY ("FileItemId") REFERENCES "FileItems" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_FileVersions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
        )
        """,
        """
        CREATE TABLE "StoragePools" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_StoragePools" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "MountPath" TEXT NOT NULL,
            "Description" TEXT NULL,
            "IsDefault" INTEGER NOT NULL,
            "IsEnabled" INTEGER NOT NULL,
            "TotalBytes" INTEGER NOT NULL,
            "UsedBytes" INTEGER NOT NULL,
            "MaxCapacityBytes" INTEGER NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL
        )
        """,
        """
        CREATE TABLE "Users" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
            "Username" TEXT NOT NULL,
            "PasswordHash" TEXT NOT NULL,
            "PasswordSalt" TEXT NOT NULL,
            "DisplayName" TEXT NULL,
            "Email" TEXT NULL,
            "Role" INTEGER NOT NULL,
            "IsEnabled" INTEGER NOT NULL,
            "LastLoginAt" TEXT NULL,
            "QuotaBytes" INTEGER NULL,
            "UsedBytes" INTEGER NOT NULL,
            "HomeDirectory" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL
        )
        """,
    ];

    public static readonly string[] Indexes =
    [
        "CREATE INDEX \"IX_FileItems_Hash\" ON \"FileItems\" (\"Hash\")",
        "CREATE INDEX \"IX_FileItems_IsDeleted\" ON \"FileItems\" (\"IsDeleted\")",
        "CREATE INDEX \"IX_FileItems_OwnerId\" ON \"FileItems\" (\"OwnerId\")",
        "CREATE INDEX \"IX_FileItems_ParentId\" ON \"FileItems\" (\"ParentId\")",
        "CREATE INDEX \"IX_FileItems_ParentId_Name\" ON \"FileItems\" (\"ParentId\", \"Name\")",
        // 路径索引四件套与生产 DDL（DependencyInjection.cs 启动期幂等 DDL）逐条对齐。
        // 2026-10-22 优化轮实测发现夹具此前与生产漂移：夹具把 IX_FileItems_Path 建成了
        // 「唯一部分索引 WHERE IsDeleted=0」，而生产里这个名字是**全量 BINARY** 索引，
        // 唯一部分索引另名 UX_FileItems_Path_Active。漂移的后果是夹具上任何不带
        // IsDeleted 条件的路径查询（子树软删/恢复/路径重写/快照采集）都用不上索引，
        // 实测同一子树查询在漂移夹具上要 12.9ms 全扫、在生产同构索引下只要 0.17ms——
        // 用夹具做性能判断会得出与生产相反的结论（本轮就被误导过一次）
        "CREATE UNIQUE INDEX \"UX_FileItems_Path_Active\" ON \"FileItems\" (\"Path\") WHERE \"IsDeleted\" = 0",
        "CREATE INDEX \"IX_FileItems_Path\" ON \"FileItems\" (\"Path\")",
        "CREATE INDEX \"IX_FileItems_Path_Nocase\" ON \"FileItems\" (\"Path\" COLLATE NOCASE)",
        "CREATE INDEX \"IX_FileItems_Path_Prefix\" ON \"FileItems\" (\"Path\" COLLATE NOCASE) WHERE \"IsDeleted\" = 0",
        "CREATE INDEX \"IX_FileItems_OwnerId_CreatedAt\" ON \"FileItems\" (\"OwnerId\", \"CreatedAt\" DESC) WHERE \"IsDeleted\" = 0",
        "CREATE INDEX \"IX_FileItems_StoragePoolId\" ON \"FileItems\" (\"StoragePoolId\")",
        "CREATE INDEX \"IX_FileShares_CreatedByUserId\" ON \"FileShares\" (\"CreatedByUserId\")",
        "CREATE INDEX \"IX_FileShares_FileItemId\" ON \"FileShares\" (\"FileItemId\")",
        "CREATE UNIQUE INDEX \"IX_FileShares_ShareCode\" ON \"FileShares\" (\"ShareCode\")",
        "CREATE INDEX \"IX_FileVersions_CreatedByUserId\" ON \"FileVersions\" (\"CreatedByUserId\")",
        "CREATE INDEX \"IX_FileVersions_FileItemId\" ON \"FileVersions\" (\"FileItemId\")",
        "CREATE INDEX \"IX_FileVersions_FileItemId_IsCurrent\" ON \"FileVersions\" (\"FileItemId\", \"IsCurrent\")",
        "CREATE UNIQUE INDEX \"IX_FileVersions_FileItemId_VersionNumber\" ON \"FileVersions\" (\"FileItemId\", \"VersionNumber\")",
        "CREATE INDEX \"IX_StoragePools_IsDefault\" ON \"StoragePools\" (\"IsDefault\")",
        "CREATE UNIQUE INDEX \"IX_StoragePools_MountPath\" ON \"StoragePools\" (\"MountPath\")",
        "CREATE UNIQUE INDEX \"IX_StoragePools_Name\" ON \"StoragePools\" (\"Name\")",
        "CREATE UNIQUE INDEX \"IX_Users_Email\" ON \"Users\" (\"Email\") WHERE [Email] IS NOT NULL",
        "CREATE INDEX \"IX_Users_IsEnabled\" ON \"Users\" (\"IsEnabled\")",
        "CREATE UNIQUE INDEX \"IX_Users_Username\" ON \"Users\" (\"Username\")",
        // 注意：本夹具只声明 5 张热路径表（FileItems/FileShares/FileVersions/
        // StoragePools/Users），生产还有 BackupDevices/DirectorySnapshots/
        // FileOcrTexts/ProtectedDirectories/SnapshotEntries/SystemSettings 等表
        // 及其索引。那些表不在夹具范围内（基准不跑它们的路径），故此处不声明
        // —— 一致性由 BenchmarkFixtureSchemaTests 按「夹具声明的表」逐表锁定
    ];

}
