-- Full-breadth Screen Editor settings (Welcome/Capture/Sharing panels, see
-- ScreenTemplateEditorWindow) plus the two Sharing Settings channel toggles
-- (Twitter/Print) dslrBooth's own Sharing screen shows alongside Email/SMS/QR.

-- Welcome screen
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoothIconLabelsEnabled')
    ALTER TABLE Location ADD BoothIconLabelsEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeShowLiveView')
    ALTER TABLE Location ADD WelcomeShowLiveView BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'LiveTemplatePreview')
    ALTER TABLE Location ADD LiveTemplatePreview BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'StretchLiveView')
    ALTER TABLE Location ADD StretchLiveView NVARCHAR(40) NOT NULL DEFAULT 'Fill Screen With Cropping';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BrowseButtonEnabled')
    ALTER TABLE Location ADD BrowseButtonEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ChooseTemplateEnabled')
    ALTER TABLE Location ADD ChooseTemplateEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'StartScreenVideoPath')
    ALTER TABLE Location ADD StartScreenVideoPath NVARCHAR(400) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'UnlockButtonOpacityPercent')
    ALTER TABLE Location ADD UnlockButtonOpacityPercent INT NOT NULL DEFAULT 10;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SessionTriggerTouchScreen')
    ALTER TABLE Location ADD SessionTriggerTouchScreen BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SessionTriggerF13')
    ALTER TABLE Location ADD SessionTriggerF13 BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SessionTriggerKeys')
    ALTER TABLE Location ADD SessionTriggerKeys BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GuestQrCodeEnabled')
    ALTER TABLE Location ADD GuestQrCodeEnabled BIT NOT NULL DEFAULT 0;

-- Capture screen
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CropLiveView')
    ALTER TABLE Location ADD CropLiveView BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AutoTriggerCamera')
    ALTER TABLE Location ADD AutoTriggerCamera BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'FlashScreenWhite')
    ALTER TABLE Location ADD FlashScreenWhite BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowCancelButton')
    ALTER TABLE Location ADD ShowCancelButton BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CountdownColorHex')
    ALTER TABLE Location ADD CountdownColorHex NVARCHAR(9) NOT NULL DEFAULT '#2ED9A0';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PhotoThumbnailsEnabled')
    ALTER TABLE Location ADD PhotoThumbnailsEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SayCheeseImagePath')
    ALTER TABLE Location ADD SayCheeseImagePath NVARCHAR(400) NULL;

-- Sharing screen (screen chrome; distinct from the Sharing Settings channel
-- config columns Script0012/Script0017 already added)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SkipSharingScreen')
    ALTER TABLE Location ADD SkipSharingScreen BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowDoneButton')
    ALTER TABLE Location ADD ShowDoneButton BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingIconsLocation')
    ALTER TABLE Location ADD SharingIconsLocation NVARCHAR(40) NOT NULL DEFAULT 'Custom';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingTextLabelsEnabled')
    ALTER TABLE Location ADD SharingTextLabelsEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'FinalScreenTimeoutSeconds')
    ALTER TABLE Location ADD FinalScreenTimeoutSeconds INT NOT NULL DEFAULT 30;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowOriginalPhotos')
    ALTER TABLE Location ADD ShowOriginalPhotos BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowRetakeButton')
    ALTER TABLE Location ADD ShowRetakeButton BIT NOT NULL DEFAULT 0;

-- Sharing Settings channel toggles (SharingSettings.TwitterEnabled/PrintEnabled)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'TwitterEnabled')
    ALTER TABLE Location ADD TwitterEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintEnabled')
    ALTER TABLE Location ADD PrintEnabled BIT NOT NULL DEFAULT 0;
