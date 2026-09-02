IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'IsLocked')
BEGIN
    ALTER TABLE Location ADD IsLocked BIT NOT NULL DEFAULT 0;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'RemoteControlEnabled')
BEGIN
    ALTER TABLE Location ADD RemoteControlEnabled BIT NOT NULL DEFAULT 0;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SlideshowEnabled')
BEGIN
    ALTER TABLE Location ADD SlideshowEnabled BIT NOT NULL DEFAULT 1;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SlideshowIntervalSeconds')
BEGIN
    ALTER TABLE Location ADD SlideshowIntervalSeconds INT NOT NULL DEFAULT 4;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SlideshowTransition')
BEGIN
    ALTER TABLE Location ADD SlideshowTransition NVARCHAR(20) NOT NULL DEFAULT 'Fade';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SlideshowShowLogoOverlay')
BEGIN
    ALTER TABLE Location ADD SlideshowShowLogoOverlay BIT NOT NULL DEFAULT 1;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SlideshowShowQrOverlay')
BEGIN
    ALTER TABLE Location ADD SlideshowShowQrOverlay BIT NOT NULL DEFAULT 1;
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SharingLog')
BEGIN
    CREATE TABLE SharingLog (
        SharingLogId    INT IDENTITY(1,1) PRIMARY KEY,
        SessionId       INT             NOT NULL REFERENCES Session(SessionId),
        Method          NVARCHAR(10)    NOT NULL CHECK (Method IN ('Email', 'SMS')),
        Destination     NVARCHAR(320)   NOT NULL,
        PhotoUrl        NVARCHAR(500)   NOT NULL,
        Status          NVARCHAR(10)    NOT NULL CHECK (Status IN ('Sent', 'Failed')),
        ErrorMessage    NVARCHAR(500)   NULL,
        SentAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_SharingLog_Session ON SharingLog(SessionId);
    CREATE INDEX IX_SharingLog_SentAt ON SharingLog(SentAt DESC);
END
