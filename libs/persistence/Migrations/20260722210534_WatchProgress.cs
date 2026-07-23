using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Persistence.Migrations;

/// <inheritdoc />
public partial class WatchProgress : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "DurationSeconds",
            table: "Videos",
            type: "double precision",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "WatchProgresses",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                VideoId = table.Column<Guid>(type: "uuid", nullable: false),
                PositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WatchProgresses", x => new { x.UserId, x.VideoId });
                table.ForeignKey(
                    name: "FK_WatchProgresses_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_WatchProgresses_Videos_VideoId",
                    column: x => x.VideoId,
                    principalTable: "Videos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WatchProgresses_VideoId",
            table: "WatchProgresses",
            column: "VideoId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "WatchProgresses");

        migrationBuilder.DropColumn(
            name: "DurationSeconds",
            table: "Videos");
    }
}
