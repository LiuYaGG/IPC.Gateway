/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Maintenance
* 项目描述 ：
* 类 名 称 ：Program
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Maintenance
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

return MaintenanceProgram.Run(args);

internal static class MaintenanceProgram
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string command = args[0].Trim().ToLowerInvariant();
        Dictionary<string, string> options = ParseOptions(args.Skip(1).ToArray());
        try
        {
            return command switch
            {
                "reset-admin" => ResetAdminPassword(options),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("执行失败：" + ex.Message);
            return 1;
        }
    }

    private static int ResetAdminPassword(IReadOnlyDictionary<string, string> options)
    {
        string password = GetOption(options, "password");
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("请通过 --password 指定新密码。");
            PrintResetAdminUsage();
            return 2;
        }

        string configDirectory = ResolveConfigDirectory(GetOption(options, "config-dir"));
        string environmentName = ResolveEnvironmentName(GetOption(options, "environment"), configDirectory);
        IConfigurationRoot configuration = BuildConfiguration(configDirectory, environmentName);

        GatewayDatabaseOptions databaseOptions = CreateDatabaseOptions(configuration);
        GatewayAccountSecurityOptions securityOptions = CreateAccountSecurityOptions(configuration);
        string username = GetOption(options, "username");
        if (string.IsNullOrWhiteSpace(username))
            username = configuration["Gateway:Auth:BootstrapAdminUsername"] ?? "admin";
        username = username.Trim();

        GatewayUserStore store = new GatewayUserStore(
            databaseOptions,
            new GatewayBootstrapUserOptions { AutoCreateAdmin = false },
            securityOptions);

        GatewayUserInfo? existing = store.FindByUsername(username);
        string displayName = existing?.DisplayName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = configuration["Gateway:Auth:BootstrapAdminDisplayName"] ?? "System Administrator";

        store.UpsertUser(username, displayName, "Admin", true, password);

        Console.WriteLine("管理员密码已重置。");
        Console.WriteLine("账号：" + username);
        Console.WriteLine("配置目录：" + configDirectory);
        Console.WriteLine("配置环境：" + (string.IsNullOrWhiteSpace(environmentName) ? "默认" : environmentName));
        Console.WriteLine("数据库：" + DescribeDatabase(databaseOptions));
        Console.WriteLine("建议重启 WebHost，并清理仍在使用的旧登录会话。");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                continue;

            string key = token.Substring(2).Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            string value = "true";
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[index + 1];
                index++;
            }

            options[key] = value;
        }

        return options;
    }

    private static IConfigurationRoot BuildConfiguration(string configDirectory, string environmentName)
    {
        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(configDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        if (!string.IsNullOrWhiteSpace(environmentName))
            builder.AddJsonFile("appsettings." + environmentName + ".json", optional: true, reloadOnChange: false);

        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    private static string ResolveConfigDirectory(string requestedDirectory)
    {
        if (!string.IsNullOrWhiteSpace(requestedDirectory))
            return Path.GetFullPath(requestedDirectory);

        string currentDirectory = Directory.GetCurrentDirectory();
        string[] candidates =
        {
            currentDirectory,
            Path.Combine(currentDirectory, "IPC.Gateway.WebHost"),
            AppContext.BaseDirectory
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
                return Path.GetFullPath(candidate);
        }

        return Path.GetFullPath(currentDirectory);
    }

    private static string ResolveEnvironmentName(string requestedEnvironment, string configDirectory)
    {
        if (!string.IsNullOrWhiteSpace(requestedEnvironment))
            return requestedEnvironment.Trim();

        string environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(environment))
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(environment))
            return environment.Trim();

        return File.Exists(Path.Combine(configDirectory, "appsettings.Development.json"))
            ? "Development"
            : string.Empty;
    }

    private static GatewayDatabaseOptions CreateDatabaseOptions(IConfiguration configuration)
    {
        IConfigurationSection database = configuration.GetSection("Gateway:Database");
        return new GatewayDatabaseOptions
        {
            Provider = database["Provider"] ?? "PostgreSQL",
            ConnectionString = database["ConnectionString"] ?? string.Empty,
            Host = database["Host"] ?? "localhost",
            Port = GetInt(database, "Port", 5432),
            Database = database["Database"] ?? "ipc_gateway",
            Username = database["Username"] ?? "postgres",
            Password = database["Password"] ?? string.Empty,
            AutoCreateDatabase = GetBool(database, "AutoCreateDatabase", true)
        };
    }

    private static GatewayAccountSecurityOptions CreateAccountSecurityOptions(IConfiguration configuration)
    {
        IConfigurationSection password = configuration.GetSection("Gateway:Security:PasswordPolicy");
        IConfigurationSection lockout = configuration.GetSection("Gateway:Security:AccountLockout");
        return new GatewayAccountSecurityOptions
        {
            Password = new GatewayPasswordPolicyOptions
            {
                Enabled = GetBool(password, "Enabled", true),
                MinLength = GetInt(password, "MinLength", 8),
                MaxLength = GetInt(password, "MaxLength", 128),
                RequireUppercase = GetBool(password, "RequireUppercase", false),
                RequireLowercase = GetBool(password, "RequireLowercase", true),
                RequireDigit = GetBool(password, "RequireDigit", true),
                RequireSymbol = GetBool(password, "RequireSymbol", true),
                RejectUsernameInPassword = GetBool(password, "RejectUsernameInPassword", true)
            },
            Lockout = new GatewayAccountLockoutOptions
            {
                Enabled = GetBool(lockout, "Enabled", true),
                MaxFailedAttempts = GetInt(lockout, "MaxFailedAttempts", 5),
                LockoutMinutes = GetInt(lockout, "LockoutMinutes", 15),
                ResetFailedCountOnSuccess = GetBool(lockout, "ResetFailedCountOnSuccess", true)
            }
        };
    }

    private static string DescribeDatabase(GatewayDatabaseOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.Provider + "（ConnectionString）";

        return options.Provider + " " + options.Host + ":" + options.Port + "/" + options.Database;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, string key)
    {
        return options.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue)
    {
        return int.TryParse(configuration[key], out int value) ? value : defaultValue;
    }

    private static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
    {
        return bool.TryParse(configuration[key], out bool value) ? value : defaultValue;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine("未知命令：" + command);
        PrintUsage();
        return 2;
    }

    private static bool IsHelp(string value)
    {
        return value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("help", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("IPC.Gateway.Maintenance");
        Console.WriteLine();
        PrintResetAdminUsage();
    }

    private static void PrintResetAdminUsage()
    {
        Console.WriteLine("用法：");
        Console.WriteLine("  IPC.Gateway.Maintenance reset-admin --password \"new-password\"");
        Console.WriteLine();
        Console.WriteLine("可选参数：");
        Console.WriteLine("  --username <name>       指定管理员账号，默认读取 BootstrapAdminUsername 或 admin");
        Console.WriteLine("  --config-dir <path>     指定 WebHost 配置目录");
        Console.WriteLine("  --environment <name>    指定配置环境，例如 Development 或 Production");
    }
}
