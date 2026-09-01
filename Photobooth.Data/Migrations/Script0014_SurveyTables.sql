IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SurveyQuestion')
BEGIN
    CREATE TABLE SurveyQuestion (
        SurveyQuestionId INT IDENTITY(1,1) PRIMARY KEY,
        LocationId       INT             NOT NULL REFERENCES Location(LocationId),
        Text             NVARCHAR(300)   NOT NULL,
        SortOrder        INT             NOT NULL DEFAULT 0,
        IsActive         BIT             NOT NULL DEFAULT 1,
        CreatedAt        DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE TABLE SurveyResponse (
        SurveyResponseId INT IDENTITY(1,1) PRIMARY KEY,
        SessionId        INT             NOT NULL REFERENCES Session(SessionId),
        SurveyQuestionId INT             NOT NULL REFERENCES SurveyQuestion(SurveyQuestionId),
        Answer           NVARCHAR(1000)  NOT NULL,
        RecordedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_SurveyQuestion_Location_Active ON SurveyQuestion(LocationId, IsActive, SortOrder);
    CREATE INDEX IX_SurveyResponse_Session ON SurveyResponse(SessionId);
END
