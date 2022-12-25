using System.Data.SqlClient;
using Dapper;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;

public class SqlInfo
{
    List<DataTypes> dataTypes = new List<DataTypes>();

    string ConnectionString;

    private string databaseName = String.Empty;

    public record DataTypes(string sql, string dotnet, string cs);
    
    public record TableAndViewNames(string DatabaseName, string TableName, string Type);

    public record TableColumns(string ColumnName,
                               string Type,
                               string DDLType,
                               string CSType,
                               string NETType,
                               int MaxLength,
                               int Precision,
                               int Scale, 
                               string Nullable,
                               string PrimaryKey,
                               string Identity);


    public SqlInfo(string connectionString, string databaseName)
    {
        this.databaseName = databaseName;

        PopulateDataTypes();
        this.ConnectionString = connectionString;
        using (IDbConnection db = new SqlConnection(ConnectionString))
        {
            try
            {
                db.Open();
            }
            catch 
            {
                throw new ArgumentException($"Database '{db.Database}'. does not exist or couldn't be opened.");
            }
            finally
            {
                db.Close();
            }
        }
    }

    public List<TableAndViewNames> GetTableAndViewNames()
    {
        string sql =
            @"SELECT
              '{DatabaseName}' as DatabaseName, TABLE_NAME as TableName, IIF(TABLE_TYPE = 'BASE TABLE', 'table', 'view') as Type
            FROM
              {DatabaseName}.INFORMATION_SCHEMA.TABLES
            WHERE SUBSTRING(TABLE_NAME, 1,2)  <> '__' AND TABLE_NAME <> 'sysdiagrams'"
            .Replace("{DatabaseName}", this.databaseName);

        List<TableAndViewNames> tables = new List<TableAndViewNames>();

        using (IDbConnection db = new SqlConnection(ConnectionString))
        {
            tables = db.Query<TableAndViewNames>(sql).ToList();
        }

        return tables;
    }

    public List<TableColumns> GetTableColumns(string tableName)
    {
        string sql =
            @"SELECT
               c.name 'ColumnName',
               t.name 'Type',
               t.name +

               CASE WHEN t.name IN ('char', 'varchar','nchar','nvarchar') THEN '('+
                     CASE WHEN c.max_length=-1 THEN 'MAX'
                          ELSE CONVERT(VARCHAR(4),
                                       CASE WHEN t.name IN('nchar','nvarchar')
                                       THEN c.max_length/2 ELSE c.max_length END)
                          END +')'
                  WHEN t.name IN ('decimal','numeric')
                          THEN '('+ CONVERT(VARCHAR(4),c.precision)+','
                                  + CONVERT(VARCHAR(4),c.Scale)+')'
                          ELSE '' END
             as ""DDLType"",
             '' as ""CSType"",
             '' as ""NETType"",
             cast(c.max_length as int) 'MaxLength',
             cast(c.precision as int) 'Precision',
             cast(c.scale as int) 'Scale',

             iif(c.is_nullable = 1, 'True','False') 'Nullable',
             iif(ISNULL(i.is_primary_key, 0) = 1,'True', 'False') 'PrimaryKey',
             iif(c.is_identity = 1, 'True', 'False') 'Identity'
       
             FROM
                 sys.columns c
             INNER JOIN
                sys.types t ON c.user_type_id = t.user_type_id
             LEFT OUTER JOIN
                sys.index_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
             LEFT OUTER JOIN
                sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
             WHERE
                c.object_id = OBJECT_ID('{TableName}')"
            .Replace("{TableName}", tableName);

        List<TableColumns> tableColumns = new List<TableColumns>();

        using (IDbConnection db = new SqlConnection(ConnectionString))
        {
            tableColumns = db.Query<TableColumns>(sql).ToList();
        }

        return setTypes(tableColumns, tableName);
    }

    public (string ColumnId, string ColumnAdded, string ColumnnUpdated) GetColumnNames(List<TableColumns> columns)
    {
        string ColumnId = string.Empty;
        string ColumnAdded = string.Empty;
        string ColumnUpdated = string.Empty;

        string[] updateColumns = new string[] { "dateupdated", "updated" };
        string[] addColumns = new string[] { "dateadded", "added" };

        foreach (var column in columns)
        {
            if (column.Identity == "True" && column.PrimaryKey == "True")   ColumnId = column.ColumnName;
            if (updateColumns.Contains(column.ColumnName)) ColumnUpdated = column.ColumnName;
            if (addColumns.Contains(column.ColumnName)) ColumnAdded = column.ColumnName;
        }

        return (ColumnId, ColumnAdded, ColumnUpdated);
    }


    private List<TableColumns> setTypes(List<TableColumns> tableColumns, string tableName)
    {
        List<TableColumns> ammendedTableColumns = new List<TableColumns>();

        foreach (TableColumns tableColumn in tableColumns)
        {
            bool columnFound = false;

            foreach (DataTypes dataType in dataTypes)
            {
                if (tableColumn.DDLType.StartsWith(dataType.sql))
                {
                    columnFound = true;
                    ammendedTableColumns.Add(tableColumn with {NETType = dataType.dotnet, CSType = dataType.cs});
                    break;
                }
            }

            if (! columnFound)
            {
                throw new ArgumentException($"Cannot map {tableName}.{tableColumn.ColumnName}'s data type of {tableColumn.DDLType}");
            }
        }

        return ammendedTableColumns;
    }

