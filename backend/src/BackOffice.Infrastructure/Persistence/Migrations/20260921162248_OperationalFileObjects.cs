using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalFileObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileObject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgencyEvidenceFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuoteEvidenceFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServicingEvidenceFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileObject", x => x.Id);
                    table.CheckConstraint("CK_FileObject_Content", "[ByteLength] BETWEEN 1 AND 20971520 AND DATALENGTH([Sha256])=64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND LEN(TRIM([FileName]))>0");
                    table.CheckConstraint("CK_FileObject_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FileObject_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_FileObject_State", "([State]='pending' AND [VerifiedAt] IS NULL AND [FailureCode] IS NULL) OR ([State]='ready' AND [VerifiedAt] IS NOT NULL AND [FailureCode] IS NULL) OR ([State]='quarantined' AND [FailureCode] IS NOT NULL AND LEN(TRIM([FailureCode]))>0)");
                    table.CheckConstraint("CK_FileObject_Storage", "([StorageKind]='local' AND [WorkId] IS NOT NULL AND [AgencyEvidenceFileId] IS NULL AND [QuoteEvidenceFileId] IS NULL AND [ServicingEvidenceFileId] IS NULL AND [MediaType] IN ('application/pdf','image/png','image/jpeg')) OR\n([StorageKind]='agency-evidence' AND [WorkId] IS NULL AND [AgencyEvidenceFileId] IS NOT NULL AND [QuoteEvidenceFileId] IS NULL AND [ServicingEvidenceFileId] IS NULL AND [State]<>'pending') OR\n([StorageKind]='quote-evidence' AND [WorkId] IS NULL AND [AgencyEvidenceFileId] IS NULL AND [QuoteEvidenceFileId] IS NOT NULL AND [ServicingEvidenceFileId] IS NULL AND [State]<>'pending') OR\n([StorageKind]='servicing-evidence' AND [WorkId] IS NULL AND [AgencyEvidenceFileId] IS NULL AND [QuoteEvidenceFileId] IS NULL AND [ServicingEvidenceFileId] IS NOT NULL AND [State]<>'pending')");
                    table.CheckConstraint("CK_FileObject_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.CheckConstraint("CK_FileObject_VerifiedAt", "[VerifiedAt] IS NULL OR [VerifiedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_FileObject_VerifiedAt_Utc", "DATEPART(TZOFFSET,[VerifiedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_FileObject_AgencyEvidenceFile_AgencyEvidenceFileId",
                        column: x => x.AgencyEvidenceFileId,
                        principalTable: "AgencyEvidenceFile",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FileObject_OperationalSubject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "OperationalSubject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FileObject_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FileObject_QuoteEvidenceFile_QuoteEvidenceFileId",
                        column: x => x.QuoteEvidenceFileId,
                        principalTable: "QuoteEvidenceFile",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FileObject_ServicingEvidenceFile_ServicingEvidenceFileId",
                        column: x => x.ServicingEvidenceFileId,
                        principalTable: "ServicingEvidenceFile",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FileObject_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileObject_AgencyEvidenceFileId",
                table: "FileObject",
                column: "AgencyEvidenceFileId",
                unique: true,
                filter: "[AgencyEvidenceFileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileObject_CreatedBy",
                table: "FileObject",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FileObject_QuoteEvidenceFileId",
                table: "FileObject",
                column: "QuoteEvidenceFileId",
                unique: true,
                filter: "[QuoteEvidenceFileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileObject_ServicingEvidenceFileId",
                table: "FileObject",
                column: "ServicingEvidenceFileId",
                unique: true,
                filter: "[ServicingEvidenceFileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileObject_SubjectId_State_Id",
                table: "FileObject",
                columns: new[] { "SubjectId", "State", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FileObject_WorkId",
                table: "FileObject",
                column: "WorkId",
                unique: true,
                filter: "[WorkId] IS NOT NULL");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FileObject_Immutable ON FileObject AFTER UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT Id FROM deleted EXCEPT SELECT Id FROM inserted)
                    THROW 51987,'File identities cannot be deleted.',1;
                  IF EXISTS(
                    SELECT Id,SubjectId,StorageKind COLLATE Latin1_General_100_BIN2,FileName COLLATE Latin1_General_100_BIN2,MediaType COLLATE Latin1_General_100_BIN2,ByteLength,Sha256,WorkId,AgencyEvidenceFileId,QuoteEvidenceFileId,ServicingEvidenceFileId,CreatedBy,CreatedAt FROM inserted
                    EXCEPT SELECT Id,SubjectId,StorageKind COLLATE Latin1_General_100_BIN2,FileName COLLATE Latin1_General_100_BIN2,MediaType COLLATE Latin1_General_100_BIN2,ByteLength,Sha256,WorkId,AgencyEvidenceFileId,QuoteEvidenceFileId,ServicingEvidenceFileId,CreatedBy,CreatedAt FROM deleted)
                    THROW 51988,'File content metadata and ownership are immutable.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE
                    (d.State='quarantined' AND (i.State<>d.State OR ISNULL(i.FailureCode,'')<>ISNULL(d.FailureCode,''))) OR
                    (d.State='ready' AND i.State NOT IN ('ready','quarantined')) OR
                    (d.VerifiedAt IS NOT NULL AND (i.VerifiedAt IS NULL OR i.VerifiedAt<>d.VerifiedAt)))
                    THROW 51989,'File finalization cannot move backwards.',1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FileObject_Source ON FileObject AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN OutboxWork w ON w.Id=i.WorkId WHERE i.StorageKind='local' AND
                    (i.State<>'pending' OR w.Kind<>'file-finalization' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.SubjectId))
                    THROW 51990,'Local files require their pending finalization work.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalSubject s ON s.Id=i.SubjectId JOIN AgencyEvidenceFile f ON f.Id=i.AgencyEvidenceFileId WHERE
                    s.AgencyId IS NULL OR s.AgencyId<>f.AgencyId OR i.State<>'ready' OR f.ScreeningState<>'demo-cleared' OR
                    i.FileName COLLATE Latin1_General_100_BIN2<>f.FileName COLLATE Latin1_General_100_BIN2 OR i.MediaType<>f.ContentType OR i.ByteLength<>f.ByteLength OR i.Sha256<>f.Sha256 COLLATE Latin1_General_100_BIN2 OR
                    i.Sha256<>LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',f.Content),2)))
                    THROW 51991,'Agency evidence bridge must preserve its screened source.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalSubject s ON s.Id=i.SubjectId JOIN QuoteEvidenceFile f ON f.Id=i.QuoteEvidenceFileId WHERE
                    s.QuoteId IS NULL OR s.QuoteId<>f.QuoteId OR i.State<>'ready' OR f.ScreeningState<>'accepted' OR
                    i.FileName COLLATE Latin1_General_100_BIN2<>f.FileName COLLATE Latin1_General_100_BIN2 OR i.MediaType<>f.ContentType OR i.ByteLength<>f.ByteLength OR i.Sha256<>f.Sha256 COLLATE Latin1_General_100_BIN2 OR
                    i.Sha256<>LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',f.Content),2)))
                    THROW 51992,'Quote evidence bridge must preserve its screened source.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalSubject s ON s.Id=i.SubjectId JOIN ServicingEvidenceFile f ON f.Id=i.ServicingEvidenceFileId WHERE
                    s.ServicingDraftId IS NULL OR s.ServicingDraftId<>f.DraftId OR i.State<>'ready' OR f.ScreeningState<>'accepted' OR
                    i.FileName COLLATE Latin1_General_100_BIN2<>f.FileName COLLATE Latin1_General_100_BIN2 OR i.MediaType<>f.ContentType OR i.ByteLength<>f.ByteLength OR i.Sha256<>f.Sha256 COLLATE Latin1_General_100_BIN2 OR
                    i.Sha256<>LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',f.Content),2)))
                    THROW 51993,'Servicing evidence bridge must preserve its screened source.',1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM FileObject) THROW 51994,'Retained file identities cannot be discarded by downgrade.',1;");
            migrationBuilder.DropTable(
                name: "FileObject");
        }
    }
}
