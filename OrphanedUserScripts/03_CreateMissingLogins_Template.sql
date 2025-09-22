-- ========================================
-- CREATE MISSING LOGINS SCRIPT - GENERIC TEMPLATE
-- ========================================
-- Purpose: Creates server logins for orphaned database users
-- Usage: Replace placeholders and run on TARGET server BEFORE BACPAC import
-- Alternative to cleanup - preserves users by creating matching server logins
-- Author: DbCop Tool
-- Date: {DATE}
-- ========================================

-- CONFIGURATION SECTION - MODIFY THESE VALUES
-- ========================================
DECLARE @SourceDatabaseName NVARCHAR(128) = '{SOURCE_DATABASE_NAME}'  -- Database with orphaned users
DECLARE @DefaultPassword NVARCHAR(128) = 'TempPassword123!'  -- Default password for SQL logins
DECLARE @DryRun BIT = 1  -- 1=Show what would be done, 0=Actually create logins
DECLARE @CreateSQLLogins BIT = 1  -- 1=Create SQL logins, 0=Skip SQL logins
DECLARE @CreateWindowsLogins BIT = 0  -- 1=Attempt Windows logins, 0=Skip (requires domain access)
DECLARE @ForcePasswordPolicy BIT = 0  -- 1=Enforce password policy, 0=Disable for temp passwords

-- Specific users to create (leave empty to process all orphaned users)
DECLARE @SpecificUsers TABLE (UserName NVARCHAR(128), LoginType CHAR(1)) -- 'S'=SQL, 'U'=Windows
-- Uncomment and customize for specific users:
-- INSERT @SpecificUsers VALUES ('BrianHayes', 'S')  -- SQL Login
-- INSERT @SpecificUsers VALUES ('Cust9999', 'S')    -- SQL Login
-- INSERT @SpecificUsers VALUES ('DOMAIN\User1', 'U') -- Windows Login

-- ========================================
-- LOGIN CREATION LOGIC - DO NOT MODIFY BELOW
-- ========================================

USE master  -- Must use master for login operations

-- Header
PRINT REPLICATE('=', 70)
PRINT 'CREATE MISSING LOGINS SCRIPT'
PRINT 'Source Database: ' + ISNULL(@SourceDatabaseName, 'Not Specified')
PRINT 'Target Server: ' + @@SERVERNAME
PRINT 'Date: ' + CONVERT(VARCHAR, GETDATE(), 121)
PRINT 'Mode: ' + CASE WHEN @DryRun = 1 THEN 'DRY RUN (Safe Preview)' ELSE 'EXECUTE (Create Logins)' END
PRINT REPLICATE('=', 70)
PRINT ''

-- Validation
IF ISNULL(@SourceDatabaseName, '') = ''
BEGIN
    PRINT '❌ ERROR: @SourceDatabaseName must be specified!'
    PRINT 'Set the source database name where orphaned users exist.'
    RETURN
END

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @SourceDatabaseName)
BEGIN
    PRINT '❌ ERROR: Database [' + @SourceDatabaseName + '] does not exist on this server!'
    PRINT 'Available databases:'
    SELECT name AS AvailableDatabases FROM sys.databases WHERE database_id > 4 ORDER BY name
    RETURN
END

-- Get orphaned users from source database
DECLARE @SQL NVARCHAR(MAX)
DECLARE @LoginCount INT = 0

-- Count orphaned users that need logins
SET @SQL = N'
SELECT @LoginCount = COUNT(*)
FROM [' + @SourceDatabaseName + N'].sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.name = sp.name COLLATE SQL_Latin1_General_CP1_CI_AS
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.name IS NULL  -- No corresponding server login
  AND (
    NOT EXISTS (SELECT 1 FROM @SpecificUsers) 
    OR dp.name IN (SELECT UserName FROM @SpecificUsers)
  )
  AND (
    (@CreateSQLLogins = 1 AND dp.type = ''S'') OR
    (@CreateWindowsLogins = 1 AND dp.type = ''U'')
  )
'

EXEC sp_executesql @SQL, 
    N'@LoginCount INT OUTPUT, @SpecificUsers TABLE (UserName NVARCHAR(128), LoginType CHAR(1)) READONLY, @CreateSQLLogins BIT, @CreateWindowsLogins BIT', 
    @LoginCount OUTPUT, @SpecificUsers, @CreateSQLLogins, @CreateWindowsLogins

