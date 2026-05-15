using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.HowToCookViewer.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ResetIngredientCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM IngredientRecipe;");
            migrationBuilder.Sql("DELETE FROM Ingredients;");
            migrationBuilder.Sql("UPDATE Recipes SET LastIngredientExtractedAt = '0001-01-01 00:00:00';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot restore cleared data.
        }
    }
}
