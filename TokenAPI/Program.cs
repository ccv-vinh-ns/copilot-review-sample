using Microsoft.OpenApi.Models;
using OracleDbConnector.CustomerCC;
using OracleDbConnector.Models;
using TokenAPI.Common.Utils;
using TokenAPI.Middlewares;
using TokenAPI.Services.Organization;
using TokenAPI.Services.Staff;
using TokenValidation;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
Console.WriteLine($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json");
builder.Configuration
    .AddJsonFile("appsettings.json", true, true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", true, true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IOracleConnectionManager>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var orcConfig = new OracleDbConnectorModel
    {
        NoSqlConfig = new NoSqlConfig
        {
            Endpoint = configuration.GetValue<string>("NoSQLEndpoint") ?? "https://nosql.ap-tokyo-1.oci.oraclecloud.com",
            ServiceType = Enum.Parse<ServiceType>(configuration.GetValue<string>("NoSQLServiceType") ?? "Cloud", true)
        }
    };

    return new OracleConnectionManager(loggerFactory, orcConfig);
});

builder.Services.AddTransient<TokenUtils>();
builder.Services.AddTransient<IValidationAccessToken, ValidationAccessToken>();

builder.Services.AddTransient<IOrganizationService, OrganizationService>();
builder.Services.AddTransient<ILoginService, LoginService>();


builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.AddServer(new OpenApiServer { Url = "/token" });
});

var app = builder.Build();

app.UseMiddleware<CustomExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
