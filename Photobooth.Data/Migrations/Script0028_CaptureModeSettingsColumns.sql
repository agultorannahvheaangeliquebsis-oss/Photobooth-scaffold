-- Splits Capture Settings into four independently-configurable panels (see
-- dslrBooth's own Capture Settings screen): Photo/GIF/Boomerang/Video each
-- get their own Enabled flag and settings, instead of one shared FrameCount/
-- FrameDelayMs/VideoDurationSeconds behind a single Mode radio. The
-- pre-existing CaptureMode/AlsoCreateGif/GifFrameCount/GifFrameDelayMs/
-- VideoDurationSeconds columns (Script0012) are reused as-is -- see
-- PhotoCaptureSettings.AlsoCreateGif, GifCaptureSettings.FrameCount/
-- FrameDelayMs, VideoCaptureSettings.ClipDurationSeconds in
-- Photobooth.Core/IBoothSettingsProvider.cs.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PhotoEnabled')
    ALTER TABLE Location ADD PhotoEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PhotoBeforePhoto1Seconds')
    ALTER TABLE Location ADD PhotoBeforePhoto1Seconds INT NOT NULL DEFAULT 10;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PhotoBeforeOtherPhotosSeconds')
    ALTER TABLE Location ADD PhotoBeforeOtherPhotosSeconds INT NOT NULL DEFAULT 10;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PhotoReviewSeconds')
    ALTER TABLE Location ADD PhotoReviewSeconds INT NOT NULL DEFAULT 3;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifEnabled')
    ALTER TABLE Location ADD GifEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifSize')
    ALTER TABLE Location ADD GifSize NVARCHAR(40) NOT NULL DEFAULT 'Regular (720x480)';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifBeforePhoto1Seconds')
    ALTER TABLE Location ADD GifBeforePhoto1Seconds INT NOT NULL DEFAULT 5;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifBeforeOtherPhotosSeconds')
    ALTER TABLE Location ADD GifBeforeOtherPhotosSeconds INT NOT NULL DEFAULT 5;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifPhotoReviewSeconds')
    ALTER TABLE Location ADD GifPhotoReviewSeconds INT NOT NULL DEFAULT 3;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifReverseGif')
    ALTER TABLE Location ADD GifReverseGif BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'GifImageOverlayPath')
    ALTER TABLE Location ADD GifImageOverlayPath NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoomerangEnabled')
    ALTER TABLE Location ADD BoomerangEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoomerangSize')
    ALTER TABLE Location ADD BoomerangSize NVARCHAR(40) NOT NULL DEFAULT 'Regular (720x480)';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoomerangCountdownSeconds')
    ALTER TABLE Location ADD BoomerangCountdownSeconds INT NOT NULL DEFAULT 5;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoomerangFrameDelayMs')
    ALTER TABLE Location ADD BoomerangFrameDelayMs INT NOT NULL DEFAULT 50;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoomerangRecordingDurationSeconds')
    ALTER TABLE Location ADD BoomerangRecordingDurationSeconds INT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'BoomerangImageOverlayPath')
    ALTER TABLE Location ADD BoomerangImageOverlayPath NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoEnabled')
    ALTER TABLE Location ADD VideoEnabled BIT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoOrientationDegrees')
    ALTER TABLE Location ADD VideoOrientationDegrees INT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoSize')
    ALTER TABLE Location ADD VideoSize NVARCHAR(40) NOT NULL DEFAULT '1280x720';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoOutputQualityPercent')
    ALTER TABLE Location ADD VideoOutputQualityPercent INT NOT NULL DEFAULT 50;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoType')
    ALTER TABLE Location ADD VideoType NVARCHAR(20) NOT NULL DEFAULT 'Video' CHECK (VideoType IN ('Video', '360SlowMotion'));
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoNumberOfClips')
    ALTER TABLE Location ADD VideoNumberOfClips INT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoCountdownBeforeClip1Seconds')
    ALTER TABLE Location ADD VideoCountdownBeforeClip1Seconds INT NOT NULL DEFAULT 5;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoCountdownBeforeOtherClipsSeconds')
    ALTER TABLE Location ADD VideoCountdownBeforeOtherClipsSeconds INT NOT NULL DEFAULT 5;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoRecordOnMotionEnabled')
    ALTER TABLE Location ADD VideoRecordOnMotionEnabled BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoSoundtrackMp3Path')
    ALTER TABLE Location ADD VideoSoundtrackMp3Path NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoImageOverlayPath')
    ALTER TABLE Location ADD VideoImageOverlayPath NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoBeforeRecordingClipPath')
    ALTER TABLE Location ADD VideoBeforeRecordingClipPath NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'VideoAfterRecordingClipPath')
    ALTER TABLE Location ADD VideoAfterRecordingClipPath NVARCHAR(500) NULL;
