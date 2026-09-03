IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PaymentProvider')
    ALTER TABLE Location ADD PaymentProvider NVARCHAR(20) NOT NULL DEFAULT 'Mock';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PayMongoWalletType')
    ALTER TABLE Location ADD PayMongoWalletType NVARCHAR(20) NOT NULL DEFAULT 'gcash';
-- DPAPI-protected (current-user scope, see Photobooth.Core.SecretProtector), not
-- plaintext -- NVARCHAR(MAX) since the base64 ciphertext is longer than the
-- source key, same reasoning EmailSmtpPasswordProtected/TwilioAuthTokenProtected
-- already established (see Script0017_SharingDeliveryConfigColumns.sql).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PayMongoSecretKeyProtected')
    ALTER TABLE Location ADD PayMongoSecretKeyProtected NVARCHAR(MAX) NOT NULL DEFAULT '';
