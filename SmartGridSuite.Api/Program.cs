using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Api.Services.ParentSync;


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

            builder.Services.AddScoped<ParentSyncService>();

            builder.Services.AddScoped<SnmpPollingService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
