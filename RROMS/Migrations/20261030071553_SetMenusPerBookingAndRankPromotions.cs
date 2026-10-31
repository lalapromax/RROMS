using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class SetMenusPerBookingAndRankPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BirthdayOnly",
                table: "RewardPerks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The perks seeded as birthday treats keep the birthday-month rule they had; administrators set it from now on.
            migrationBuilder.Sql("UPDATE RewardPerks SET BirthdayOnly = 1 WHERE Title LIKE '%birthday%' AND Frequency <> 3;");

            migrationBuilder.AddColumn<string>(
                name: "EligibleTierIdsRaw",
                table: "Promotions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReservationSetMenus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    SetMenuId = table.Column<int>(type: "int", nullable: true),
                    SetMenuName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Guests = table.Column<int>(type: "int", nullable: false),
                    SentToKitchenAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationSetMenus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationSetMenus_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReservationSetMenus_SetMenus_SetMenuId",
                        column: x => x.SetMenuId,
                        principalTable: "SetMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSetMenus_ReservationId",
                table: "ReservationSetMenus",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSetMenus_SetMenuId",
                table: "ReservationSetMenus",
                column: "SetMenuId");

            // A booking used to carry one set menu for the whole table. Keep those as one line for the whole party.
            migrationBuilder.Sql(@"
INSERT INTO ReservationSetMenus (ReservationId, SetMenuId, SetMenuName, Guests, SentToKitchenAt)
SELECT r.Id, r.SetMenuId, s.Name, r.PartySize, NULL
FROM Reservations r
JOIN SetMenus s ON s.Id = r.SetMenuId
WHERE r.SetMenuId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_SetMenus_SetMenuId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_SetMenuId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SetMenuId",
                table: "Reservations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthdayOnly",
                table: "RewardPerks");

            migrationBuilder.DropColumn(
                name: "EligibleTierIdsRaw",
                table: "Promotions");

            migrationBuilder.AddColumn<int>(
                name: "SetMenuId",
                table: "Reservations",
                type: "int",
                nullable: true);

            // Going back to one set menu per booking keeps the first one chosen.
            migrationBuilder.Sql(@"
UPDATE r SET SetMenuId = pick.SetMenuId
FROM Reservations r
CROSS APPLY (SELECT TOP 1 SetMenuId FROM ReservationSetMenus x WHERE x.ReservationId = r.Id AND x.SetMenuId IS NOT NULL ORDER BY x.Id) pick;");

            migrationBuilder.DropTable(
                name: "ReservationSetMenus");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_SetMenuId",
                table: "Reservations",
                column: "SetMenuId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_SetMenus_SetMenuId",
                table: "Reservations",
                column: "SetMenuId",
                principalTable: "SetMenus",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
