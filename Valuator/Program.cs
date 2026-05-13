using StackExchange.Redis;
using RabbitMQ.Client;
using System.Text;

namespace Valuator;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorPages();
        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
            var configuration = ConfigurationOptions.Parse(redisConnectionString);
            return ConnectionMultiplexer.Connect(configuration);
        });

        builder.Services.AddSingleton<IConnection>(sp =>
        {
            var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
            var factory = new ConnectionFactory() { HostName = host };
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

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.MapRazorPages();

        app.Run();
    }
}
