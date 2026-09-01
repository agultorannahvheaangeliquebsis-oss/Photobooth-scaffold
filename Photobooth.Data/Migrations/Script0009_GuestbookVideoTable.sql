IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'GuestbookVideo')
BEGIN
    CREATE TABLE GuestbookVideo (
        GuestbookVideoId INT IDENTITY(1,1) PRIMARY KEY,
        SessionId        INT             NOT NULL REFERENCES Session(SessionId),
        FilePath         NVARCHAR(500)   NOT NULL,
        DurationSeconds  INT             NOT NULL,
        RecordedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_GuestbookVideo_Session ON GuestbookVideo(SessionId);
END
