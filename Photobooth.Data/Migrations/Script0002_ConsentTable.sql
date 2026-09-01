IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Consent')
BEGIN
    CREATE TABLE Consent (
        ConsentId           INT IDENTITY(1,1) PRIMARY KEY,
        SessionId           INT             NOT NULL REFERENCES Session(SessionId),
        DisclaimerAccepted  BIT             NOT NULL,
        EmailOptIn          BIT             NOT NULL DEFAULT 0,
        Email               NVARCHAR(255)   NULL,
        RecordedAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Consent_Session ON Consent(SessionId);
END
