using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.HowToCookViewer.MySql.Migrations
{
    /// <inheritdoc />
    public partial class ClearLocalizedRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Previous translations were generated with a broken translation implementation.
            // Wipe all rows so the LocalizeRecipesJob will re-translate everything correctly.
            migrationBuilder.Sql("DELETE FROM LocalizedRecipes;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data loss is intentional; cannot restore deleted rows.
        }
    }
}
