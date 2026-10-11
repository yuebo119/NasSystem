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
/// 同时建 <c>__EFMigrationsHistory</c> 并预置 4 条迁移 ID，
/// 使被测 API 启动时的 MigrateAsync 成为 no-op（否则会尝试重放 DropFilePermissions 而失败）。
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
        """
        CREATE TABLE "__EFMigrationsHistory" (
            "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
            "ProductVersion" TEXT NOT NULL
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
        "CREATE UNIQUE INDEX \"IX_FileItems_Path\" ON \"FileItems\" (\"Path\") WHERE [IsDeleted] = 0",
        "CREATE INDEX \"IX_FileItems_Path_Prefix\" ON \"FileItems\" (\"Path\" COLLATE NOCASE) WHERE \"IsDeleted\" = 0",
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
    ];

    /// <summary>预置的迁移 ID，使被测 API 启动迁移成为 no-op。</summary>
    public static readonly (string Id, string ProductVersion)[] MigrationHistory =
    [
        ("20260825080018_InitialBaseline", "10.0.12"),
        ("20260825083000_AddBusinessUniqueIndexes", "10.0.12"),
        ("20260929012902_DropFilePermissions", "10.0.12"),
        ("20260929020525_AddPathPrefixIndex", "10.0.12"),
    ];
}
