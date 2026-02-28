using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFiles.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    HashedSecret = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Prefix);
                });

            migrationBuilder.CreateTable(
                name: "Buckets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OwnerKeyPrefix = table.Column<string>(type: "TEXT", maxLength: 13, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buckets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    BucketId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ShortCode = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => new { x.BucketId, x.Path });
                });

            migrationBuilder.CreateTable(
                name: "ShortUrls",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    BucketId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortUrls", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "UploadTokens",
                columns: table => new
                {
                    Token = table.Column<string>(type: "TEXT", maxLength: 55, nullable: false),
                    BucketId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaxUploads = table.Column<int>(type: "INTEGER", nullable: true),
                    UploadsUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadTokens", x => x.Token);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Buckets_ExpiresAt",
                table: "Buckets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Buckets_Owner",
                table: "Buckets",
                column: "Owner");

            migrationBuilder.CreateIndex(
                name: "IX_Buckets_OwnerKeyPrefix",
                table: "Buckets",
                column: "OwnerKeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_Files_BucketId",
                table: "Files",
                column: "BucketId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_ShortCode",
                table: "Files",
                column: "ShortCode",
                unique: true,
                filter: "ShortCode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShortUrls_BucketId_FilePath",
                table: "ShortUrls",
                columns: new[] { "BucketId", "FilePath" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadTokens_BucketId",
                table: "UploadTokens",
                column: "BucketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "Buckets");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "ShortUrls");

            migrationBuilder.DropTable(
                name: "UploadTokens");
        }
    }
}
