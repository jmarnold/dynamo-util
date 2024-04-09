using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

var rootCommand = new RootCommand("DynamoDB data loading utility");

var loadCommand = new Command("load", "Loads data from files");
rootCommand.AddCommand(loadCommand);

var dirOption = new Option<string>(
        name: "--dir",
        description: "Specify the directory to load (defaults to ./operations)");
dirOption.AddAlias("-d");
dirOption.SetDefaultValue("./operations");
loadCommand.AddOption(dirOption);

var endpointOption = new Option<string>(
    name: "--endpoint",
    description: "Specify the AWS DynamoDB ServiceURL");
endpointOption.AddAlias("-e");
loadCommand.AddOption(endpointOption);

var regionOption = new Option<string>(
    name: "--region",
    description: "Specify the AWS DynamoDB Region (takes precedence over ServiceURL)");
endpointOption.AddAlias("-r");
loadCommand.AddOption(regionOption);

var tableOption = new Option<string>(
    name: "--table",
    description: "Specify the table name to override for all operations");
endpointOption.AddAlias("-t");
loadCommand.AddOption(tableOption);

//\{(?<variable>[A-Za-z0-9_]+)\}
const string VARIABLE_PATTERN = "\\{(?<variable>[A-Za-z0-9_]+)\\}";
Regex variablePattern = new(VARIABLE_PATTERN, RegexOptions.Compiled);

loadCommand.SetHandler(async (context) =>
{
    var endpoint = context.ParseResult.GetValueForOption(endpointOption);
    var dir = context.ParseResult.GetValueForOption(dirOption) ?? "./operations";
    var table = context.ParseResult.GetValueForOption(tableOption);
    var region = context.ParseResult.GetValueForOption(regionOption);
    var client = new AmazonDynamoDBClient(new AmazonDynamoDBConfig
    {
        ServiceURL = endpoint,
        RegionEndpoint = string.IsNullOrEmpty(region) ? null : RegionEndpoint.GetBySystemName(region)
    });

    var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories);
    foreach (var file in files)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"Reading from {file}  -  ");
        using var reader = new StreamReader(file, Encoding.UTF8);
        var contents = reader.ReadToEnd();
        var matches = variablePattern.Matches(contents);
        var variables = new List<string>();
        foreach (Match match in matches)
        {
            variables.Add(match.Groups["variable"].Value);
        }
    
        foreach (var variable in variables)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
            {
                contents = contents.Replace("{" + variable + "}", value);
            }
        }
        var request = JsonSerializer.Deserialize<TransactWriteItemsRequest>(contents);

        if (!string.IsNullOrEmpty(table))
        {
            foreach (var item in request!.TransactItems)
            {
                if (item.Delete != null)
                    item.Delete.TableName = table;
                
                if (item.Update != null)
                    item.Update.TableName = table;
                
                if (item.Put != null)
                    item.Put.TableName = table;
            }
        }
        
        await client.TransactWriteItemsAsync(request);   
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"{request!.TransactItems.Count} statement(s) executed");
    }
});

await rootCommand.InvokeAsync(args);