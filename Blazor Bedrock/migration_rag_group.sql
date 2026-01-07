START TRANSACTION;
ALTER TABLE `ChatGptConversations` ADD `RagGroupId` int NULL;

CREATE TABLE `RagGroups` (
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

CREATE TABLE `RagGroupDocuments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RagGroupId` int NOT NULL,
    `DocumentId` int NOT NULL,
    `AddedAt` datetime(6) NOT NULL,
    `IsIndexed` tinyint(1) NOT NULL,
    CONSTRAINT `PK_RagGroupDocuments` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RagGroupDocuments_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_RagGroupDocuments_RagGroups_RagGroupId` FOREIGN KEY (`RagGroupId`) REFERENCES `RagGroups` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_ChatGptConversations_RagGroupId` ON `ChatGptConversations` (`RagGroupId`);

CREATE INDEX `IX_RagGroupDocuments_DocumentId` ON `RagGroupDocuments` (`DocumentId`);

CREATE UNIQUE INDEX `IX_RagGroupDocuments_RagGroupId_DocumentId` ON `RagGroupDocuments` (`RagGroupId`, `DocumentId`);

CREATE INDEX `IX_RagGroups_PineconeIndexName` ON `RagGroups` (`PineconeIndexName`);

CREATE INDEX `IX_RagGroups_TenantId_UserId` ON `RagGroups` (`TenantId`, `UserId`);

CREATE INDEX `IX_RagGroups_UserId` ON `RagGroups` (`UserId`);

ALTER TABLE `ChatGptConversations` ADD CONSTRAINT `FK_ChatGptConversations_RagGroups_RagGroupId` FOREIGN KEY (`RagGroupId`) REFERENCES `RagGroups` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260107214010_AddRagGroupIdToConversation', '9.0.0');

COMMIT;

