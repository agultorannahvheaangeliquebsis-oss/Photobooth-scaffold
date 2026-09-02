IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Location') AND name = 'PoseStripPosition')
    ALTER TABLE Location ADD PoseStripPosition NVARCHAR(10) NOT NULL DEFAULT 'Bottom' CHECK (PoseStripPosition IN ('Top', 'Bottom', 'Left', 'Right'));
