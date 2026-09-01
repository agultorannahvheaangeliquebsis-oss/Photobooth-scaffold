IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'AdminPin')
    ALTER TABLE Location ADD AdminPin NVARCHAR(20) NOT NULL DEFAULT '1234';
