using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelWebAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_paid_at",
                table: "payments");

            migrationBuilder.CreateIndex(
                name: "ix_users_created_at",
                table: "users",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_review_date",
                table: "reviews",
                column: "review_date");

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid_at_created_at",
                table: "payments",
                columns: new[] { "paid_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_messages_created_at",
                table: "contact_messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_created_at",
                table: "bookings",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_created_at",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_reviews_review_date",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_payments_paid_at_created_at",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_contact_messages_created_at",
                table: "contact_messages");

            migrationBuilder.DropIndex(
                name: "ix_bookings_created_at",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid_at",
                table: "payments",
                column: "paid_at");
        }
    }
}
