using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using ServerlessAPI;
using ServerlessAPI.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

string region = Environment.GetEnvironmentVariable("AWS_REGION") ?? RegionEndpoint.USEast2.SystemName;
string jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "your-secret-key-change-this-in-production";
string sampleTable = Environment.GetEnvironmentVariable("SAMPLE_TABLE") ?? "ServerlessAPIBookCatalog";

builder.Services
        .AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(region)))
        .AddScoped<IDynamoDBContext, DynamoDBContext>()
        .AddScoped<IBookRepository, BookRepository>()
        .AddScoped<JwtValidator>(provider => new JwtValidator(jwtSecret));

// Add AWS Lambda support
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();