-- Thumbnail Strip style settings -- see PhotoThumbnailsEnabled/PoseStripPosition
-- (Script0020/Script0016) and ScreenTemplateEditorWindow's "Thumbnail Strip
-- Settings" section.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PoseStripBackgroundOpacityPercent')
    ALTER TABLE Location ADD PoseStripBackgroundOpacityPercent INT NOT NULL DEFAULT 45;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PoseStripActiveBorderColorHex')
    ALTER TABLE Location ADD PoseStripActiveBorderColorHex NVARCHAR(9) NOT NULL DEFAULT '#2ED9A0';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PoseStripShowPlaceholderNumbers')
    ALTER TABLE Location ADD PoseStripShowPlaceholderNumbers BIT NOT NULL DEFAULT 1;