    public (string withId, string withoutId) GetColumnsAsParmList(List<TableColumns> columns, 
                                                (string ColumnId, string ColumnAdded, string ColumnnUpdated) columnNames)
    {
        StringBuilder sbAllColumns = new();
        StringBuilder sbIdColumnOnly = new();

        string[] addUpdateColumns = new string[] { "dateadded", "added", "dateupdated", "updated" };

        foreach (var column in columns)
        {
            string nullAssignment = String.Empty;

            if (addUpdateColumns.Contains(column.ColumnName.ToLower()))
            {
                nullAssignment = " = NULL";
            }

            if (column.ColumnName == columnNames.ColumnId)
            {
                sbIdColumnOnly.AppendLine($"    @{column.ColumnName} {column.DDLType}{nullAssignment}, ");
            }
            sbAllColumns.AppendLine($"    @{column.ColumnName} {column.DDLType}{nullAssignment}, ");
        }
        string allColumns = removeTrailingComma(sbAllColumns.ToString());
        string idColumnOnly = removeTrailingComma(sbIdColumnOnly.ToString());

        return (allColumns, idColumnOnly);
    }

    public (string values, string names) GetColumnsInsertInto(List<TableColumns> columns,
                                             (string ColumnId, string ColumnAdded, string ColumnnUpdated) columnNames)
    {
        StringBuilder sbNames = new();
        StringBuilder sbValues = new();

        string[] columnExcludes = new string[] { columnNames.ColumnId, columnNames.ColumnnUpdated };

        foreach (var column in columns)
        {
            if (! columnExcludes.Contains(column.ColumnName.ToLower()))
            {
                sbNames.Append($"[{column.ColumnName}], ");
                sbValues.Append($"@{column.ColumnName}, ");
            }
        }

        string names = removeTrailingComma(sbNames.ToString());
        string values = removeTrailingComma(sbValues.ToString());

        return (names, values);
    }
  
    public (string update, string selectAll) GetColumnsUpdateAndSelectAll(List<TableColumns> columns,
                                                 (string ColumnId, string ColumnAdded, string ColumnnUpdated) columnNames)

    {
        StringBuilder sbUpdate = new();
        StringBuilder sbSelectAll = new();

        string[] columnExcludes = new string[] { columnNames.ColumnId, columnNames.ColumnAdded };

        foreach (var column in columns)
        {
            if (! columnExcludes.Contains(column.ColumnName.ToLower()))
            {
                sbUpdate.Append($"[{column.ColumnName}] = @{column.ColumnName}, ");
            }
            sbSelectAll.Append($"[{column.ColumnName}], ");
        }

        string update = removeTrailingComma(sbUpdate.ToString());
        string selectAll = removeTrailingComma(sbSelectAll.ToString());

        return (update, selectAll);
    }

    private string removeTrailingComma(string str)
    {
        return Regex.Replace(str, @"\s*,\s*$", String.Empty);

    }

    private void PopulateDataTypes()
    {
        dataTypes.Add(new DataTypes("bigint", "Int64", "long"));
        dataTypes.Add(new DataTypes("binary", "Byte[]", "byte[]"));
        dataTypes.Add(new DataTypes("bit", "Boolean", "bool"));
        dataTypes.Add(new DataTypes("char", "String", "string"));
        dataTypes.Add(new DataTypes("date", "DateTime", "System.DateTime"));
        dataTypes.Add(new DataTypes("datetime", "DateTime", "System.DateTime"));
        dataTypes.Add(new DataTypes("datetime2", "DateTime", "System.DateTime"));
        dataTypes.Add(new DataTypes("datetimeoffset", "DateTimeOffset", "System.DateTimeOffset"));
        dataTypes.Add(new DataTypes("decimal", "Decimal", "decimal "));
        dataTypes.Add(new DataTypes("float", "Double", "double"));
        dataTypes.Add(new DataTypes("image", "Byte[]", "byte[]"));
        dataTypes.Add(new DataTypes("int", "Int32", "int"));
        dataTypes.Add(new DataTypes("money", "Decimal", "decimal"));
        dataTypes.Add(new DataTypes("nchar", "String", "string"));
        dataTypes.Add(new DataTypes("ntext", "String", "string"));
        dataTypes.Add(new DataTypes("numeric", "Decimal", "decimal"));
        dataTypes.Add(new DataTypes("nvarchar", "String", "string"));
        dataTypes.Add(new DataTypes("real", "Single", "float"));
        dataTypes.Add(new DataTypes("rowversion", "Byte[]", "byte[]"));
        dataTypes.Add(new DataTypes("smalldatetime", "DateTime", "System.DateTime"));
        dataTypes.Add(new DataTypes("smallint", "Int16", "short"));
        dataTypes.Add(new DataTypes("smallmoney", "Decimal", "decimal"));
        dataTypes.Add(new DataTypes("time", "TimeSpan", "System.TimeSpan"));
        dataTypes.Add(new DataTypes("timestamp", "Byte[]", "byte[]"));
        dataTypes.Add(new DataTypes("tinyint", "Byte", "byte"));
        dataTypes.Add(new DataTypes("varbinary", "Byte[]", "byte[]"));
        dataTypes.Add(new DataTypes("varchar", "String", "string"));
    }
}

