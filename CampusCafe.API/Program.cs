var builder = WebApplication.CreateBuilder(args);

// Enable CORS for browser fetch requests
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Keep quantity columns in the .accdb as whole numbers (safe to run on every start)
if (app.Configuration.GetValue<bool>("Database:WholeNumberQuantities", true))
{
    var connStr = app.Configuration.GetConnectionString("DefaultConnection") ?? CampusCafe.API.DbHelper.FallbackConnection;
    CampusCafe.API.DbMigration.Run(connStr, app.Logger);
}

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();