using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Api.Services.SiteDashboard;

namespace SmartGridSuite.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var conn = builder.Configuration.GetConnectionString("SmartGridDb");
            builder.Services.AddDbContext<SmartGridDbContext>(opt =>
                opt.UseMySql(conn, ServerVersion.AutoDetect(conn)));

            builder.Services.Configure<ParentDatabaseOptions>(
                builder.Configuration.GetSection(ParentDatabaseOptions.SectionName));

            builder.Services.Configure<EmailOptions>(
                builder.Configuration.GetSection(EmailOptions.SectionName));

            builder.Services.Configure<ClientVersionOptions>(
                builder.Configuration.GetSection(ClientVersionOptions.SectionName));

            builder.Services.AddScoped<ParentDatabaseConnectionFactory>();

            builder.Services.AddScoped<ParentSyncService>();

            builder.Services.AddScoped<SiteDashboardCacheService>();

            builder.Services.AddScoped<SiteDashboardLookupService>();

            builder.Services.AddScoped<SiteDashboardCacheRefreshService>();

            builder.Services.Configure<SiteDashboardCacheRefreshOptions>(builder.Configuration.GetSection(
                SiteDashboardCacheRefreshOptions.SectionName));

            builder.Services.AddHostedService<
                SiteDashboardCacheRefreshHostedService>();

            builder.Services.AddScoped<SnmpPollingService>();

            builder.Services.AddScoped<TruckBoardInitializationService>();

            builder.Services.AddScoped<EmailService>();

            builder.Services.AddScoped<DailyAssignmentEmailSequenceService>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
