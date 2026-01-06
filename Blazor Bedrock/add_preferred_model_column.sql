-- Manual SQL to add PreferredModel column to Tenants table
-- Run this if the migration doesn't apply automatically
-- This script checks if the column exists first to avoid errors

SET @dbname = DATABASE();
SET @tablename = 'Tenants';
SET @columnname = 'PreferredModel';
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE
      (TABLE_SCHEMA = @dbname)
      AND (TABLE_NAME = @tablename)
      AND (COLUMN_NAME = @columnname)
  ) > 0,
  'SELECT 1', -- Column exists, do nothing
  CONCAT('ALTER TABLE `', @tablename, '` ADD COLUMN `', @columnname, '` varchar(100) CHARACTER SET utf8mb4 NULL')
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
