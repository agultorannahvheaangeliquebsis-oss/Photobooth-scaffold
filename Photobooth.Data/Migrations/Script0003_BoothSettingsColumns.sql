IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CountdownSeconds')
    ALTER TABLE Location ADD CountdownSeconds INT NOT NULL DEFAULT 3;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GlamFilterEnabled')
    ALTER TABLE Location ADD GlamFilterEnabled BIT NOT NULL DEFAULT 0;
