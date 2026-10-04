using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SerenityCharacterBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "profile_id",
                table: "serenity_resource_transaction",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "serenity_character_balance",
                columns: table => new
                {
                    profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    balance = table.Column<double>(type: "REAL", nullable: false),
                    starting_funds = table.Column<double>(type: "REAL", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serenity_character_balance", x => x.profile_id);
                    table.ForeignKey(
                        name: "FK_serenity_character_balance_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "serenity_character_balance");

            migrationBuilder.DropColumn(
                name: "profile_id",
                table: "serenity_resource_transaction");
        }
    }
}
