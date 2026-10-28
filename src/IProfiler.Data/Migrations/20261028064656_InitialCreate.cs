using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IProfiler.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Rotation = table.Column<int>(type: "INTEGER", nullable: false),
                    DeskewAngle = table.Column<double>(type: "REAL", nullable: true),
                    Dpi = table.Column<int>(type: "INTEGER", nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    PatchCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Crop_Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Crop_Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Crop_X = table.Column<int>(type: "INTEGER", nullable: true),
                    Crop_Y = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pages_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BatchId_Order",
                table: "Documents",
                columns: new[] { "BatchId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_DocumentId_Order",
                table: "Pages",
                columns: new[] { "DocumentId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pages");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Batches");
        }
    }
}
