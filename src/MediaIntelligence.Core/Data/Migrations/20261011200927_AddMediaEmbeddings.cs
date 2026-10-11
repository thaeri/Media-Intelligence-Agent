using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MediaIntelligence.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "media_embedding",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_embedding", x => new { x.ContentId, x.Model });
                    table.CheckConstraint("CK_media_embedding_content_version", "\"ContentVersion\" > 0");
                    table.CheckConstraint("CK_media_embedding_dimension", "\"Dimension\" > 0");
                    table.ForeignKey(
                        name: "FK_media_embedding_media_content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "media_content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_embedding_Model_Dimension",
                table: "media_embedding",
                columns: new[] { "Model", "Dimension" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_embedding");
        }
    }
}
