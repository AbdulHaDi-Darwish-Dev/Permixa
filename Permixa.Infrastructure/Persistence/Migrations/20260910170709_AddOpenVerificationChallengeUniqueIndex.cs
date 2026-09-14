using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Permixa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenVerificationChallengeUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VerificationChallenges_UserId_Purpose_Destination",
                table: "VerificationChallenges");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationChallenges_Open_UserId_Purpose_Destination",
                table: "VerificationChallenges",
                columns: new[] { "UserId", "Purpose", "Destination" },
                unique: true,
                filter: "[ConsumedAtUtc] IS NULL AND [InvalidatedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VerificationChallenges_Open_UserId_Purpose_Destination",
                table: "VerificationChallenges");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationChallenges_UserId_Purpose_Destination",
                table: "VerificationChallenges",
                columns: new[] { "UserId", "Purpose", "Destination" });
        }
    }
}