PRINT 'Found ' + CAST(@LoginCount AS VARCHAR) + ' orphaned user(s) that need server logins.'
PRINT ''

IF @LoginCount = 0
BEGIN
    PRINT '✅ No orphaned users found that need logins created!'
    PRINT 'Either no orphaned users exist, or they don''t match your filter criteria.'
    RETURN
END

-- Show what will be created
PRINT '--- LOGINS TO BE CREATED ---'

SET @SQL = N'
SELECT 
    ROW_NUMBER() OVER (ORDER BY dp.name) AS [#],
    dp.name AS [Login Name],
    CASE dp.type 
        WHEN ''S'' THEN ''SQL Server Login''
        WHEN ''U'' THEN ''Windows Login''
        ELSE dp.type_desc 
    END AS [Login Type],
    CASE dp.type
        WHEN ''S'' THEN ''Password: '' + @DefaultPassword
        WHEN ''U'' THEN ''Windows Authentication''
        ELSE ''Unknown''
    END AS [Authentication],
    CASE WHEN @DryRun = 1 THEN ''WOULD CREATE'' ELSE ''WILL CREATE'' END AS [Action],
    CASE WHEN sp.name IS NOT NULL THEN ''⚠️  Already Exists'' ELSE ''✅ New Login'' END AS [Status]
FROM [' + @SourceDatabaseName + N'].sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.name = sp.name COLLATE SQL_Latin1_General_CP1_CI_AS
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND (
    NOT EXISTS (SELECT 1 FROM @SpecificUsers) 
    OR dp.name IN (SELECT UserName FROM @SpecificUsers)
  )
  AND (
    (@CreateSQLLogins = 1 AND dp.type = ''S'') OR
    (@CreateWindowsLogins = 1 AND dp.type = ''U'')
  )
ORDER BY dp.type, dp.name
'

EXEC sp_executesql @SQL, 
    N'@SpecificUsers TABLE (UserName NVARCHAR(128), LoginType CHAR(1)) READONLY, @CreateSQLLogins BIT, @CreateWindowsLogins BIT, @DefaultPassword NVARCHAR(128), @DryRun BIT', 
    @SpecificUsers, @CreateSQLLogins, @CreateWindowsLogins, @DefaultPassword, @DryRun

PRINT ''

IF @DryRun = 1
BEGIN
    PRINT '🔍 DRY RUN MODE - No logins will be created'
    PRINT 'Set @DryRun = 0 to actually create the logins'
    PRINT ''
    PRINT 'Preview of CREATE LOGIN commands:'
    PRINT ''
END
ELSE
BEGIN
    PRINT '⚠️  EXECUTING LOGIN CREATION - Server logins will be created!'
    PRINT ''
END

-- Generate and execute login creation commands
DECLARE @UserName NVARCHAR(128)
DECLARE @UserType CHAR(1)
DECLARE @CreateLoginSQL NVARCHAR(500)
DECLARE @SuccessCount INT = 0
DECLARE @SkipCount INT = 0
DECLARE @ErrorCount INT = 0

-- Cursor for processing each orphaned user
DECLARE login_cursor CURSOR FOR
EXEC('
SELECT DISTINCT dp.name, dp.type
FROM [' + @SourceDatabaseName + '].sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.name = sp.name COLLATE SQL_Latin1_General_CP1_CI_AS
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.name IS NULL
  AND (' + CAST(@CreateSQLLogins AS VARCHAR) + ' = 1 AND dp.type = ''S'' OR ' + CAST(@CreateWindowsLogins AS VARCHAR) + ' = 1 AND dp.type = ''U'')
ORDER BY dp.type, dp.name
')

OPEN login_cursor
FETCH NEXT FROM login_cursor INTO @UserName, @UserType

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Check if we should process this specific user
    IF NOT EXISTS (SELECT 1 FROM @SpecificUsers) OR EXISTS (SELECT 1 FROM @SpecificUsers WHERE UserName = @UserName)
    BEGIN
        -- Check if login already exists
        IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @UserName COLLATE SQL_Latin1_General_CP1_CI_AS)
        BEGIN
            SET @SkipCount = @SkipCount + 1
            IF @DryRun = 1
                PRINT '-- SKIP: Login [' + @UserName + '] already exists'
            ELSE
                PRINT '⚠️  SKIPPED: Login [' + @UserName + '] already exists'
        END
        ELSE
        BEGIN
            -- Generate appropriate CREATE LOGIN command
            IF @UserType = 'S'  -- SQL Login
            BEGIN
                SET @CreateLoginSQL = 'CREATE LOGIN [' + @UserName + '] WITH PASSWORD = ''' + @DefaultPassword + ''''
                
                IF @ForcePasswordPolicy = 0
                    SET @CreateLoginSQL = @CreateLoginSQL + ', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF'
                    
                SET @CreateLoginSQL = @CreateLoginSQL + ';'
            END
            ELSE IF @UserType = 'U'  -- Windows Login
            BEGIN
                SET @CreateLoginSQL = 'CREATE LOGIN [' + @UserName + '] FROM WINDOWS;'
            END
            ELSE
            BEGIN
                PRINT '-- ERROR: Unknown user type ''' + @UserType + ''' for user: ' + @UserName
                SET @ErrorCount = @ErrorCount + 1
                GOTO NextUser
            END
            
            -- Execute or preview the command
            IF @DryRun = 1
            BEGIN
                PRINT @CreateLoginSQL
            END
            ELSE
            BEGIN
                BEGIN TRY
                    EXEC sp_executesql @CreateLoginSQL
                    SET @SuccessCount = @SuccessCount + 1
                    PRINT '✅ Created login: ' + @UserName + 
                          CASE WHEN @UserType = 'S' THEN ' (SQL Login with temp password)' 
                               WHEN @UserType = 'U' THEN ' (Windows Login)'
                               ELSE '' END
                END TRY
                BEGIN CATCH
                    SET @ErrorCount = @ErrorCount + 1
                    PRINT '❌ Failed to create login [' + @UserName + ']: ' + ERROR_MESSAGE()
                END CATCH
            END
        END
    END
    
    NextUser:
    FETCH NEXT FROM login_cursor INTO @UserName, @UserType
END

CLOSE login_cursor
DEALLOCATE login_cursor

-- Summary
PRINT ''
PRINT '--- SUMMARY ---'
IF @DryRun = 1
BEGIN
    PRINT 'Logins that would be created: ' + CAST(@LoginCount - @SkipCount AS VARCHAR)
    PRINT 'Logins that would be skipped: ' + CAST(@SkipCount AS VARCHAR)
    PRINT ''
    PRINT 'To execute: Set @DryRun = 0 and run again'
END
ELSE
BEGIN
    PRINT 'Logins successfully created: ' + CAST(@SuccessCount AS VARCHAR)
    PRINT 'Logins skipped (already exist): ' + CAST(@SkipCount AS VARCHAR)
    PRINT 'Errors encountered: ' + CAST(@ErrorCount AS VARCHAR)
    PRINT ''
    
    IF @ErrorCount = 0 AND @SuccessCount > 0
    BEGIN
        PRINT '✅ LOGIN CREATION COMPLETED SUCCESSFULLY!'
        PRINT ''
        PRINT '🔐 IMPORTANT SECURITY NOTES:'
        PRINT '• Change temporary passwords immediately!'
        PRINT '• Review login permissions and roles'
        PRINT '• Test BACPAC import - orphaned user errors should be resolved'
    END
    ELSE IF @SuccessCount = 0
    BEGIN
        PRINT '⚠️  No new logins were created'
    END
END

IF @CreateSQLLogins = 1 AND @DryRun = 0 AND @SuccessCount > 0
BEGIN
    PRINT ''
    PRINT '🔑 SQL LOGIN PASSWORD CHANGES:'
    PRINT 'Use these commands to set proper passwords:'
    PRINT ''
    
    -- Generate password change commands for created SQL logins
    SET @SQL = N'
    SELECT ''ALTER LOGIN ['' + dp.name + ''] WITH PASSWORD = ''''YourSecurePassword'''';''
    FROM [' + @SourceDatabaseName + N'].sys.database_principals dp
    JOIN sys.server_principals sp ON dp.name = sp.name COLLATE SQL_Latin1_General_CP1_CI_AS
    WHERE dp.type = ''S''
      AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
      AND dp.principal_id > 4
      AND sp.type = ''S''
    ORDER BY dp.name
    '
    
    EXEC sp_executesql @SQL
END

PRINT ''
PRINT REPLICATE('=', 70)
PRINT 'LOGIN CREATION SCRIPT COMPLETE'
PRINT REPLICATE('=', 70)