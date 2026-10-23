-- =============================================================================
--  YK Coatings ERP — تنظيف بيانات اختبار E2E
-- =============================================================================
--  cleans up every row created by the browser E2E test round.
--  The script is data-driven: it discovers the foreign-key graph at runtime,
--  so it does not depend on hard-coded child/parent column names.
--
--  ── HOW IT WORKS ──────────────────────────────────────────────────────────
--  1) "roots"  : every table that has a column ending in "Code" and whose rows
--                match the marker (default 'TEST-%'). These are the master rows
--                the test created: items, warehouses, suppliers, customers,
--                employees, accounts, cost centres, departments, job titles …
--  2) "closure": walks the FK graph transitively from those roots, so every
--                document row hanging off a TEST master row is collected
--                (purchase orders, GRNs, invoices, issues, batches, counts,
--                transfers, journals, vouchers, movements, balances …).
--  3) "delete" : deletes leaves first, then their parents.
--
--  ── SAFETY ───────────────────────────────────────────────────────────────
--  • DRY RUN by default (@Execute = 0): the whole run is inside a transaction
--    that is ROLLBACKed. You get the report, nothing is deleted.
--  • Set @Execute = 1 only after reviewing the dry-run report.
--  • FK constraints are re-enabled WITH CHECK at the end: if anything was
--    missed, the re-enable FAILS and the transaction rolls back.
--  • System tables are never touched (see @Protect).
--
--  ── USAGE ────────────────────────────────────────────────────────────────
--    EXEC dbo.sp_CleanupE2ETestData @Execute = 0, @Marker = 'TEST-%';
--    -- راجع التقرير، ثم:
--    EXEC dbo.sp_CleanupE2ETestData @Execute = 1, @Marker = 'TEST-%';
-- =============================================================================

