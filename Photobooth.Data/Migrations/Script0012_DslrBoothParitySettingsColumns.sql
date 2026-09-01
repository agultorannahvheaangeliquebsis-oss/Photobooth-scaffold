IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CaptureMode')
    ALTER TABLE Location ADD CaptureMode NVARCHAR(20) NOT NULL DEFAULT 'Photo' CHECK (CaptureMode IN ('Photo', 'GIF', 'Boomerang', 'Video'));
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AlsoCreateGif')
    ALTER TABLE Location ADD AlsoCreateGif BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifFrameCount')
    ALTER TABLE Location ADD GifFrameCount INT NOT NULL DEFAULT 4;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifFrameDelayMs')
    ALTER TABLE Location ADD GifFrameDelayMs INT NOT NULL DEFAULT 500;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoDurationSeconds')
    ALTER TABLE Location ADD VideoDurationSeconds INT NOT NULL DEFAULT 10;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoothIconsEnabled')
    ALTER TABLE Location ADD BoothIconsEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowLiveView')
    ALTER TABLE Location ADD ShowLiveView BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'MirrorLiveView')
    ALTER TABLE Location ADD MirrorLiveView BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'LiveViewRotation')
    ALTER TABLE Location ADD LiveViewRotation INT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EnableWebcams')
    ALTER TABLE Location ADD EnableWebcams BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WebcamResolutionQuality')
    ALTER TABLE Location ADD WebcamResolutionQuality INT NOT NULL DEFAULT 70;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AudioInputDeviceName')
    ALTER TABLE Location ADD AudioInputDeviceName NVARCHAR(200) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BeautyFilterEnabled')
    ALTER TABLE Location ADD BeautyFilterEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BeautyFilterAlsoDuringCountdown')
    ALTER TABLE Location ADD BeautyFilterAlsoDuringCountdown BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'FiltersMode')
    ALTER TABLE Location ADD FiltersMode NVARCHAR(20) NOT NULL DEFAULT 'Ask' CHECK (FiltersMode IN ('Ask', 'Auto'));
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'FiltersEnabled')
    ALTER TABLE Location ADD FiltersEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EnabledFilterPresetIds')
    ALTER TABLE Location ADD EnabledFilterPresetIds NVARCHAR(300) NOT NULL DEFAULT 'Original,BlackAndWhiteGlam,BlackAndWhite,Filter1977,Brannan,Gotham,Hefe,LordKelvin,Nashville';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PostProcessingEnabled')
    ALTER TABLE Location ADD PostProcessingEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PostProcessingApplicationPath')
    ALTER TABLE Location ADD PostProcessingApplicationPath NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'StickersEnabled')
    ALTER TABLE Location ADD StickersEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WatermarkImagePath')
    ALTER TABLE Location ADD WatermarkImagePath NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WatermarkEnabled')
    ALTER TABLE Location ADD WatermarkEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GreenScreenEnabled')
    ALTER TABLE Location ADD GreenScreenEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GreenScreenBackgroundPath')
    ALTER TABLE Location ADD GreenScreenBackgroundPath NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SurveyEnabled')
    ALTER TABLE Location ADD SurveyEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'DisclaimerHeader')
    ALTER TABLE Location ADD DisclaimerHeader NVARCHAR(200) NOT NULL DEFAULT 'Do you agree with the terms?';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'DisclaimerText')
    ALTER TABLE Location ADD DisclaimerText NVARCHAR(MAX) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintAutomatically')
    ALTER TABLE Location ADD PrintAutomatically BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'ShowPrintButton')
    ALTER TABLE Location ADD ShowPrintButton BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintLimitPerEvent')
    ALTER TABLE Location ADD PrintLimitPerEvent INT NOT NULL DEFAULT 5000;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintLimitPerSession')
    ALTER TABLE Location ADD PrintLimitPerSession INT NOT NULL DEFAULT 3;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PrintSharpening')
    ALTER TABLE Location ADD PrintSharpening NVARCHAR(10) NOT NULL DEFAULT 'Medium' CHECK (PrintSharpening IN ('Low', 'Medium', 'High'));
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailEnabled')
    ALTER TABLE Location ADD EmailEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SmsEnabled')
    ALTER TABLE Location ADD SmsEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'QrEnabled')
    ALTER TABLE Location ADD QrEnabled BIT NOT NULL DEFAULT 1;
