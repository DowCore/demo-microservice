namespace Meta.Dow.SaaS.Orchestration;

public static class DataSourceProvider
{
    public const string Postgres = "postgres";
    public const string MySql = "mysql";
    public const string SqlServer = "sqlserver";
    public const string Oracle = "oracle";
    public const string Redis = "redis";
    public const string MongoDb = "mongodb";

    public const string FamilySql = "sql";
    public const string FamilyRedis = "redis";
    public const string FamilyMongo = "mongo";

    public static readonly string[] All =
    [
        Postgres,
        MySql,
        SqlServer,
        Oracle,
        Redis,
        MongoDb
    ];

    public static bool IsSupported(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return false;
        }

        var p = Normalize(provider);
        return p is Postgres or MySql or SqlServer or Oracle or Redis or MongoDb;
    }

    public static string Normalize(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "mongo" => MongoDb,
        "mssql" or "sql server" => SqlServer,
        "postgresql" or "npgsql" => Postgres,
        var x => x
    };

    public static string GetFamily(string? provider)
    {
        var p = Normalize(provider ?? "");
        return p switch
        {
            Postgres or MySql or SqlServer or Oracle => FamilySql,
            Redis => FamilyRedis,
            MongoDb => FamilyMongo,
            _ => "unknown"
        };
    }

    public static bool IsSql(string? provider) => GetFamily(provider) == FamilySql;

    public static bool IsRedis(string? provider) => GetFamily(provider) == FamilyRedis;

    public static bool IsMongo(string? provider) => GetFamily(provider) == FamilyMongo;

    public static string DisplayName(string provider) => Normalize(provider) switch
    {
        Postgres => "PostgreSQL",
        MySql => "MySQL",
        SqlServer => "SQL Server",
        Oracle => "Oracle",
        Redis => "Redis",
        MongoDb => "MongoDB",
        var x => x
    };

    public static string ConnectionHint(string provider) => Normalize(provider) switch
    {
        Postgres => "Host=127.0.0.1;Port=5432;Database=app;Username=u;Password=p",
        MySql => "Server=127.0.0.1;Port=3306;Database=app;User ID=u;Password=p",
        SqlServer =>
            "Server=127.0.0.1,1433;Database=app;User Id=u;Password=p;Encrypt=True;TrustServerCertificate=True",
        Oracle => "User Id=u;Password=p;Data Source=127.0.0.1:1521/ORCL",
        Redis => "localhost:6379,password=,defaultDatabase=0",
        MongoDb => "mongodb://user:pass@127.0.0.1:27017/app?authSource=admin",
        _ => ""
    };
}
