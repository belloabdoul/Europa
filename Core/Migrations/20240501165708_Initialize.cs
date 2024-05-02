using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Core.Migrations
{
    /// <inheritdoc />
    public partial class Initialize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    image_hash = table.Column<Vector>(type: "vector", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "similarities",
                columns: table => new
                {
                    hypothetical_original_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hypothetical_duplicate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_similarities", x => new { x.hypothetical_original_id, x.hypothetical_duplicate_id });
                    table.ForeignKey(
                        name: "fk_similarities_files_hypothetical_duplicate_id",
                        column: x => x.hypothetical_duplicate_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_similarities_files_hypothetical_original_id",
                        column: x => x.hypothetical_original_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_similarities_hypothetical_duplicate_id",
                table: "similarities",
                column: "hypothetical_duplicate_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "similarities");

            migrationBuilder.DropTable(
                name: "files");
        }
    }
}
