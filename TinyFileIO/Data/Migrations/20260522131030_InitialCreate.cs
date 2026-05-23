using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinyFileIO.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    username = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    password = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    salt = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    is_super_admin = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_acls",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bucket_name = table.Column<string>(type: "TEXT", maxLength: 63, nullable: true),
                    can_read = table.Column<bool>(type: "INTEGER", nullable: false),
                    can_add = table.Column<bool>(type: "INTEGER", nullable: false),
                    can_update = table.Column<bool>(type: "INTEGER", nullable: false),
                    can_delete = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_acls", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_acls_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_acls_user_id_bucket_name",
                table: "user_acls",
                columns: new[] { "user_id", "bucket_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_acls");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
