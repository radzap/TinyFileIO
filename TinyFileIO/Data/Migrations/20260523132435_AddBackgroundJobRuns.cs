using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinyFileIO.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundJobRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "background_job_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    job_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    started_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    finished_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_by_user_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    created_by_username = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    target_bucket = table.Column<string>(type: "TEXT", maxLength: 63, nullable: true),
                    parameters_json = table.Column<string>(type: "TEXT", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    total_items = table.Column<int>(type: "INTEGER", nullable: false),
                    processed_items = table.Column<int>(type: "INTEGER", nullable: false),
                    succeeded_items = table.Column<int>(type: "INTEGER", nullable: false),
                    skipped_items = table.Column<int>(type: "INTEGER", nullable: false),
                    failed_items = table.Column<int>(type: "INTEGER", nullable: false),
                    bytes_processed = table.Column<long>(type: "INTEGER", nullable: false),
                    stats_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_background_job_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_background_job_runs_created_utc",
                table: "background_job_runs",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "IX_background_job_runs_job_type_status",
                table: "background_job_runs",
                columns: new[] { "job_type", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_job_runs");
        }
    }
}
