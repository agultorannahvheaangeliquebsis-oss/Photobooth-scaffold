IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Feedback')
BEGIN
    CREATE TABLE Feedback (
        FeedbackId      INT IDENTITY(1,1) PRIMARY KEY,
        SessionId       INT             NOT NULL REFERENCES Session(SessionId),
        Rating          INT             NULL CHECK (Rating BETWEEN 1 AND 5),
        Comment         NVARCHAR(1000)  NULL,
        RecordedAt      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Feedback_Session ON Feedback(SessionId);
END
