-- ========================================
-- CREATE DUMMY LOGINS - TARGET SERVER
-- ========================================
-- Purpose: Create dummy server logins for orphaned users to prevent BACPAC import errors
-- Usage: Run on target server (master database) BEFORE BACPAC import
-- Input: List of user names and types from source orphaned user detection
-- ========================================

USE master;

-- Parameters (these will be replaced programmatically)
DECLARE @UserName NVARCHAR(128) = '{USER_NAME}';
DECLARE @UserType CHAR(1) = '{USER_TYPE}';  -- 'S' for SQL, 'U' for Windows
DECLARE @DefaultPassword NVARCHAR(128) = 'TempPassword123!';

-- Validate input
IF NULLIF(@UserName, '') IS NULL
BEGIN
    RAISERROR('Invalid user name parameter', 16, 1);
    RETURN;
END

IF @UserType NOT IN ('S', 'U')
BEGIN
    RAISERROR('Invalid user type. Must be S (SQL) or U (Windows)', 16, 1);
    RETURN;
END

-- Check if login already exists
IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @UserName)
BEGIN
    PRINT 'Login [' + @UserName + '] already exists - skipping creation';
    RETURN;
END

-- Create the login based on type
BEGIN TRY
    IF @UserType = 'S'
    BEGIN
        -- Create SQL Server login with dummy password
        DECLARE @CreateLoginSQL NVARCHAR(500) =
            'CREATE LOGIN [' + @UserName + '] WITH PASSWORD = ''' + @DefaultPassword + ''', ' +
            'CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF';

        EXEC sp_executesql @CreateLoginSQL;
        PRINT 'Created SQL login: [' + @UserName + '] with temporary password';
    END
    ELSE IF @UserType = 'U'
    BEGIN
        -- Create Windows login (requires domain validation)
        DECLARE @CreateWindowsLoginSQL NVARCHAR(500) =
            'CREATE LOGIN [' + @UserName + '] FROM WINDOWS';

        EXEC sp_executesql @CreateWindowsLoginSQL;
        PRINT 'Created Windows login: [' + @UserName + ']';
    END

    PRINT 'Login creation successful for: [' + @UserName + ']';
END TRY
BEGIN CATCH
    PRINT 'ERROR creating login [' + @UserName + ']: ' + ERROR_MESSAGE();
    -- Don't throw error - continue with other logins
END CATCH