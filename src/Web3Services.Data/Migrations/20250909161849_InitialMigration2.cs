using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web3Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_SpentTxHash",
                schema: "public",
                table: "OutputsBySlot",
                column: "SpentTxHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutputsBySlot_SpentTxHash",
                schema: "public",
                table: "OutputsBySlot");
        }
    }
}
