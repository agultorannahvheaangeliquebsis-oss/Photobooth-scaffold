IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CustomFilter')
BEGIN
    CREATE TABLE CustomFilter (
        CustomFilterId  INT IDENTITY(1,1) PRIMARY KEY,
        LocationId      INT             NOT NULL REFERENCES Location(LocationId),
        Name            NVARCHAR(100)   NOT NULL,
        CubeFilePath    NVARCHAR(500)   NOT NULL,
        SortOrder       INT             NOT NULL DEFAULT 0,
        IsActive        BIT             NOT NULL DEFAULT 1,
        CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_CustomFilter_Location_Active ON CustomFilter(LocationId, IsActive, SortOrder);
END
