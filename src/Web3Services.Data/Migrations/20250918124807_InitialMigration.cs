using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web3Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "BlockTests",
                schema: "public",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Height = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTests", x => x.Hash);
                });

            migrationBuilder.CreateTable(
                name: "OutputsBySlot",
                schema: "public",
                columns: table => new
                {
                    OutRef = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    SpentTxHash = table.Column<string>(type: "text", nullable: false),
                    SpentSlot = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    PaymentKeyHash = table.Column<string>(type: "text", nullable: false),
                    StakeKeyHash = table.Column<string>(type: "text", nullable: false),
                    Raw = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutputsBySlot", x => x.OutRef);
                });

            migrationBuilder.CreateTable(
                name: "ReducerStates",
                schema: "public",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LatestIntersectionsJson = table.Column<string>(type: "text", nullable: false),
                    StartIntersectionJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReducerStates", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "TrackedAddresses",
                schema: "public",
                columns: table => new
                {
                    PaymentKeyHash = table.Column<string>(type: "text", nullable: false),
                    StakeKeyHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedAddresses", x => new { x.PaymentKeyHash, x.StakeKeyHash });
                });

            migrationBuilder.CreateTable(
                name: "TransactionsByAddress",
                schema: "public",
                columns: table => new
                {
                    StakeKeyHash = table.Column<string>(type: "text", nullable: false),
                    PaymentKeyHash = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Subjects = table.Column<string[]>(type: "text[]", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Raw = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionsByAddress", x => new { x.PaymentKeyHash, x.StakeKeyHash, x.Hash });
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_PaymentKeyHash",
                schema: "public",
                table: "OutputsBySlot",
                column: "PaymentKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_PaymentKeyHash_Slot",
                schema: "public",
                table: "OutputsBySlot",
                columns: new[] { "PaymentKeyHash", "Slot" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_PaymentKeyHash_StakeKeyHash",
                schema: "public",
                table: "OutputsBySlot",
                columns: new[] { "PaymentKeyHash", "StakeKeyHash" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_Slot",
                schema: "public",
                table: "OutputsBySlot",
                column: "Slot");

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_SpentSlot",
                schema: "public",
                table: "OutputsBySlot",
                column: "SpentSlot");

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_SpentTxHash",
                schema: "public",
                table: "OutputsBySlot",
                column: "SpentTxHash");

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_StakeKeyHash",
                schema: "public",
                table: "OutputsBySlot",
                column: "StakeKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_OutputsBySlot_StakeKeyHash_Slot",
                schema: "public",
                table: "OutputsBySlot",
                columns: new[] { "StakeKeyHash", "Slot" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedAddresses_CreatedAt",
                schema: "public",
                table: "TrackedAddresses",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedAddresses_PaymentKeyHash",
                schema: "public",
                table: "TrackedAddresses",
                column: "PaymentKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedAddresses_StakeKeyHash",
                schema: "public",
                table: "TrackedAddresses",
                column: "StakeKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsByAddress_Hash",
                schema: "public",
                table: "TransactionsByAddress",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsByAddress_PaymentKeyHash",
                schema: "public",
                table: "TransactionsByAddress",
                column: "PaymentKeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsByAddress_PaymentKeyHash_Slot_Hash",
                schema: "public",
                table: "TransactionsByAddress",
                columns: new[] { "PaymentKeyHash", "Slot", "Hash" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsByAddress_PaymentKeyHash_StakeKeyHash",
                schema: "public",
                table: "TransactionsByAddress",
                columns: new[] { "PaymentKeyHash", "StakeKeyHash" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsByAddress_PaymentKeyHash_StakeKeyHash_Slot_Hash",
                schema: "public",
                table: "TransactionsByAddress",
                columns: new[] { "PaymentKeyHash", "StakeKeyHash", "Slot", "Hash" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsByAddress_Slot",
                schema: "public",
                table: "TransactionsByAddress",
                column: "Slot");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsByAddress_Subjects",
                schema: "public",
                table: "TransactionsByAddress",
                column: "Subjects")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockTests",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OutputsBySlot",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ReducerStates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TrackedAddresses",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TransactionsByAddress",
                schema: "public");
        }
    }
}
