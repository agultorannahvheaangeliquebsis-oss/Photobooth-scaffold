IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PaymentTiming')
    ALTER TABLE Location ADD PaymentTiming NVARCHAR(20) NOT NULL DEFAULT 'SharingScreen'
        CHECK (PaymentTiming IN ('SharingScreen', 'StartScreen'));
