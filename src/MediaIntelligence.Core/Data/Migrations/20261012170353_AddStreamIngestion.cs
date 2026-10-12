using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaIntelligence.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stream_ingestion",
                columns: table => new
                {
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stream_ingestion", x => new { x.Topic, x.EventId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_stream_ingestion_ProcessedAtUtc",
                table: "stream_ingestion",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stream_ingestion");
        }
    }
}
