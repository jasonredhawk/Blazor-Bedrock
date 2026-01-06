-- Simple SQL to add PreferredModel column (run this in MySQL)
-- Database: BlazorBedrock
-- Table: Tenants

ALTER TABLE `Tenants` 
ADD COLUMN `PreferredModel` varchar(100) CHARACTER SET utf8mb4 NULL;
