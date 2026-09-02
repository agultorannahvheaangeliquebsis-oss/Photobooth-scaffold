-- Saved print template library (see PrintTemplateRepository): a location can now
-- save several named/favoritable print setups, not just the one "live" setup its
-- own PrintLayout/PrintWidthInches/PrintHeightInches/PrintStripCopies columns and
-- PrintTemplateElement rows already held. Deliberately additive -- those existing
-- columns and rows stay exactly what BoothStateMachine/PrintCompositor read as the
-- location's live setup; this table is only ever read/written by the admin-facing
-- template editor and switcher.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PrintTemplate')
BEGIN
    CREATE TABLE PrintTemplate (
        PrintTemplateId INT IDENTITY(1,1) PRIMARY KEY,
        LocationId      INT             NOT NULL REFERENCES Location(LocationId),
        Name            NVARCHAR(100)   NOT NULL,
        Layout          NVARCHAR(20)    NOT NULL CHECK (Layout IN ('Single', 'Strip')),
        WidthInches     DECIMAL(5,2)    NOT NULL,
        HeightInches    DECIMAL(5,2)    NOT NULL,
        StripCopies     INT             NOT NULL DEFAULT 1,
        IsFavorite      BIT             NOT NULL DEFAULT 0,
        SortOrder       INT             NOT NULL DEFAULT 0,
        CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_PrintTemplate_Location ON PrintTemplate(LocationId, SortOrder);
END

-- Which saved template (if any) a PrintTemplateElement row belongs to. NULL is the
-- existing/default meaning: a row that belongs to the location's live setup, keyed
-- only by LocationId same as before Script0010. A non-NULL row is a saved snapshot
-- of one PrintTemplate library entry's elements, keyed by both columns (LocationId
-- kept alongside for symmetry with the live rows and so a template's elements can
-- never dangle without a location).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PrintTemplateElement') AND name = 'PrintTemplateId')
    ALTER TABLE PrintTemplateElement ADD PrintTemplateId INT NULL REFERENCES PrintTemplate(PrintTemplateId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PrintTemplateElement_Template' AND object_id = OBJECT_ID('PrintTemplateElement'))
    CREATE INDEX IX_PrintTemplateElement_Template ON PrintTemplateElement(PrintTemplateId, SortOrder);
