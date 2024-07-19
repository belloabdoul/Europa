using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
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
                name: "images_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    image_hash = table.Column<Vector>(type: "vector(32)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_images_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "similarities",
                columns: table => new
                {
                    original_id = table.Column<long>(type: "bigint", nullable: false),
                    duplicate_id = table.Column<long>(type: "bigint", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_similarities", x => new { x.original_id, x.duplicate_id });
                    table.ForeignKey(
                        name: "FK_similarities_images_groups_duplicate_id",
                        column: x => x.duplicate_id,
                        principalTable: "images_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_similarities_images_groups_original_id",
                        column: x => x.original_id,
                        principalTable: "images_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_images_groups_hash",
                table: "images_groups",
                column: "hash");

            migrationBuilder.CreateIndex(
                name: "IX_images_groups_image_hash",
                table: "images_groups",
                column: "image_hash")
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:ef_construction", 200)
                .Annotation("Npgsql:StorageParameter:m", 16);

            migrationBuilder.CreateIndex(
                name: "IX_similarities_duplicate_id",
                table: "similarities",
                column: "duplicate_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "similarities");

            migrationBuilder.DropTable(
                name: "images_groups");
        }
    }
}
