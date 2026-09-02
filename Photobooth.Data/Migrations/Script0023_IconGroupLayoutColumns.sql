-- Free-position icon/button groups (Welcome's Booth Icons, Sharing's icon
-- row, Capture's Cancel button) -- see IconGroupLayout and
-- ScreenTemplateEditorWindow's per-screen icon-group drag/layout/align UI.

-- Welcome screen: Booth Icons group (Photo/GIF/Boomerang/Video mode tiles)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomePhotoIconEnabled')
    ALTER TABLE Location ADD WelcomePhotoIconEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeGifIconEnabled')
    ALTER TABLE Location ADD WelcomeGifIconEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeBoomerangIconEnabled')
    ALTER TABLE Location ADD WelcomeBoomerangIconEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeVideoIconEnabled')
    ALTER TABLE Location ADD WelcomeVideoIconEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeIconsPositionXPercent')
    ALTER TABLE Location ADD WelcomeIconsPositionXPercent FLOAT NOT NULL DEFAULT 0.27;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeIconsPositionYPercent')
    ALTER TABLE Location ADD WelcomeIconsPositionYPercent FLOAT NOT NULL DEFAULT 0.72;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeIconsLayout')
    ALTER TABLE Location ADD WelcomeIconsLayout NVARCHAR(20) NOT NULL DEFAULT 'Row';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeIconsAlignment')
    ALTER TABLE Location ADD WelcomeIconsAlignment NVARCHAR(20) NOT NULL DEFAULT 'Center';

-- Capture screen: Cancel button (single-item group, no Layout/Alignment)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CaptureCancelButtonPositionXPercent')
    ALTER TABLE Location ADD CaptureCancelButtonPositionXPercent FLOAT NOT NULL DEFAULT 0.5;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CaptureCancelButtonPositionYPercent')
    ALTER TABLE Location ADD CaptureCancelButtonPositionYPercent FLOAT NOT NULL DEFAULT 0.93;

-- Sharing screen: icon row (QR/Email/SMS/Print) -- per-icon enable already
-- exists (SharingSettings.QrEnabled/EmailEnabled/SmsEnabled/PrintEnabled),
-- this only adds the group-wide enable + free position/layout/alignment.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingIconsGroupEnabled')
    ALTER TABLE Location ADD SharingIconsGroupEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingIconsPositionXPercent')
    ALTER TABLE Location ADD SharingIconsPositionXPercent FLOAT NOT NULL DEFAULT 0.56;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingIconsPositionYPercent')
    ALTER TABLE Location ADD SharingIconsPositionYPercent FLOAT NOT NULL DEFAULT 0.32;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingIconsLayout')
    ALTER TABLE Location ADD SharingIconsLayout NVARCHAR(20) NOT NULL DEFAULT 'Column';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingIconsAlignment')
    ALTER TABLE Location ADD SharingIconsAlignment NVARCHAR(20) NOT NULL DEFAULT 'Start';

-- One-time backfill: BoothIconsEnabled used to gate nothing on screen (see
-- ScreenSettings' own doc comment), so an existing row's stored value --
-- almost always the column's own DEFAULT 0 -- was never a real admin choice.
-- Now that it hides the Welcome mode tiles for real, flip every still-default
-- row to match the mode tiles' previous always-visible behavior so this
-- ships without silently hiding mode selection on any already-running booth.
UPDATE Location SET BoothIconsEnabled = 1 WHERE BoothIconsEnabled = 0;
