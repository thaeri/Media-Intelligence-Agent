using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaIntelligence.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSqlPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audience",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audience", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "media_content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "date", nullable: false),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_content", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audience_consent",
                columns: table => new
                {
                    AudienceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audience_consent", x => x.AudienceId);
                    table.CheckConstraint("CK_audience_consent_status", "\"Status\" >= 0 AND \"Status\" <= 2");
                    table.ForeignKey(
                        name: "FK_audience_consent_audience_AudienceId",
                        column: x => x.AudienceId,
                        principalTable: "audience",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_audience_association",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AudienceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_audience_association", x => new { x.DeviceId, x.AudienceId });
                    table.ForeignKey(
                        name: "FK_device_audience_association_audience_AudienceId",
                        column: x => x.AudienceId,
                        principalTable: "audience",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign", x => x.Id);
                    table.UniqueConstraint("AK_campaign_Id_ContentId", x => new { x.Id, x.ContentId });
                    table.CheckConstraint("CK_campaign_utc_range", "\"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.ForeignKey(
                        name: "FK_campaign_media_content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "media_content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "viewing_event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ViewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WatchDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    AdShown = table.Column<bool>(type: "boolean", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewing_event", x => x.Id);
                    table.CheckConstraint("CK_viewing_event_watch_duration", "\"WatchDurationSeconds\" >= 0");
                    table.ForeignKey(
                        name: "FK_viewing_event_media_content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "media_content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ad_impression",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_impression", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ad_impression_campaign_CampaignId_ContentId",
                        columns: x => new { x.CampaignId, x.ContentId },
                        principalTable: "campaign",
                        principalColumns: new[] { "Id", "ContentId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ad_impression_media_content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "media_content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ad_impression_CampaignId_ContentId",
                table: "ad_impression",
                columns: new[] { "CampaignId", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ad_impression_CampaignId_OccurredAtUtc",
                table: "ad_impression",
                columns: new[] { "CampaignId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ad_impression_ContentId",
                table: "ad_impression",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_campaign_ContentId",
                table: "campaign",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_campaign_Name",
                table: "campaign",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_audience_association_AudienceId",
                table: "device_audience_association",
                column: "AudienceId");

            migrationBuilder.CreateIndex(
                name: "IX_media_content_Title",
                table: "media_content",
                column: "Title",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_viewing_event_ContentId_ViewedAtUtc",
                table: "viewing_event",
                columns: new[] { "ContentId", "ViewedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ad_impression");

            migrationBuilder.DropTable(
                name: "audience_consent");

            migrationBuilder.DropTable(
                name: "device_audience_association");

            migrationBuilder.DropTable(
                name: "viewing_event");

            migrationBuilder.DropTable(
                name: "campaign");

            migrationBuilder.DropTable(
                name: "audience");

            migrationBuilder.DropTable(
                name: "media_content");
        }
    }
}
