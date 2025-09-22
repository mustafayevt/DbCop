-- ========================================
-- ORPHANED USER CLEANUP SCRIPT - GENERIC TEMPLATE
-- ========================================
-- Purpose: Removes orphaned users from target database after BACPAC import
-- Usage: Replace placeholders with your actual values and run on TARGET server
-- IMPORTANT: Run this AFTER BACPAC import fails with orphaned user errors
-- Author: DbCop Tool
-- Date: {DATE}
-- ========================================

-- CONFIGURATION SECTION - MODIFY THESE VALUES
-- ========================================
DECLARE @TargetDatabaseName NVARCHAR(128) = '{TARGET_DATABASE_NAME}'  -- Replace with your target database name
DECLARE @DryRun BIT = 1  -- 1=Show what would be done (safe), 0=Actually execute changes
DECLARE @LogLevel INT = 2  -- 1=Errors only, 2=Normal, 3=Verbose

-- Specific users to target (leave empty to target all orphaned users)
DECLARE @SpecificUsers TABLE (UserName NVARCHAR(128))
-- Uncomment and customize these lines for specific users:
-- INSERT @SpecificUsers VALUES ('BrianHayes')
-- INSERT @SpecificUsers VALUES ('Cust9999')
-- INSERT @SpecificUsers VALUES ('SomeOtherUser')

-- Users to NEVER remove (safety list)
DECLARE @ProtectedUsers TABLE (UserName NVARCHAR(128))
INSERT @ProtectedUsers VALUES ('dbo')
INSERT @ProtectedUsers VALUES ('guest')
INSERT @ProtectedUsers VALUES ('INFORMATION_SCHEMA')
INSERT @ProtectedUsers VALUES ('sys')
INSERT @ProtectedUsers VALUES ('public')
-- Add any additional users you want to protect:
-- INSERT @ProtectedUsers VALUES ('ImportantUser')

-- ========================================
-- CLEANUP LOGIC - DO NOT MODIFY BELOW
-- ========================================

-- Validation
IF NULLIF(@TargetDatabaseName, '') IS NULL
BEGIN
    SET @TargetDatabaseName = DB_NAME()
    PRINT 'Using current database: ' + @TargetDatabaseName
END

-- Header
PRINT REPLICATE('=', 70)
PRINT 'ORPHANED USER CLEANUP SCRIPT'
PRINT 'Target Database: ' + @TargetDatabaseName
PRINT 'Server: ' + @@SERVERNAME
PRINT 'Date: ' + CONVERT(VARCHAR, GETDATE(), 121)
PRINT 'Mode: ' + CASE WHEN @DryRun = 1 THEN 'DRY RUN (Safe Preview)' ELSE 'EXECUTE (Live Changes)' END
PRINT REPLICATE('=', 70)
PRINT ''

-- Check database exists
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @TargetDatabaseName)
BEGIN
    PRINT '❌ ERROR: Database [' + @TargetDatabaseName + '] does not exist!'
    RETURN
END

-- Build dynamic SQL for cross-database operations
DECLARE @SQL NVARCHAR(MAX)
DECLARE @CleanupSQL NVARCHAR(MAX) = ''
DECLARE @RoleCleanupSQL NVARCHAR(MAX) = ''
DECLARE @UserCount INT = 0
DECLARE @RoleCount INT = 0

-- Get orphaned user count and details
SET @SQL = N'
USE [' + @TargetDatabaseName + N']

SELECT @UserCount = COUNT(*)
FROM sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (SELECT UserName FROM @ProtectedUsers)
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.sid IS NULL
  AND (
    NOT EXISTS (SELECT 1 FROM @SpecificUsers) 
    OR dp.name IN (SELECT UserName FROM @SpecificUsers)
  )
'

