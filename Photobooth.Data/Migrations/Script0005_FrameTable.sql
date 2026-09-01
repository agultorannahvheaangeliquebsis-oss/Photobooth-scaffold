IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Frame')
BEGIN
    CREATE TABLE Frame (
        FrameId         INT IDENTITY(1,1) PRIMARY KEY,
        LocationId      INT             NOT NULL REFERENCES Location(LocationId),
        Name            NVARCHAR(100)   NOT NULL,
        ImagePath       NVARCHAR(500)   NOT NULL,
        SortOrder       INT             NOT NULL DEFAULT 0,
        IsActive        BIT             NOT NULL DEFAULT 1,
        CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Frame_Location_Active ON Frame(LocationId, IsActive, SortOrder);
END
