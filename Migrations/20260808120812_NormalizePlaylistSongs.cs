using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace rptr.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePlaylistSongs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaylistSongs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlaylistId = table.Column<int>(type: "int", nullable: false),
                    SpotifySongId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SongName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrimaryArtistName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalSongLengthMs = table.Column<int>(type: "int", nullable: true),
                    Excluded = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistSongs", x => x.Id);
                    table.CheckConstraint("CK_PlaylistSongs_OriginalSongLengthMs_Positive", "[OriginalSongLengthMs] IS NULL OR [OriginalSongLengthMs] > 0");
                    table.ForeignKey(
                        name: "FK_PlaylistSongs_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [SongTags])
                BEGIN
                    SET IDENTITY_INSERT [PlaylistSongs] ON;

                    INSERT INTO [PlaylistSongs]
                        ([Id], [PlaylistId], [SpotifySongId], [SongName], [PrimaryArtistName],
                         [OriginalSongLengthMs], [Excluded], [Tags])
                    SELECT
                        [t].[Id],
                        [t].[PlaylistId],
                        [t].[SongId],
                        COALESCE([s].[SongName], [t].[SongName]),
                        COALESCE([s].[PrimaryArtistName], [t].[PrimaryArtistName]),
                        [s].[OriginalSongLengthMs],
                        CAST(0 AS bit),
                        [t].[Tags]
                    FROM [SongTags] AS [t]
                    LEFT JOIN [Snippets] AS [s]
                        ON [s].[PlaylistId] = [t].[PlaylistId]
                        AND [s].[SongId] = [t].[SongId];

                    SET IDENTITY_INSERT [PlaylistSongs] OFF;
                END;

                INSERT INTO [PlaylistSongs]
                    ([PlaylistId], [SpotifySongId], [SongName], [PrimaryArtistName],
                     [OriginalSongLengthMs], [Excluded], [Tags])
                SELECT
                    [s].[PlaylistId],
                    [s].[SongId],
                    [s].[SongName],
                    [s].[PrimaryArtistName],
                    [s].[OriginalSongLengthMs],
                    CAST(0 AS bit),
                    NULL
                FROM [Snippets] AS [s]
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM [SongTags] AS [t]
                    WHERE [t].[PlaylistId] = [s].[PlaylistId]
                        AND [t].[SongId] = [s].[SongId]
                );
                """);

            migrationBuilder.AddColumn<int>(
                name: "PlaylistSongId",
                table: "Snippets",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [s]
                SET [PlaylistSongId] = [ps].[Id]
                FROM [Snippets] AS [s]
                INNER JOIN [PlaylistSongs] AS [ps]
                    ON [ps].[PlaylistId] = [s].[PlaylistId]
                    AND [ps].[SpotifySongId] = [s].[SongId];
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Snippets_Playlists_PlaylistId",
                table: "Snippets");

            migrationBuilder.DropIndex(
                name: "IX_Snippets_PlaylistId_SongId",
                table: "Snippets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Snippets_OriginalSongLengthMs_Positive",
                table: "Snippets");

            migrationBuilder.DropColumn(
                name: "OriginalSongLengthMs",
                table: "Snippets");

            migrationBuilder.DropColumn(
                name: "PlaylistId",
                table: "Snippets");

            migrationBuilder.DropColumn(
                name: "PrimaryArtistName",
                table: "Snippets");

            migrationBuilder.DropColumn(
                name: "SongId",
                table: "Snippets");

            migrationBuilder.DropColumn(
                name: "SongName",
                table: "Snippets");

            migrationBuilder.AlterColumn<int>(
                name: "PlaylistSongId",
                table: "Snippets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropTable(
                name: "SongTags");

            migrationBuilder.CreateIndex(
                name: "IX_Snippets_PlaylistSongId",
                table: "Snippets",
                column: "PlaylistSongId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSongs_PlaylistId_SpotifySongId",
                table: "PlaylistSongs",
                columns: new[] { "PlaylistId", "SpotifySongId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Snippets_PlaylistSongs_PlaylistSongId",
                table: "Snippets",
                column: "PlaylistSongId",
                principalTable: "PlaylistSongs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Snippets_PlaylistSongs_PlaylistSongId",
                table: "Snippets");

            migrationBuilder.DropIndex(
                name: "IX_Snippets_PlaylistSongId",
                table: "Snippets");

            migrationBuilder.AddColumn<int>(
                name: "OriginalSongLengthMs",
                table: "Snippets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlaylistId",
                table: "Snippets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryArtistName",
                table: "Snippets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SongId",
                table: "Snippets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "SongName",
                table: "Snippets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [s]
                SET
                    [PlaylistId] = [ps].[PlaylistId],
                    [SongId] = [ps].[SpotifySongId],
                    [SongName] = [ps].[SongName],
                    [PrimaryArtistName] = [ps].[PrimaryArtistName],
                    [OriginalSongLengthMs] = [ps].[OriginalSongLengthMs]
                FROM [Snippets] AS [s]
                INNER JOIN [PlaylistSongs] AS [ps]
                    ON [ps].[Id] = [s].[PlaylistSongId];
                """);

            migrationBuilder.AlterColumn<int>(
                name: "OriginalSongLengthMs",
                table: "Snippets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PlaylistId",
                table: "Snippets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryArtistName",
                table: "Snippets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SongId",
                table: "Snippets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "SongName",
                table: "Snippets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "SongTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlaylistId = table.Column<int>(type: "int", nullable: false),
                    PrimaryArtistName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SongId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SongName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SongTags_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [PlaylistSongs] WHERE [Tags] IS NOT NULL)
                BEGIN
                    SET IDENTITY_INSERT [SongTags] ON;

                    INSERT INTO [SongTags]
                        ([Id], [PlaylistId], [SongId], [SongName], [PrimaryArtistName], [Tags])
                    SELECT
                        [Id],
                        [PlaylistId],
                        [SpotifySongId],
                        [SongName],
                        [PrimaryArtistName],
                        [Tags]
                    FROM [PlaylistSongs]
                    WHERE [Tags] IS NOT NULL;

                    SET IDENTITY_INSERT [SongTags] OFF;
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Snippets_PlaylistId_SongId",
                table: "Snippets",
                columns: new[] { "PlaylistId", "SongId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Snippets_OriginalSongLengthMs_Positive",
                table: "Snippets",
                sql: "[OriginalSongLengthMs] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_SongTags_PlaylistId_SongId",
                table: "SongTags",
                columns: new[] { "PlaylistId", "SongId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Snippets_Playlists_PlaylistId",
                table: "Snippets",
                column: "PlaylistId",
                principalTable: "Playlists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropColumn(
                name: "PlaylistSongId",
                table: "Snippets");

            migrationBuilder.DropTable(
                name: "PlaylistSongs");
        }
    }
}
