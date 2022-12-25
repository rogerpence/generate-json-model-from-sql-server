using Cocona;
using System.Configuration;
using System.Text;

var builder = CoconaApp.CreateBuilder();
var app = builder.Build();


app.AddCommand("test", ([Option('i')]string include, [Argument]string? file) => 
{
    Console.WriteLine($"{include} {file}");
});

app.AddCommand("generate", ([Option('d')] string DatabaseName,
                                           [Option('t')] string TemplateFile,
                                           [Option('n')] string? TableName) =>
{
    if (TableName is null) TableName = "all";
    TableName = TableName.ToLower();

    SqlInfo si = StaticHelpers.AttemptDatabaseConnection(DatabaseName);

    var tableObjects = si.GetTableAndViewNames();

    if (TableName != "all" && ! tableObjects.Any(table => table.TableName == TableName))
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.Red;
        Console.WriteLine($"Table {TableName} does not exist.");
        Console.ResetColor();
        Environment.Exit(-1);
    }

    string[] tables;
    if (TableName != "all")
    {
        tables = new string[] { TableName };
    }
    else 
    {
        tables = tableObjects.Select(table => table.TableName).ToArray();
    }

    StringBuilder allProcs = new();

    foreach (var table in tables)
    {
        var columns = si.GetTableColumns(table);

        var columnNames = si.GetColumnNames(columns);

        (string allColumnsAsParms,   string idColumnAsParm)      = si.GetColumnsAsParmList(columns, columnNames);
        (string insertColumnNames,   string insertColumnValues)  = si.GetColumnsInsertInto(columns, columnNames);
        (string updateColumns,       string selectAllColumns)    = si.GetColumnsUpdateAndSelectAll(columns, columnNames);

        string templateContents = StaticHelpers.ReadTemplateFile(TemplateFile);
        templateContents = templateContents.Replace("{{table}}", table);
        templateContents = templateContents.Replace("{{allColumnsAsParms}}", allColumnsAsParms);
        templateContents = templateContents.Replace("{{idColumnAsParm}}", idColumnAsParm);
        templateContents = templateContents.Replace("{{insertColumnNames}}", insertColumnNames);
        templateContents = templateContents.Replace("{{insertColumnValues}}", insertColumnValues);
        templateContents = templateContents.Replace("{{updateColumns}}", updateColumns);
        templateContents = templateContents.Replace("{{selectAllColumns}}", selectAllColumns);

        templateContents = templateContents.Replace("{{idColumnName}}", columnNames.ColumnId);

        string assignUpdateDate = String.Empty;
        string assignAddDate = String.Empty;

        if (columnNames.ColumnnUpdated != String.Empty)   assignUpdateDate = $"SET @{columnNames.ColumnnUpdated} = GETDATE()";
        if (columnNames.ColumnAdded != String.Empty)      assignAddDate    = $"SET @{columnNames.ColumnAdded} = GETDATE()";

        templateContents = templateContents.Replace("{{assignUpdateDate}}", assignUpdateDate);
        templateContents = templateContents.Replace("{{assignAddDate}}", assignAddDate);

        allProcs.Append(templateContents);
    }
    TextCopy.ClipboardService.SetText(allProcs.ToString());
});

app.Run();