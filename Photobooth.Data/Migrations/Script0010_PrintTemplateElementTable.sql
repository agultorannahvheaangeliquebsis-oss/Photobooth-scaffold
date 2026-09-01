IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PrintTemplateElement')
BEGIN
    CREATE TABLE PrintTemplateElement (
        ElementId       INT IDENTITY(1,1) PRIMARY KEY,
        LocationId      INT             NOT NULL REFERENCES Location(LocationId),
        Kind            NVARCHAR(20)    NOT NULL CHECK (Kind IN ('Logo', 'Text', 'Image', 'Shape', 'QrCode', 'SessionData', 'PhotoSlot')),
        XPercent        DECIMAL(6,4)    NOT NULL,
        YPercent        DECIMAL(6,4)    NOT NULL,
        WidthPercent    DECIMAL(6,4)    NOT NULL,
        HeightPercent   DECIMAL(6,4)    NOT NULL,
        Text            NVARCHAR(200)   NULL,
        ImagePath       NVARCHAR(500)   NULL,
        FontFamily      NVARCHAR(100)   NULL,
        FontSizePercent DECIMAL(6,4)    NULL,
        Bold            BIT             NOT NULL DEFAULT 0,
        ColorHex        NVARCHAR(9)     NULL,
        ShapeType       NVARCHAR(20)    NULL,
        PhotoIndex      INT             NULL,
        SortOrder       INT             NOT NULL DEFAULT 0,
        CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_PrintTemplateElement_Location ON PrintTemplateElement(LocationId, SortOrder);
END
