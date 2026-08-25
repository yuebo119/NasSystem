using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

using NasSystem.Infrastructure.Data;

#pragma warning disable CA1814 // 逐行建表优于拆分——基线场景无大表

namespace NasSystem.Infrastructure.Migrations;

/// <summary>
/// 业务唯一性兜底索引（幂等 SQL）：
/// 存量 EnsureCreated 库打基线标记后会应用本迁移补齐最新唯一索引；
/// 新库在 InitialBaseline 已含同名索引，IF NOT EXISTS 幂等跳过。
/// ITM-N7：TOCTOU 的数据库兜底。
/// </summary>
[DbContext(typeof(NasDbContext))]
[Migration("20260825083000_AddBusinessUniqueIndexes")]
public sealed class AddBusinessUniqueIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_FileItems_Path ON FileItems(Path) WHERE [IsDeleted] = 0;");
        migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_FileShares_ShareCode ON FileShares(ShareCode);");
        migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users(Username);");
        migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_FileVersions_FileItemId_VersionNumber ON FileVersions(FileItemId, VersionNumber);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_FileItems_Path;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_FileShares_ShareCode;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Users_Username;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_FileVersions_FileItemId_VersionNumber;");
    }
}
