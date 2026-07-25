using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.HowToCookViewer.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalizedRecipeEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Embedding",
                table: "LocalizedRecipes",
                type: "longblob",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmbeddedAt",
                table: "LocalizedRecipes",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "LocalizedRecipes");

            migrationBuilder.DropColumn(
                name: "LastEmbeddedAt",
                table: "LocalizedRecipes");
        }
    }
}
