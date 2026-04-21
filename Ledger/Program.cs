using Ledger.Banking;
using Ledger.Infrastructure;
using Ledger.Jobs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


var connectionString = builder.Configuration.GetConnectionString("Ledger")
                       ?? throw new InvalidOperationException("Missing connection string 'Ledger'");

builder.Services.AddDbContext<LedgerDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", "ledger")));

builder.Services.AddSingleton<IMockBank, MockBank>();
builder.Services.AddHostedService<BankStatementPollingJob>();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
    await ChartOfAccountsSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();