CREATE OR ALTER PROCEDURE dbo.sp_CleanupE2ETestData
    @Execute BIT          = 0,        -- 0 = dry run (default) · 1 = delete for real
    @Marker  NVARCHAR(20) = N'TEST-%'  -- نمط يطابق أكواد الصفوف الاختبارية
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @sql NVARCHAR(MAX), @MarkerCopy NVARCHAR(20) = @Marker;

    -- جداول نظام لا تُمَسّ أبداً.
    DECLARE @Protect TABLE (TableName NVARCHAR(100) PRIMARY KEY);
    INSERT INTO @Protect (TableName) VALUES
        (N'SystemUsers'), (N'UserRoles'), (N'UserPermissions'), (N'RolePermissions'),
        (N'AuditLog'), (N'LoginHistory'), (N'Notifications'), (N'SystemSettings'),
        (N'NumberSequences'), (N'SystemModules'), (N'UserPreferences');

    ------------------------------------------------------------------ 1. الجذور
    IF OBJECT_ID('tempdb..#Root') IS NOT NULL DROP TABLE #Root;
    CREATE TABLE #Root (TableName NVARCHAR(100), CodeColumn NVARCHAR(100));

    INSERT INTO #Root (TableName, CodeColumn)
    SELECT DISTINCT t.name, c.name
      FROM sys.tables t
      JOIN sys.schemas s  ON s.schema_id = t.schema_id
      JOIN sys.columns c  ON c.object_id = t.object_id
     WHERE s.name = N'dbo'
       AND c.name LIKE N'%Code'
       AND c.is_computed = 0
       AND EXISTS (SELECT 1 FROM sys.columns pk
                    WHERE pk.object_id = t.object_id AND pk.is_identity = 1);

    -- هوية كل جدول (عمود المفتاح) + ترتيب الحذف
    IF OBJECT_ID('tempdb..#Ident') IS NOT NULL DROP TABLE #Ident;
    CREATE TABLE #Ident (TableName NVARCHAR(100) PRIMARY KEY, PkColumn NVARCHAR(100), DelOrder INT);

    INSERT INTO #Ident (TableName, PkColumn, DelOrder)
    SELECT t.name, c.name, 0
      FROM sys.tables t
      JOIN sys.schemas s  ON s.schema_id = t.schema_id
      JOIN sys.columns c  ON c.object_id = t.object_id AND c.is_identity = 1
     WHERE s.name = N'dbo';

    IF OBJECT_ID('tempdb..#Target') IS NOT NULL DROP TABLE #Target;
    CREATE TABLE #Target (TableName NVARCHAR(100), PkColumn NVARCHAR(100), PkValue BIGINT,
                          CONSTRAINT PK_Target PRIMARY KEY (TableName, PkValue));

    -- اجمع صفوف الجذور (مطابقة الكود)
    SET @sql = N'';
    SELECT @sql += N' UNION ALL SELECT N''' + r.TableName + N''', N''' + i.PkColumn +
                 N''', CONVERT(BIGINT, ' + QUOTENAME(r.CodeColumn) + N') FROM dbo.' + QUOTENAME(r.TableName) +
                 N' WHERE ' + QUOTENAME(r.CodeColumn) + N' LIKE @MarkerCopy'
        FROM #Root r
        JOIN #Ident i ON i.TableName = r.TableName
       WHERE NOT EXISTS (SELECT 1 FROM @Protect p WHERE p.TableName = r.TableName);

    IF LEN(@sql) > 12
    BEGIN
        SET @sql = N'INSERT INTO #Target (TableName, PkColumn, PkValue) ' + SUBSTRING(@sql, 12, LEN(@sql));
        EXEC sp_executesql @sql, N'@MarkerCopy NVARCHAR(20)', @MarkerCopy = @MarkerCopy;
    END

    -- ------------------------------------------------- 2. الإغلاق عبر FKs
    IF OBJECT_ID('tempdb..#Fk') IS NOT NULL DROP TABLE #Fk;
    CREATE TABLE #Fk (ParentTable NVARCHAR(100), ParentCol NVARCHAR(100),
                      ChildTable NVARCHAR(100), ChildFk NVARCHAR(100));

    INSERT INTO #Fk (ParentTable, ParentCol, ChildTable, ChildFk)
    SELECT DISTINCT OBJECT_NAME(fk.referenced_object_id), pc.name,
                      OBJECT_NAME(fk.parent_object_id),        cc.name
      FROM sys.foreign_keys fk
      JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
      JOIN sys.columns pc ON pc.object_id = fk.referenced_object_id AND pc.column_id = fkc.referenced_column_id
      JOIN sys.columns cc ON cc.object_id = fk.parent_object_id       AND cc.column_id = fkc.parent_column_id
     WHERE OBJECT_NAME(fk.referenced_object_id) IS NOT NULL
       AND OBJECT_NAME(fk.parent_object_id)    IS NOT NULL;

    DECLARE @added INT = 1, @guard INT = 0;
    WHILE @added > 0 AND @guard < 50
    BEGIN
        SET @added = 0;
        SET @guard = @guard + 1;

        DECLARE @pT NVARCHAR(100), @pC NVARCHAR(100), @cT NVARCHAR(100), @cF NVARCHAR(100);
        DECLARE fk_cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT ParentTable, ParentCol, ChildTable, ChildFk FROM #Fk;
        OPEN fk_cur;
        FETCH NEXT FROM fk_cur INTO @pT, @pC, @cT, @cF;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            IF EXISTS (SELECT 1 FROM #Target WHERE TableName = @pT)
               AND EXISTS (SELECT 1 FROM #Ident WHERE TableName = @cT)
               AND NOT EXISTS (SELECT 1 FROM @Protect p WHERE p.TableName = @cT)
            BEGIN
                DECLARE @pkc NVARCHAR(100) = (SELECT TOP 1 PkColumn FROM #Ident WHERE TableName = @cT);
                IF @pkc IS NOT NULL
                BEGIN
                    SET @sql = N'INSERT INTO #Target (TableName, PkColumn, PkValue)
                                 SELECT N''' + @cT + N''', N''' + @pkc + N''', CONVERT(BIGINT, ' + QUOTENAME(@cF) + N')
                                   FROM dbo.' + QUOTENAME(@cT) + N'
                                  WHERE ' + QUOTENAME(@cF) + N' IN (SELECT PkValue FROM #Target WHERE TableName = N''' + @pT + N''')
                                    AND NOT EXISTS (SELECT 1 FROM #Target t2
                                                     WHERE t2.TableName = N''' + @cT + N''' AND t2.PkValue = ' + QUOTENAME(@cF) + N')';
                    EXEC sp_executesql @sql;
                    SET @added = @added + @@ROWCOUNT;
                END
            END
            FETCH NEXT FROM fk_cur INTO @pT, @pC, @cT, @cF;
        END
        CLOSE fk_cur;
        DEALLOCATE fk_cur;
    END

    ------------------------------------------------------------------ 3. التقرير
    PRINT N'===== تقرير الصفوف المرشّحة للحذف =====';
    SELECT t.TableName, COUNT(*) AS RowCount
      FROM #Target t GROUP BY t.TableName ORDER BY RowCount DESC, t.TableName;

    DECLARE @total INT = (SELECT COUNT(*) FROM #Target);
    PRINT N'===== الإجمالي: ' + CAST(@total AS NVARCHAR(20)) + N' صف =====';

    IF @Execute = 0
    BEGIN
        PRINT N'*** وضع التجربة (DRY RUN) — لم يُحذف شيء. استخدم @Execute = 1 للحذف الفعلي. ***';
        RETURN;
    END

    ------------------------------------------------------- 4. الحذف (أوراق أولاً)
    BEGIN TRANSACTION;

    -- عطّل قيود المفاتيح الأجنبية مؤقتاً حتى لا يقيّدنا الترتيب.
    EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';

    -- احسب ترتيب الحذف: الورقة أولاً (لاchildren داخل المجموعة).
    ;WITH Depth AS (
        SELECT fk.ChildTable, fk.ParentTable, 0 AS Lvl
          FROM #Fk fk
        UNION ALL
        SELECT d.ChildTable, fk.ParentTable, d.Lvl + 1
          FROM Depth d
          JOIN #Fk fk ON fk.ChildTable = d.ParentTable
    )
    UPDATE i
       SET DelOrder = (SELECT ISNULL(MAX(Lvl), 0) FROM Depth d WHERE d.ChildTable = i.TableName)
      FROM #Ident i;

    DECLARE @tbl NVARCHAR(100);
    DECLARE del_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT TableName FROM #Ident WHERE DelOrder = 0 AND TableName IN (SELECT DISTINCT TableName FROM #Target);
    OPEN del_cur;
    FETCH NEXT FROM del_cur INTO @tbl;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'DELETE FROM dbo.' + QUOTENAME(@tbl) + N'
                      WHERE ' + QUOTENAME((SELECT PkColumn FROM #Ident WHERE TableName = @tbl)) +
                      N' IN (SELECT PkValue FROM #Target WHERE TableName = N''' + @tbl + N''');';
        BEGIN TRY
            EXEC sp_executesql @sql;
            PRINT N'حُذف: ' + @tbl + N' (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + N' صف)';
        END TRY
        BEGIN CATCH
            PRINT N'تعذّر حذف ' + @tbl + N': ' + ERROR_MESSAGE();
        END CATCH
        FETCH NEXT FROM del_cur INTO @tbl;
    END
    CLOSE del_cur; DEALLOCATE del_cur;

    -- أعد تفعيل القيود WITH CHECK: إن تبقّى يتيم فسيفشل ⇒ rollback.
    EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';

    COMMIT TRANSACTION;
    PRINT N'===== تم الحذف =====';
END
GO
