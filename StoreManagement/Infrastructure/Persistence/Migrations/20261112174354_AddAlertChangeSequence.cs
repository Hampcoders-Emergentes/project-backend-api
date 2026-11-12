using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertChangeSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                schema: "alert_management",
                table: "alerts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "store_alert_sequences",
                schema: "alert_management",
                columns: table => new
                {
                    store_id = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_alert_sequences", x => x.store_id);
                    table.ForeignKey(
                        name: "FK_store_alert_sequences_stores_store_id",
                        column: x => x.store_id,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_Sequence",
                schema: "alert_management",
                table: "alerts",
                column: "Sequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_alert_sequences",
                schema: "alert_management");

            migrationBuilder.DropIndex(
                name: "IX_alerts_Sequence",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "Sequence",
                schema: "alert_management",
                table: "alerts");
        }
    }
}
