-- Safe migration script for adding RagGroupId to ChatGptConversations
-- This script checks for existing objects before creating/altering them

START TRANSACTION;

-- Add RagGroupId column if it doesn't exist
SET @dbname = DATABASE();
SET @tablename = "ChatGptConversations";
SET @columnname = "RagGroupId";
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE
      (table_name = @tablename)
      AND (table_schema = @dbname)
      AND (column_name = @columnname)
  ) > 0,
  "SELECT 'Column already exists.'",
  CONCAT("ALTER TABLE ", @tablename, " ADD ", @columnname, " int NULL")
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- Create RagGroups table if it doesn't exist
CREATE TABLE IF NOT EXISTS `RagGroups` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `Description` varchar(1000) CHARACTER SET utf8mb4 NULL,
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `TenantId` int NOT NULL,
    `TopK` int NOT NULL DEFAULT 5,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `PineconeIndexName` varchar(200) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_RagGroups` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RagGroups_Tenants_TenantId` FOREIGN KEY (`TenantId`) REFERENCES `Tenants` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_RagGroups_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

-- Create RagGroupDocuments table if it doesn't exist
CREATE TABLE IF NOT EXISTS `RagGroupDocuments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RagGroupId` int NOT NULL,
    `DocumentId` int NOT NULL,
    `AddedAt` datetime(6) NOT NULL,
    `IsIndexed` tinyint(1) NOT NULL,
    CONSTRAINT `PK_RagGroupDocuments` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RagGroupDocuments_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_RagGroupDocuments_RagGroups_RagGroupId` FOREIGN KEY (`RagGroupId`) REFERENCES `RagGroups` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

-- Create indexes if they don't exist (MySQL doesn't support IF NOT EXISTS for indexes, so we check first)
SET @index_name = 'IX_ChatGptConversations_RagGroupId';
SET @index_exists = (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS 
    WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'ChatGptConversations'
    AND INDEX_NAME = @index_name
);
SET @index_sql = IF(@index_exists = 0,
    'CREATE INDEX `IX_ChatGptConversations_RagGroupId` ON `ChatGptConversations` (`RagGroupId`)',
    'SELECT ''Index already exists.''');
PREPARE idx_stmt FROM @index_sql;
EXECUTE idx_stmt;
DEALLOCATE PREPARE idx_stmt;

-- Note: Other indexes on RagGroups and RagGroupDocuments tables will be created when those tables are created
-- If the tables already exist, you may need to add these indexes manually if they don't exist

-- Add foreign key constraint if it doesn't exist
SET @fk_name = 'FK_ChatGptConversations_RagGroups_RagGroupId';
SET @fk_exists = (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
    WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME = 'ChatGptConversations'
    AND CONSTRAINT_NAME = @fk_name
    AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);

SET @fk_sql = IF(@fk_exists = 0,
    CONCAT('ALTER TABLE `ChatGptConversations` ADD CONSTRAINT `', @fk_name, '` FOREIGN KEY (`RagGroupId`) REFERENCES `RagGroups` (`Id`) ON DELETE SET NULL'),
    'SELECT ''Foreign key already exists.''');
PREPARE fk_stmt FROM @fk_sql;
EXECUTE fk_stmt;
DEALLOCATE PREPARE fk_stmt;

-- Record migration in EF migrations history if not already recorded
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260107214010_AddRagGroupIdToConversation', '9.0.0');

COMMIT;
