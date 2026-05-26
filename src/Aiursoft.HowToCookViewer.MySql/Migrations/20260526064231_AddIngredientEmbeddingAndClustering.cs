using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.HowToCookViewer.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientEmbeddingAndClustering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanonicalIngredientId",
                table: "Ingredients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Embedding",
                table: "Ingredients",
                type: "longblob",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmbeddedAt",
                table: "Ingredients",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_CanonicalIngredientId",
                table: "Ingredients",
                column: "CanonicalIngredientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ingredients_Ingredients_CanonicalIngredientId",
                table: "Ingredients",
                column: "CanonicalIngredientId",
                principalTable: "Ingredients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ingredients_Ingredients_CanonicalIngredientId",
                table: "Ingredients");

            migrationBuilder.DropIndex(
                name: "IX_Ingredients_CanonicalIngredientId",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "CanonicalIngredientId",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "LastEmbeddedAt",
                table: "Ingredients");
        }
    }
}
