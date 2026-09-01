IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintLayout')
    ALTER TABLE Location ADD PrintLayout NVARCHAR(20) NOT NULL DEFAULT 'Single' CHECK (PrintLayout IN ('Single', 'Strip'));
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintWidthInches')
    ALTER TABLE Location ADD PrintWidthInches DECIMAL(5,2) NOT NULL DEFAULT 4;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintHeightInches')
    ALTER TABLE Location ADD PrintHeightInches DECIMAL(5,2) NOT NULL DEFAULT 6;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintStripCopies')
    ALTER TABLE Location ADD PrintStripCopies INT NOT NULL DEFAULT 1;
