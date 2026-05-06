using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.HowToCookViewer.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalizedRecipe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalizedRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Culture = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LocalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LocalizedDescription = table.Column<string>(type: "TEXT", nullable: false),
                    LocalizedIngredients = table.Column<string>(type: "TEXT", nullable: false),
                    LocalizedCalculation = table.Column<string>(type: "TEXT", nullable: false),
                    LocalizedSteps = table.Column<string>(type: "TEXT", nullable: false),
                    LocalizedNotes = table.Column<string>(type: "TEXT", nullable: false),
                    LastLocalizedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizedRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalizedRecipes_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalizedRecipes_RecipeId_Culture",
                table: "LocalizedRecipes",
                columns: new[] { "RecipeId", "Culture" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalizedRecipes");
        }
    }
}
