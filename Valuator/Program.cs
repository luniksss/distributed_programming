using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using RabbitMQ.Client;
using Valuator.Data;

namespace Valuator;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var connectionString = builder.Configuration.GetConnectionString("IdentityConnection");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddIdentity<IdentityUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });

        builder.Services.AddRazorPages();

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
            if (string.IsNullOrEmpty(redisConnectionString))
                redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "redis:6379";
            var configuration = ConfigurationOptions.Parse(redisConnectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configuration);
        });

        builder.Services.AddSingleton<IConnection>(sp =>
        {
            var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
            var userName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest";
            var password = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "guest";

            var factory = new ConnectionFactory
            {
                HostName = host,
                UserName = userName,
                Password = password
            };

            const int maxRetries = 10;
            for (int i = 1; i <= maxRetries; i++)
            {
                try
                {
                    return factory.CreateConnection();
                }
                catch
                {
                    Thread.Sleep(2000);
                }
            }
            throw new Exception("не удалось подключиться к RabbitMQ");
        });

        builder.Services.AddSingleton<IModel>(sp =>
        {
            var connection = sp.GetRequiredService<IConnection>();
            var channel = connection.CreateModel();

            channel.QueueDeclare(
                queue: "rank_tasks",
                durable: true,
                exclusive: false,
                autoDelete: false
            );
            channel.ExchangeDeclare(
                exchange: "events_exchange",
                type: ExchangeType.Fanout
            );

            return channel;
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Database.EnsureCreated();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }
        app.UseStaticFiles();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapRazorPages();
        app.Run();
    }
}