EXEC sp_executesql @SQL, 
    N'@UserCount INT OUTPUT, @ProtectedUsers TABLE (UserName NVARCHAR(128)) READONLY, @SpecificUsers TABLE (UserName NVARCHAR(128)) READONLY', 
    @UserCount OUTPUT, @ProtectedUsers, @SpecificUsers

IF @LogLevel >= 2
    PRINT 'Found ' + CAST(@UserCount AS VARCHAR) + ' orphaned user(s) to process.'

IF @UserCount = 0
BEGIN
    PRINT '✅ No orphaned users found matching criteria!'
    PRINT 'Database appears to be clean or all users have valid server logins.'
    RETURN
END

-- Show what will be processed
PRINT ''
PRINT '--- ORPHANED USERS TO BE PROCESSED ---'

SET @SQL = N'
USE [' + @TargetDatabaseName + N']

SELECT 
    ROW_NUMBER() OVER (ORDER BY dp.name) AS [#],
    dp.name AS [User Name],
    dp.type_desc AS [Type],
    FORMAT(dp.create_date, ''yyyy-MM-dd HH:mm'') AS [Created],
    dp.default_schema_name AS [Schema],
    CASE WHEN @DryRun = 1 THEN ''WOULD DELETE'' ELSE ''WILL DELETE'' END AS [Action]
FROM sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (SELECT UserName FROM @ProtectedUsers)
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.sid IS NULL
  AND (
    NOT EXISTS (SELECT 1 FROM @SpecificUsers) 
    OR dp.name IN (SELECT UserName FROM @SpecificUsers)
  )
ORDER BY dp.name
'

EXEC sp_executesql @SQL, 
    N'@DryRun BIT, @ProtectedUsers TABLE (UserName NVARCHAR(128)) READONLY, @SpecificUsers TABLE (UserName NVARCHAR(128)) READONLY', 
    @DryRun, @ProtectedUsers, @SpecificUsers

-- Show role cleanup
PRINT ''
PRINT '--- ROLE MEMBERSHIPS TO BE CLEANED ---'

SET @SQL = N'
USE [' + @TargetDatabaseName + N']

SELECT 
    dp_member.name AS [User],
    STRING_AGG(dp_role.name, '', '') WITHIN GROUP (ORDER BY dp_role.name) AS [Roles to Remove From]
FROM sys.database_role_members rm
JOIN sys.database_principals dp_role ON rm.role_principal_id = dp_role.principal_id
JOIN sys.database_principals dp_member ON rm.member_principal_id = dp_member.principal_id
LEFT JOIN sys.server_principals sp ON dp_member.sid = sp.sid
WHERE dp_member.type IN (''S'', ''U'')
  AND dp_member.name NOT IN (SELECT UserName FROM @ProtectedUsers)
  AND sp.sid IS NULL
  AND (
    NOT EXISTS (SELECT 1 FROM @SpecificUsers) 
    OR dp_member.name IN (SELECT UserName FROM @SpecificUsers)
  )
GROUP BY dp_member.name
ORDER BY dp_member.name
'

EXEC sp_executesql @SQL, 
    N'@ProtectedUsers TABLE (UserName NVARCHAR(128)) READONLY, @SpecificUsers TABLE (UserName NVARCHAR(128)) READONLY', 
    @ProtectedUsers, @SpecificUsers

PRINT ''

IF @DryRun = 1
BEGIN
    PRINT '🔍 DRY RUN MODE - No changes will be made'
    PRINT 'Set @DryRun = 0 to execute the cleanup'
    PRINT ''
    PRINT 'Preview of commands that would be executed:'
    PRINT ''
END
ELSE
BEGIN
    PRINT '⚠️  EXECUTING CLEANUP - Changes will be made!'
    PRINT ''
END

-- Generate role cleanup commands
SET @SQL = N'
USE [' + @TargetDatabaseName + N']

SELECT @RoleCleanupSQL = STRING_AGG(
    ''ALTER ROLE ['' + dp_role.name + ''] DROP MEMBER ['' + dp_member.name + ''];'',
    CHAR(13)
) WITHIN GROUP (ORDER BY dp_member.name, dp_role.name)
FROM sys.database_role_members rm
JOIN sys.database_principals dp_role ON rm.role_principal_id = dp_role.principal_id
JOIN sys.database_principals dp_member ON rm.member_principal_id = dp_member.principal_id
LEFT JOIN sys.server_principals sp ON dp_member.sid = sp.sid
WHERE dp_member.type IN (''S'', ''U'')
  AND dp_member.name NOT IN (SELECT UserName FROM @ProtectedUsers)
  AND sp.sid IS NULL
  AND (
    NOT EXISTS (SELECT 1 FROM @SpecificUsers) 
    OR dp_member.name IN (SELECT UserName FROM @SpecificUsers)
  )
'

EXEC sp_executesql @SQL, 
    N'@RoleCleanupSQL NVARCHAR(MAX) OUTPUT, @ProtectedUsers TABLE (UserName NVARCHAR(128)) READONLY, @SpecificUsers TABLE (UserName NVARCHAR(128)) READONLY', 
    @RoleCleanupSQL OUTPUT, @ProtectedUsers, @SpecificUsers

-- Generate user drop commands
SET @SQL = N'
USE [' + @TargetDatabaseName + N']

SELECT @CleanupSQL = STRING_AGG(
    ''DROP USER ['' + dp.name + ''];'',
    CHAR(13)
) WITHIN GROUP (ORDER BY dp.name)
FROM sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (SELECT UserName FROM @ProtectedUsers)
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.sid IS NULL
  AND (
    NOT EXISTS (SELECT 1 FROM @SpecificUsers) 
    OR dp.name IN (SELECT UserName FROM @SpecificUsers)
  )
'

EXEC sp_executesql @SQL, 
    N'@CleanupSQL NVARCHAR(MAX) OUTPUT, @ProtectedUsers TABLE (UserName NVARCHAR(128)) READONLY, @SpecificUsers TABLE (UserName NVARCHAR(128)) READONLY', 
    @CleanupSQL OUTPUT, @ProtectedUsers, @SpecificUsers

-- Execute or preview cleanup
IF ISNULL(@RoleCleanupSQL, '') != '' OR ISNULL(@CleanupSQL, '') != ''
BEGIN
    PRINT '-- Step 1: Remove users from roles --'
    IF ISNULL(@RoleCleanupSQL, '') != ''
    BEGIN
        IF @DryRun = 1
            PRINT @RoleCleanupSQL
        ELSE
        BEGIN
            SET @SQL = 'USE [' + @TargetDatabaseName + '] ' + @RoleCleanupSQL
            EXEC sp_executesql @SQL
            PRINT '✅ Role cleanup completed'
        END
    END
    ELSE
        PRINT '-- No role memberships to clean up'
    
    PRINT ''
    PRINT '-- Step 2: Drop orphaned users --'
    IF ISNULL(@CleanupSQL, '') != ''
    BEGIN
        IF @DryRun = 1
            PRINT @CleanupSQL
        ELSE
        BEGIN
            SET @SQL = 'USE [' + @TargetDatabaseName + '] ' + @CleanupSQL
            EXEC sp_executesql @SQL
            PRINT '✅ User cleanup completed'
        END
    END
    ELSE
        PRINT '-- No users to drop'
        
    PRINT ''
    
    IF @DryRun = 0
    BEGIN
        PRINT '✅ CLEANUP COMPLETED SUCCESSFULLY!'
        PRINT 'Orphaned users have been removed from the database.'
        PRINT 'You can now retry your BACPAC operations.'
    END
END
ELSE
BEGIN
    PRINT 'No cleanup commands generated - nothing to do!'
END

PRINT ''
PRINT REPLICATE('=', 70)
PRINT 'CLEANUP SCRIPT COMPLETE'
PRINT REPLICATE('=', 70)