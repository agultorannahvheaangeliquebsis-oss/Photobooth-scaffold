-- Per-screen background color + optional background image (Welcome/Capture/
-- Sharing), see ScreenTemplateEditorWindow's new Background sub-section and
-- KioskWindow's per-screen Background/background Image bindings. Colors
-- default to '#17181A', matching KioskDark.xaml's previously-fixed
-- KioskCanvasBrush, so an existing install renders identically until an
-- admin changes one.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeBackgroundColorHex')
    ALTER TABLE Location ADD WelcomeBackgroundColorHex NVARCHAR(9) NOT NULL DEFAULT '#17181A';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'WelcomeBackgroundImagePath')
    ALTER TABLE Location ADD WelcomeBackgroundImagePath NVARCHAR(400) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CaptureBackgroundColorHex')
    ALTER TABLE Location ADD CaptureBackgroundColorHex NVARCHAR(9) NOT NULL DEFAULT '#17181A';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CaptureBackgroundImagePath')
    ALTER TABLE Location ADD CaptureBackgroundImagePath NVARCHAR(400) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingBackgroundColorHex')
    ALTER TABLE Location ADD SharingBackgroundColorHex NVARCHAR(9) NOT NULL DEFAULT '#17181A';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SharingBackgroundImagePath')
    ALTER TABLE Location ADD SharingBackgroundImagePath NVARCHAR(400) NULL;
