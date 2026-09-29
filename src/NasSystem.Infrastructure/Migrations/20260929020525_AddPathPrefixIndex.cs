using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NasSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPathPrefixIndex : Migration
    {
        /// <summary>
        /// Path 前缀查询索引（NOCASE 部分索引）。
        /// </summary>
        /// <remarks>
        /// 后代遍历（软删/恢复/重命名子树的 GetDescendantsAsync）生成 LIKE 'prefix%'，
        /// SQLite 默认 case_sensitive_like=OFF 时 BINARY 排序的 IX_FileItems_Path
        /// 无法服务前缀匹配，退化为全表扫描。补 NOCASE 排序的部分索引供其使用；
        /// IsDeleted=0 过滤与调用方查询谓词一致。用裸 SQL 而非模型配置：
        /// 同一属性上第二个索引经 Fluent API 显式命名时，EF 迁移生成器会与
        /// 未命名的唯一索引发生重名歧义（误判为重命名），故不走模型。
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_FileItems_Path_Prefix\" ON \"FileItems\" (\"Path\" COLLATE NOCASE) WHERE \"IsDeleted\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_FileItems_Path_Prefix\";");
        }
    }
}
