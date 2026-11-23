<#
.SYNOPSIS
    استخراج سكيما قاعدة بيانات YKCoatings الحية إلى ملفات داخل المستودع.

.DESCRIPTION
    يولّد هذا السكربت ملفين:
      schema.sql      — تعريفات كل الجداول (أعمدة، مفاتيح أساسية وأجنبية، فهارس، قيم افتراضية)
      procedures.sql  — النص الكامل لكل الإجراءات المخزّنة والدوال والمناظر والمشغّلات
                        كما هي حرفيًا في sys.sql_modules على القاعدة الحية.

    الهدف: إغلاق فجوة أن منطق الترحيل الحاكم (sp_PostReceiptVoucher وغيرها) كان يعيش
    خارج المستودع. بعد التشغيل راجع الملفين ثم اعتمدهما في Git.

    لا يقرأ السكربت أي بيانات أعمال؛ تعريفات فقط. لا يُضمَّن أي كلمة مرور في المخرجات.

.EXAMPLE
    # مصادقة ويندوز (الأسهل من جهاز متصل بالقاعدة):
    powershell -ExecutionPolicy Bypass -File .\database\extract-schema.ps1 -Server "SERVER\INSTANCE" -Database "YKCoatings"

.EXAMPLE
    # مصادقة SQL: ستُطلب كلمة المرور في نافذة آمنة ولا تُكتب في أي ملف:
    powershell -ExecutionPolicy Bypass -File .\database\extract-schema.ps1 -Server "host,1433" -Database "YKCoatings" -SqlUser "sa"

.NOTES
    متطلبات: Windows PowerShell 5.1 أو أحدث، وصلاحية VIEW DEFINITION على القاعدة.
    الكائنات المشفّرة (WITH ENCRYPTION) لا يمكن استخراج نصها؛ تُدرج أسماؤها في تقرير نهاية التشغيل.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Database,
    [string]$SqlUser,
    [string]$OutputDir = (Join-Path $PSScriptRoot ".")
)

$ErrorActionPreference = "Stop"

if ($SqlUser) {
    $secure = Read-Host "كلمة مرور SQL للمستخدم $SqlUser" -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = $Database
    $builder["User ID"] = $SqlUser
    $builder["Password"] = $plain
    $builder["Encrypt"] = $false
} else {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = $Database
    $builder["Integrated Security"] = $true
}

$connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$connection.Open()
try {
    function Invoke-Query([string]$Sql) {
        $cmd = $connection.CreateCommand()
        $cmd.CommandText = $Sql
        $cmd.CommandTimeout = 300
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
        $table = New-Object System.Data.DataTable
        [void]$adapter.Fill($table)
        return $table
    }

    $utf8 = New-Object System.Text.UTF8Encoding $true
    $header = "-- YKCoatings — مستخرج آليًا من القاعدة الحية بتاريخ $(Get-Date -Format 'yyyy-MM-dd HH:mm')`r`n" +
              "-- المصدر: الخادم $Server / القاعدة $Database`r`n" +
              "-- أعد توليده بتشغيل database/extract-schema.ps1 بعد أي تغيير في القاعدة.`r`n`r`n"

    # ---------------------------------------------------------------
    # 1) الكائنات القابلة للبرمجة: النص الحرفي من sys.sql_modules
    # ---------------------------------------------------------------
    $modules = Invoke-Query @"
SELECT s.name AS SchemaName, o.name AS ObjectName, o.type_desc AS ObjectType, m.definition AS Definition
FROM sys.sql_modules m
INNER JOIN sys.objects o ON o.object_id = m.object_id
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.is_ms_shipped = 0
ORDER BY o.type, s.name, o.name
"@
    $encrypted = Invoke-Query @"
SELECT s.name + '.' + o.name AS ObjectName
FROM sys.objects o
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('P','FN','IF','TF','V','TR') AND m.definition IS NULL
ORDER BY 1
"@

    $procBuilder = New-Object System.Text.StringBuilder
    [void]$procBuilder.Append($header)
    foreach ($row in $modules.Rows) {
        [void]$procBuilder.AppendLine("-- ============================================================")
        [void]$procBuilder.AppendLine("-- $($row.ObjectType): $($row.SchemaName).$($row.ObjectName)")
        [void]$procBuilder.AppendLine("-- ============================================================")
        [void]$procBuilder.AppendLine([string]$row.Definition)
        [void]$procBuilder.AppendLine("GO")
        [void]$procBuilder.AppendLine("")
    }
    [System.IO.File]::WriteAllText((Join-Path $OutputDir "procedures.sql"), $procBuilder.ToString(), $utf8)
    Write-Host "procedures.sql: $($modules.Rows.Count) كائنًا قابلًا للبرمجة."

    # ---------------------------------------------------------------
    # 2) الجداول: توليد CREATE TABLE من كتالوج النظام
    # ---------------------------------------------------------------
    $columns = Invoke-Query @"
SELECT t.object_id AS TableId, s.name AS SchemaName, t.name AS TableName,
       c.column_id AS Ordinal, c.name AS ColumnName,
       ty.name AS TypeName, c.max_length AS MaxLength, c.precision AS [Precision], c.scale AS Scale,
       c.is_nullable AS IsNullable, c.is_identity AS IsIdentity, c.is_computed AS IsComputed,
       cc.definition AS ComputedDefinition,
       dc.definition AS DefaultDefinition, dc.name AS DefaultName,
       ic.seed_value AS SeedValue, ic.increment_value AS IncrementValue
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
INNER JOIN sys.columns c ON c.object_id = t.object_id
INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name, c.column_id
"@

    $primaryKeys = Invoke-Query @"
SELECT tc.object_id AS TableId, kc.name AS ConstraintName,
       STRING_AGG(CONVERT(NVARCHAR(MAX), c.name), ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnList
FROM sys.key_constraints kc
INNER JOIN sys.tables tc ON tc.object_id = kc.parent_object_id
INNER JOIN sys.index_columns ic ON ic.object_id = tc.object_id AND ic.index_id = kc.unique_index_id
INNER JOIN sys.columns c ON c.object_id = tc.object_id AND c.column_id = ic.column_id
WHERE kc.type = 'PK'
GROUP BY tc.object_id, kc.name
"@

    $foreignKeys = Invoke-Query @"
SELECT fk.name AS ConstraintName,
       ps.name + '.' + pt.name AS ParentTable, pc.name AS ParentColumn,
       rs.name + '.' + rt.name AS RefTable, rc.name AS RefColumn,
       fk.delete_referential_action_desc AS OnDelete, fk.update_referential_action_desc AS OnUpdate
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
INNER JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
INNER JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
INNER JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY fk.name, fkc.constraint_column_id
"@

    $indexes = Invoke-Query @"
SELECT s.name + '.' + t.name AS TableFullName, i.name AS IndexName, i.is_unique AS IsUnique,
       STRING_AGG(CONVERT(NVARCHAR(MAX), c.name), ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnList
FROM sys.indexes i
INNER JOIN sys.tables t ON t.object_id = i.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
INNER JOIN sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
INNER JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = ic.column_id
WHERE i.name IS NOT NULL AND i.is_primary_key = 0 AND i.type > 0
GROUP BY s.name, t.name, i.name, i.is_unique
ORDER BY 1, 2
"@

    $pkByTable = @{}
    foreach ($pk in $primaryKeys.Rows) { $pkByTable[[int]$pk.TableId] = $pk }

    $schemaBuilder = New-Object System.Text.StringBuilder
    [void]$schemaBuilder.Append($header)

    $currentTable = $null
    foreach ($row in $columns.Rows) {
        $tableKey = "$($row.SchemaName).$($row.TableName)"
        if ($tableKey -ne $currentTable) {
            if ($null -ne $currentTable) { [void]$schemaBuilder.AppendLine(");"); [void]$schemaBuilder.AppendLine("GO"); [void]$schemaBuilder.AppendLine("") }
            $currentTable = $tableKey
            $currentTableId = [int]$row.TableId
            [void]$schemaBuilder.AppendLine("CREATE TABLE [$($row.SchemaName)].[$($row.TableName)] (")
            $firstColumn = $true
        }
        if (-not $firstColumn) { [void]$schemaBuilder.AppendLine(",") }
        $firstColumn = $false

        if ($row.IsComputed) {
            [void]$schemaBuilder.Append("    [$($row.ColumnName)] AS $($row.ComputedDefinition)")
            continue
        }
        $typeSql = switch ($row.TypeName) {
            { $_ -in 'nvarchar','nchar' } { "[$($row.TypeName)]($([int]$row.MaxLength / 2))" }
            { $_ -in 'varchar','char','varbinary','binary' } {
                if ([int]$row.MaxLength -eq -1) { "[$($row.TypeName)](MAX)" } else { "[$($row.TypeName)]($($row.MaxLength))" } }
            { $_ -in 'decimal','numeric' } { "[$($row.TypeName)]($($row.Precision),$($row.Scale))" }
            'datetime2' { "[$($row.TypeName)]($($row.Scale))" }
            default { "[$($row.TypeName)]" }
        }
        if ([int]$row.MaxLength -eq -1 -and $row.TypeName -eq 'nvarchar') { $typeSql = "[nvarchar](MAX)" }
        $line = "    [$($row.ColumnName)] $typeSql"
        if ($row.IsIdentity) { $line += " IDENTITY($($row.SeedValue),$($row.IncrementValue))" }
        $line += $(if ($row.IsNullable) { " NULL" } else { " NOT NULL" })
        if ($row.DefaultDefinition -ne [DBNull]::Value -and $row.DefaultDefinition) {
            $line += " CONSTRAINT [$($row.DefaultName)] DEFAULT $($row.DefaultDefinition)"
        }
        [void]$schemaBuilder.Append($line)
    }
    if ($null -ne $currentTable) {
        # إغلاق آخر جدول؛ المفاتيح الأساسية والأجنبية والفهارس تُلحق بعد كل الجداول لضمان ترتيب إنشاء صحيح
        [void]$schemaBuilder.AppendLine("")
        [void]$schemaBuilder.AppendLine(");")
        [void]$schemaBuilder.AppendLine("GO")
        [void]$schemaBuilder.AppendLine("")
    }

    foreach ($pk in $primaryKeys.Rows) {
        $tableRow = $columns.Rows | Where-Object { [int]$_.TableId -eq [int]$pk.TableId } | Select-Object -First 1
        if ($tableRow) {
            [void]$schemaBuilder.AppendLine("ALTER TABLE [$($tableRow.SchemaName)].[$($tableRow.TableName)] ADD CONSTRAINT [$($pk.ConstraintName)] PRIMARY KEY ($($pk.ColumnList));")
        }
    }
    [void]$schemaBuilder.AppendLine("GO")
    [void]$schemaBuilder.AppendLine("")

    $fkGrouped = $foreignKeys.Rows | Group-Object ConstraintName
    foreach ($group in $fkGrouped) {
        $first = $group.Group[0]
        $cols = ($group.Group | ForEach-Object { "[$($_.ParentColumn)]" }) -join ", "
        $refCols = ($group.Group | ForEach-Object { "[$($_.RefColumn)]" }) -join ", "
        $parentParts = $first.ParentTable -split '\.'
        $refParts = $first.RefTable -split '\.'
        $line = "ALTER TABLE [$($parentParts[0])].[$($parentParts[1])] ADD CONSTRAINT [$($group.Name)] FOREIGN KEY ($cols) REFERENCES [$($refParts[0])].[$($refParts[1])] ($refCols)"
        if ($first.OnDelete -ne 'NO_ACTION') { $line += " ON DELETE $($first.OnDelete -replace '_',' ')" }
        if ($first.OnUpdate -ne 'NO_ACTION') { $line += " ON UPDATE $($first.OnUpdate -replace '_',' ')" }
        [void]$schemaBuilder.AppendLine($line + ";")
    }
    [void]$schemaBuilder.AppendLine("GO")
    [void]$schemaBuilder.AppendLine("")

    foreach ($ix in $indexes.Rows) {
        $parts = $ix.TableFullName -split '\.'
        $unique = if ($ix.IsUnique) { "UNIQUE " } else { "" }
        [void]$schemaBuilder.AppendLine("CREATE ${unique}INDEX [$($ix.IndexName)] ON [$($parts[0])].[$($parts[1])] ($($ix.ColumnList));")
    }
    [void]$schemaBuilder.AppendLine("GO")

    [System.IO.File]::WriteAllText((Join-Path $OutputDir "schema.sql"), $schemaBuilder.ToString(), $utf8)
    $tableCount = ($columns.Rows | Select-Object -ExpandProperty TableId -Unique).Count
    Write-Host "schema.sql: $tableCount جدولًا."

    if ($encrypted.Rows.Count -gt 0) {
        Write-Warning "الكائنات التالية مشفّرة أو بلا نص متاح ولن تظهر في procedures.sql:"
        foreach ($row in $encrypted.Rows) { Write-Warning "  - $($row.ObjectName)" }
    }
    Write-Host "تم. راجع الملفين ثم اعتمدهما: git add database/schema.sql database/procedures.sql"
}
finally {
    $connection.Close()
}
