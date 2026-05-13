using StackExchange.Redis;
using RabbitMQ.Client;
using System.Text;

namespace Valuator;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorPages();
        builder.Services.AddKeyedSingleton<IConnectionMultiplexer>("MAIN", (sp, key) =>
        {
            var connString = Environment.GetEnvironmentVariable("DB_MAIN") ?? "localhost:6383";
            return ConnectionMultiplexer.Connect(connString);
        });

        builder.Services.AddKeyedSingleton<IConnectionMultiplexer>("RU", (sp, key) =>
        {
            var connString = Environment.GetEnvironmentVariable("DB_RU") ?? "localhost:6380";
            return ConnectionMultiplexer.Connect(connString);
        });
        builder.Services.AddKeyedSingleton<IConnectionMultiplexer>("EU", (sp, key) =>
        {
            var connString = Environment.GetEnvironmentVariable("DB_EU") ?? "localhost:6381";
            return ConnectionMultiplexer.Connect(connString);
        });
        builder.Services.AddKeyedSingleton<IConnectionMultiplexer>("ASIA", (sp, key) =>
        {
            var connString = Environment.GetEnvironmentVariable("DB_ASIA") ?? "localhost:6382";
            return ConnectionMultiplexer.Connect(connString);
        });
        builder.Services.AddScoped<IShardResolver, ShardResolver>();

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
