using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NasSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoragePools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MountPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    UsedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxCapacityBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoragePools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QuotaBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    UsedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    HomeDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    PhysicalPath = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ItemType = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StoragePoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileItems_FileItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "FileItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileItems_StoragePools_StoragePoolId",
                        column: x => x.StoragePoolId,
                        principalTable: "StoragePools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileItems_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FilePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Permission = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilePermissions_FileItems_FileItemId",
                        column: x => x.FileItemId,
                        principalTable: "FileItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilePermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FileShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShareCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FileItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessType = table.Column<int>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Permission = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MaxDownloads = table.Column<int>(type: "INTEGER", nullable: true),
                    DownloadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileShares_FileItems_FileItemId",
                        column: x => x.FileItemId,
                        principalTable: "FileItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileShares_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FileVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileVersions_FileItems_FileItemId",
                        column: x => x.FileItemId,
                        principalTable: "FileItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_Hash",
                table: "FileItems",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_IsDeleted",
                table: "FileItems",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_OwnerId",
                table: "FileItems",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_ParentId",
                table: "FileItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_ParentId_Name",
                table: "FileItems",
                columns: new[] { "ParentId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_Path",
                table: "FileItems",
                column: "Path",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_StoragePoolId",
                table: "FileItems",
                column: "StoragePoolId");

            migrationBuilder.CreateIndex(
                name: "IX_FilePermissions_FileItemId",
                table: "FilePermissions",
                column: "FileItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FilePermissions_FileItemId_UserId",
                table: "FilePermissions",
                columns: new[] { "FileItemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FilePermissions_UserId",
                table: "FilePermissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileShares_CreatedByUserId",
                table: "FileShares",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileShares_FileItemId",
                table: "FileShares",
                column: "FileItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FileShares_ShareCode",
                table: "FileShares",
                column: "ShareCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_CreatedByUserId",
                table: "FileVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_FileItemId",
                table: "FileVersions",
                column: "FileItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_FileItemId_IsCurrent",
                table: "FileVersions",
                columns: new[] { "FileItemId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_FileItemId_VersionNumber",
                table: "FileVersions",
                columns: new[] { "FileItemId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoragePools_IsDefault",
                table: "StoragePools",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_StoragePools_MountPath",
                table: "StoragePools",
                column: "MountPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoragePools_Name",
                table: "StoragePools",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsEnabled",
                table: "Users",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilePermissions");

            migrationBuilder.DropTable(
                name: "FileShares");

            migrationBuilder.DropTable(
                name: "FileVersions");

            migrationBuilder.DropTable(
                name: "FileItems");

            migrationBuilder.DropTable(
                name: "StoragePools");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
