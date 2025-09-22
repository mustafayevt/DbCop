-- ========================================
-- DETECT ORPHANED USERS - SOURCE DATABASE
-- ========================================
-- Purpose: Detect orphaned users on source database and return as result set
-- Usage: Run on source database connection
-- Returns: List of orphaned users that need logins created on target
-- ========================================

-- Check for orphaned users (users without corresponding server logins)
SELECT
    dp.name AS [UserName],
    dp.type AS [UserType],
    dp.type_desc AS [UserTypeDescription],
    CASE
        WHEN dp.type = 'S' THEN 'SQL'
        WHEN dp.type = 'U' THEN 'Windows'
        ELSE 'Other'
    END AS [LoginType],
    dp.default_schema_name AS [DefaultSchema]
FROM sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN ('S', 'U')  -- SQL and Windows users only
  AND dp.name NOT IN ('dbo', 'guest', 'INFORMATION_SCHEMA', 'sys', 'public')
  AND dp.principal_id > 4  -- Exclude built-in accounts
  AND dp.is_fixed_role = 0  -- Exclude fixed roles
  AND sp.sid IS NULL  -- No corresponding server login (orphaned)
ORDER BY dp.name;

-- Also return summary count
SELECT COUNT(*) AS OrphanedUserCount
FROM sys.database_principals dp
LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE dp.type IN ('S', 'U')
  AND dp.name NOT IN ('dbo', 'guest', 'INFORMATION_SCHEMA', 'sys', 'public')
  AND dp.principal_id > 4
  AND dp.is_fixed_role = 0
  AND sp.sid IS NULL;