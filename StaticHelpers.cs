using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

public class StaticHelpers
{
    public static SqlInfo AttemptDatabaseConnection(string databaseName)
    {

        string connectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString.Replace("{Database}", databaseName);
        ArgumentNullException.ThrowIfNull(connectionString);

        SqlInfo si;

        try
        {
            si = new SqlInfo(connectionString, databaseName);
            return si;
        }
        catch (SystemException ex)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Environment.Exit(-1);
            return null;
        }
    }

    public static string GetTemplatePath(string templateFolder)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   templateFolder);

        if (! Directory.Exists(path))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Red;
            Console.WriteLine($"Template {path} does not exist.");
            Environment.Exit(-1);
        }
        return path; 
    }

    public static string ReadTemplateFile(string templateFilename)
    {
        string? templateFolder = ConfigurationManager.AppSettings["templateFileFolder"];
        if (templateFolder is null )
        {
            templateFolder = "DapperCrud";
        }

        string templateFilePath = Path.Combine(GetTemplatePath(templateFolder), templateFilename);
        if (!File.Exists(templateFilePath))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Red;
            Console.WriteLine($"Template file [{templateFilePath}] does not exist.");
            Environment.Exit(-1);
        }

        string fileContents = File.ReadAllText(templateFilePath);
        return fileContents;
    }



}