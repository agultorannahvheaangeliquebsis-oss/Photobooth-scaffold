IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PrintTemplateElement') AND name = 'ShapeType')
    ALTER TABLE PrintTemplateElement ADD ShapeType NVARCHAR(20) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PrintTemplateElement') AND name = 'PhotoIndex')
    ALTER TABLE PrintTemplateElement ADD PhotoIndex INT NULL;

DECLARE @constraintName NVARCHAR(200);
DECLARE @constraintDefinition NVARCHAR(MAX);
SELECT TOP 1 @constraintName = cc.name, @constraintDefinition = cc.definition
FROM sys.check_constraints cc
JOIN sys.columns col ON col.object_id = cc.parent_object_id AND col.column_id = cc.parent_column_id
WHERE cc.parent_object_id = OBJECT_ID('PrintTemplateElement') AND col.name = 'Kind';

IF @constraintName IS NOT NULL AND @constraintDefinition NOT LIKE '%PhotoSlot%'
BEGIN
    EXEC('ALTER TABLE PrintTemplateElement DROP CONSTRAINT [' + @constraintName + ']');
    EXEC('ALTER TABLE PrintTemplateElement ADD CONSTRAINT CK_PrintTemplateElement_Kind CHECK (Kind IN (''Logo'', ''Text'', ''Image'', ''Shape'', ''QrCode'', ''SessionData'', ''PhotoSlot''))');
END
