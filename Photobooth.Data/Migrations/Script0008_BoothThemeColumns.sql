IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AccentColorHex')
    ALTER TABLE Location ADD AccentColorHex NVARCHAR(9) NOT NULL DEFAULT '#365C58';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CanvasColorHex')
    ALTER TABLE Location ADD CanvasColorHex NVARCHAR(9) NOT NULL DEFAULT '#F4F3F0';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'InkColorHex')
    ALTER TABLE Location ADD InkColorHex NVARCHAR(9) NOT NULL DEFAULT '#202124';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'LogoImagePath')
    ALTER TABLE Location ADD LogoImagePath NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EventName')
    ALTER TABLE Location ADD EventName NVARCHAR(100) NOT NULL DEFAULT 'Focus & Snap';
