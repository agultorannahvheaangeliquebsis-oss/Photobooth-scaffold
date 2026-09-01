IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantEnabled')
    ALTER TABLE Location ADD AttendantEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantStyle')
    ALTER TABLE Location ADD AttendantStyle NVARCHAR(20) NOT NULL DEFAULT 'Friendly';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantRandomizeConsent')
    ALTER TABLE Location ADD AttendantRandomizeConsent BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantRandomizeCountdown')
    ALTER TABLE Location ADD AttendantRandomizeCountdown BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantRandomizeCapturing')
    ALTER TABLE Location ADD AttendantRandomizeCapturing BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantRandomizeReviewing')
    ALTER TABLE Location ADD AttendantRandomizeReviewing BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantRandomizePrinting')
    ALTER TABLE Location ADD AttendantRandomizePrinting BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AttendantRandomizeComplete')
    ALTER TABLE Location ADD AttendantRandomizeComplete BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VirtualAttendantClip')
BEGIN
    CREATE TABLE VirtualAttendantClip (
        ClipId          INT IDENTITY(1,1) PRIMARY KEY,
        LocationId      INT             NOT NULL REFERENCES Location(LocationId),
        Stage           NVARCHAR(20)    NOT NULL CONSTRAINT CK_VirtualAttendantClip_Stage CHECK (Stage IN ('Setup', 'Idle', 'Consent', 'Countdown', 'Capturing', 'FilterPicker', 'Reviewing', 'FramePicker', 'Payment', 'Printing', 'Complete', 'Guestbook', 'Feedback', 'Survey', 'Error')),
        FilePath        NVARCHAR(500)   NOT NULL,
        SortOrder       INT             NOT NULL DEFAULT 0,
        CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_VirtualAttendantClip_Location_Stage ON VirtualAttendantClip(LocationId, Stage, SortOrder);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_VirtualAttendantClip_Stage')
BEGIN
    DECLARE @constraintName NVARCHAR(200);
    SELECT @constraintName = cc.name
    FROM sys.check_constraints cc
    JOIN sys.columns col ON col.object_id = cc.parent_object_id AND col.column_id = cc.parent_column_id
    WHERE cc.parent_object_id = OBJECT_ID('VirtualAttendantClip') AND col.name = 'Stage';

    IF @constraintName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE VirtualAttendantClip DROP CONSTRAINT [' + @constraintName + ']');
    END

    ALTER TABLE VirtualAttendantClip ADD CONSTRAINT CK_VirtualAttendantClip_Stage
        CHECK (Stage IN ('Setup', 'Idle', 'Consent', 'Countdown', 'Capturing', 'FilterPicker', 'Reviewing', 'FramePicker', 'Payment', 'Printing', 'Complete', 'Guestbook', 'Feedback', 'Survey', 'Error'));
END
