IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'SaveMirroredPhotos')
    ALTER TABLE Location ADD SaveMirroredPhotos BIT NOT NULL DEFAULT 1;
