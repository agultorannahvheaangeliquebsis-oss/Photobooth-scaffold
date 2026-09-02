IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailFromAddress')
    ALTER TABLE Location ADD EmailFromAddress NVARCHAR(320) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailSubject')
    ALTER TABLE Location ADD EmailSubject NVARCHAR(200) NOT NULL DEFAULT 'Here is your photo';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailSmtpHost')
    ALTER TABLE Location ADD EmailSmtpHost NVARCHAR(200) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailSmtpPort')
    ALTER TABLE Location ADD EmailSmtpPort INT NOT NULL DEFAULT 587;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailSmtpUsername')
    ALTER TABLE Location ADD EmailSmtpUsername NVARCHAR(200) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailUseSsl')
    ALTER TABLE Location ADD EmailUseSsl BIT NOT NULL DEFAULT 1;
-- DPAPI-protected (current-user scope, see Photobooth.Core.SecretProtector), not
-- plaintext -- NVARCHAR(MAX) since the base64 ciphertext is longer than the
-- source password/token.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'EmailSmtpPasswordProtected')
    ALTER TABLE Location ADD EmailSmtpPasswordProtected NVARCHAR(MAX) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'TwilioAccountSid')
    ALTER TABLE Location ADD TwilioAccountSid NVARCHAR(100) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'TwilioFromNumber')
    ALTER TABLE Location ADD TwilioFromNumber NVARCHAR(20) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'TwilioAuthTokenProtected')
    ALTER TABLE Location ADD TwilioAuthTokenProtected NVARCHAR(MAX) NOT NULL DEFAULT '';
