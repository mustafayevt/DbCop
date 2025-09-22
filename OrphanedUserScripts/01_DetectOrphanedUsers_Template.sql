-- ========================================
-- ORPHANED USER DETECTION SCRIPT - GENERIC TEMPLATE
-- ========================================
-- Purpose: Identifies database users without corresponding server logins
-- Usage: Replace placeholders with your actual values and run on SOURCE server
-- Author: DbCop Tool
-- Date: {DATE}
-- ========================================

-- CONFIGURATION SECTION - MODIFY THESE VALUES
-- ========================================
DECLARE @DatabaseName NVARCHAR(128) = '{DATABASE_NAME}'  -- Replace with your database name
DECLARE @ShowDetailsLevel INT = 2  -- 1=Summary only, 2=Detailed report, 3=Full diagnostic

-- You can also run this by changing database context instead:
-- USE [YourDatabaseName]

-- ========================================
-- DETECTION LOGIC - DO NOT MODIFY BELOW
-- ========================================

-- Header
PRINT REPLICATE('=', 60)
PRINT 'ORPHANED USER DETECTION REPORT'
PRINT 'Database: ' + ISNULL(@DatabaseName, DB_NAME())
PRINT 'Server: ' + @@SERVERNAME
PRINT 'Instance: ' + @@SERVICENAME
PRINT 'Date: ' + CONVERT(VARCHAR, GETDATE(), 121)
PRINT 'Detail Level: ' + CAST(@ShowDetailsLevel AS VARCHAR)
PRINT REPLICATE('=', 60)
PRINT ''

-- Build dynamic SQL to handle cross-database queries if needed
DECLARE @SQL NVARCHAR(MAX)
DECLARE @DatabaseContext NVARCHAR(200) = ISNULL(@DatabaseName, DB_NAME())

-- Check if database exists
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @DatabaseContext)
BEGIN
    PRINT 'ERROR: Database [' + @DatabaseContext + '] does not exist on this server!'
    PRINT 'Available databases:'
    SELECT name AS AvailableDatabases FROM sys.databases WHERE database_id > 4 ORDER BY name
    RETURN
END

-- Main detection query
SET @SQL = N'
USE [' + @DatabaseContext + N']

-- Summary count
DECLARE @OrphanedCount INT
SELECT @OrphanedCount = COUNT(*)
FROM sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN (''S'', ''U'')  -- SQL and Windows users
  AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
  AND dp.principal_id > 4  -- Exclude built-in accounts
  AND dp.is_fixed_role = 0  -- Exclude fixed roles
  AND sp.sid IS NULL  -- No corresponding server login

PRINT ''SUMMARY: Found '' + CAST(@OrphanedCount AS VARCHAR) + '' orphaned user(s)''
PRINT ''''

IF @OrphanedCount = 0
BEGIN
    PRINT ''✅ No orphaned users found! Database is clean.''
    PRINT ''This database should import without orphaned user errors.''
END
ELSE
BEGIN
    PRINT ''⚠️  WARNING: '' + CAST(@OrphanedCount AS VARCHAR) + '' orphaned user(s) detected!''
    PRINT ''These users will cause import errors during BACPAC operations.''
    PRINT ''''
'

