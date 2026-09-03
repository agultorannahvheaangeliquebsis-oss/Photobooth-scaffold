IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'CameraDeviceName')
    ALTER TABLE Location ADD CameraDeviceName NVARCHAR(200) NULL;
