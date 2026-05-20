using StackExchange.Redis;
using RabbitMQ.Client;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Valuator.Services;

namespace Valuator;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorPages();
        builder.Services.AddScoped<IUserService, UserService>();

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Login";
                options.Cookie.HttpOnly = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(1);
            });

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");
            var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "redis:6379";
            var configuration = ConfigurationOptions.Parse(redisConnectionString);
            if (!string.IsNullOrEmpty(redisPassword))
                configuration.Password = redisPassword;

            return ConnectionMultiplexer.Connect(configuration);
        });

        builder.Services.AddSingleton<IConnection>(sp =>
        {
            var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
            var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest";
            var rabbitPass = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
            var factory = new ConnectionFactory
            {
                HostName = host,
                UserName = rabbitUser,
                Password = rabbitPass
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