-- Add detailed reporting based on level
IF @ShowDetailsLevel >= 2
BEGIN
    SET @SQL = @SQL + N'
    -- Detailed orphaned user list
    PRINT ''--- ORPHANED USERS DETAILS ---''
    SELECT 
        ROW_NUMBER() OVER (ORDER BY dp.name) AS [#],
        dp.name AS [User Name],
        dp.type_desc AS [User Type],
        FORMAT(dp.create_date, ''yyyy-MM-dd HH:mm'') AS [Created],
        FORMAT(dp.modify_date, ''yyyy-MM-dd HH:mm'') AS [Last Modified],
        CASE 
            WHEN dp.default_schema_name IS NOT NULL 
            THEN dp.default_schema_name 
            ELSE ''(default)'' 
        END AS [Default Schema],
        ''ORPHANED'' AS [Status],
        ''Will cause BACPAC import errors'' AS [Impact]
    FROM sys.database_principals dp
    LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
    WHERE dp.type IN (''S'', ''U'')
      AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
      AND dp.principal_id > 4
      AND dp.is_fixed_role = 0
      AND sp.sid IS NULL
    ORDER BY dp.name
    
    PRINT ''''
    
    -- Role memberships for orphaned users
    PRINT ''--- ROLE MEMBERSHIPS (Will need cleanup) ---''
    SELECT DISTINCT
        dp_member.name AS [Orphaned User],
        STRING_AGG(dp_role.name, '', '') WITHIN GROUP (ORDER BY dp_role.name) AS [Member Of Roles]
    FROM sys.database_role_members rm
    JOIN sys.database_principals dp_role ON rm.role_principal_id = dp_role.principal_id
    JOIN sys.database_principals dp_member ON rm.member_principal_id = dp_member.principal_id
    LEFT JOIN sys.server_principals sp ON dp_member.sid = sp.sid
    WHERE dp_member.type IN (''S'', ''U'')
      AND dp_member.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
      AND sp.sid IS NULL
    GROUP BY dp_member.name
    ORDER BY dp_member.name
    
    PRINT ''''
'
END

-- Add full diagnostic info
IF @ShowDetailsLevel >= 3
BEGIN
    SET @SQL = @SQL + N'
    -- Full diagnostic information
    PRINT ''--- FULL DIAGNOSTIC INFO ---''
    SELECT 
        dp.name AS [User Name],
        dp.type_desc AS [Type],
        dp.principal_id AS [Principal ID],
        CONVERT(VARCHAR(100), dp.sid, 1) AS [User SID],
        dp.authentication_type_desc AS [Auth Type],
        dp.default_language_name AS [Language],
        dp.is_fixed_role AS [Is Fixed Role],
        CASE WHEN sp.name IS NOT NULL THEN ''Has Login'' ELSE ''ORPHANED'' END AS [Login Status]
    FROM sys.database_principals dp
    LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
    WHERE dp.type IN (''S'', ''U'')
      AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
      AND dp.principal_id > 4
      AND dp.is_fixed_role = 0
    ORDER BY CASE WHEN sp.name IS NULL THEN 0 ELSE 1 END, dp.name
    
    PRINT ''''
'
END

-- Add recommendations
SET @SQL = @SQL + N'
    -- Recommendations
    PRINT ''--- RECOMMENDATIONS ---''
    IF @OrphanedCount > 0
    BEGIN
        PRINT ''🔧 OPTION 1: Clean up orphaned users after BACPAC import''
        PRINT ''   - Import BACPAC (will fail with orphaned user errors)''
        PRINT ''   - Run cleanup script on target database to remove orphaned users''
        PRINT ''   - Recommended for dev/test environments''
        PRINT ''''
        PRINT ''🔧 OPTION 2: Create missing logins on target server''
        PRINT ''   - Create server logins on target for: '' + (
            SELECT STRING_AGG(dp.name, '', '') 
            FROM sys.database_principals dp
            LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
            WHERE dp.type IN (''S'', ''U'')
              AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
              AND dp.principal_id > 4
              AND dp.is_fixed_role = 0
              AND sp.sid IS NULL
        )
        PRINT ''   - Then retry BACPAC import''
        PRINT ''   - Recommended for production environments''
        PRINT ''''
        PRINT ''🔧 OPTION 3: Use DACPAC (schema-only) instead of BACPAC''
        PRINT ''   - Avoids user-related issues entirely''
        PRINT ''   - Only transfers schema, not data''
        PRINT ''   - Use when you only need structure changes''
    END
    ELSE
    BEGIN
        PRINT ''✅ No action needed - database is ready for BACPAC operations''
    END
    
    PRINT ''''
    PRINT ''📋 NEXT STEPS:''
    PRINT ''1. Copy the user names listed above''
    PRINT ''2. Choose your preferred cleanup approach''
    PRINT ''3. Run the appropriate cleanup script on target server''
    PRINT ''4. Retry BACPAC import operation''
'

-- Execute the dynamic SQL
EXEC sp_executesql @SQL

PRINT ''
PRINT REPLICATE('=', 60)
PRINT 'DETECTION COMPLETE'
PRINT REPLICATE('=', 60)

-- Generate cleanup script template
PRINT ''
PRINT '-- QUICK CLEANUP SCRIPT TEMPLATE --'
PRINT '-- Copy and customize for your target database --'
PRINT ''

EXEC('
SELECT 
    ''-- Remove orphaned user: '' + dp.name AS [Cleanup Commands]
FROM [' + @DatabaseContext + '].sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.sid IS NULL

UNION ALL

SELECT ''USE [YourTargetDatabase]''
UNION ALL
SELECT ''-- Drop orphaned users:''
UNION ALL

SELECT 
    ''DROP USER ['' + dp.name + '']''
FROM [' + @DatabaseContext + '].sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN (''S'', ''U'')
  AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'', ''public'')
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.sid IS NULL
ORDER BY [Cleanup Commands]
')