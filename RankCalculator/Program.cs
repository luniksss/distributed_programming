using System.Text;
using System.Text.RegularExpressions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using System.Text.Json;

namespace RankCalculator;

class Program
{
    private const string QueueName = "rank_tasks";
    private const string EventsExchangeName = "events_exchange";

    static async Task Main(string[] args)
    {
        string mainConn = Environment.GetEnvironmentVariable("DB_MAIN") ?? "localhost:6383";
        string ruConn = Environment.GetEnvironmentVariable("DB_RU") ?? "localhost:6380";
        string euConn = Environment.GetEnvironmentVariable("DB_EU") ?? "localhost:6381";
        string asiaConn = Environment.GetEnvironmentVariable("DB_ASIA") ?? "localhost:6382";

        var mainRedis = await ConnectionMultiplexer.ConnectAsync(mainConn);
        var ruRedis = await ConnectionMultiplexer.ConnectAsync(ruConn);
        var euRedis = await ConnectionMultiplexer.ConnectAsync(euConn);
        var asiaRedis = await ConnectionMultiplexer.ConnectAsync(asiaConn);

        var shardConnections = new Dictionary<string, IConnectionMultiplexer>
        {
            ["RU"] = ruRedis,
            ["EU"] = euRedis,
            ["ASIA"] = asiaRedis
        };
        
        string rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";

        try {
            IConnection connection = await ConnectToRabbitMQAsync(rabbitHost);
            IChannel consumeChannel = await connection.CreateChannelAsync();
            IChannel publishChannel = await connection.CreateChannelAsync();

            await DeclareSettings(consumeChannel, publishChannel, mainRedis, shardConnections);
            await Task.Delay(-1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка логгера: {ex.Message}");
        }
    }

    private static async Task DeclareSettings(IChannel consumeChannel, IChannel publishChannel,
        IConnectionMultiplexer mainRedis, Dictionary<string, IConnectionMultiplexer> shardConnections) {
        await consumeChannel.QueueDeclareAsync(
            queue: QueueName, 
            durable: true, 
            exclusive: false, 
            autoDelete: false
        );
        await publishChannel.ExchangeDeclareAsync(
            exchange: EventsExchangeName, 
            type: ExchangeType.Fanout
        );

        await StartConsumerAsync(consumeChannel, publishChannel, mainRedis, shardConnections);
        Console.WriteLine("ожидание заданий");
    }

    private static async Task<IConnection> ConnectToRabbitMQAsync(string host, int maxRetries = 10)
    {
        var factory = new ConnectionFactory { HostName = host };
        for (int i = 1; i <= maxRetries; i++)
        {
            try { return await factory.CreateConnectionAsync(); }
            catch
            {
                await Task.Delay(2000);
            }
        }
        throw new Exception("не удалось подключиться к RabbitMQ");
    }

    private static async Task StartConsumerAsync(IChannel consumeChannel, IChannel publishChannel,
        IConnectionMultiplexer mainRedis, Dictionary<string, IConnectionMultiplexer> shardConnections)
    {
        var consumer = new AsyncEventingBasicConsumer(consumeChannel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            await ProcessMessageAsync(consumeChannel, publishChannel, ea, mainRedis, shardConnections);
        };
        await consumeChannel.BasicConsumeAsync(queue: QueueName, autoAck: false, consumer: consumer);
    }

    private static async Task ProcessMessageAsync(IChannel consumeChannel, IChannel publishChannel,
        BasicDeliverEventArgs ea, IConnectionMultiplexer mainRedis, Dictionary<string, IConnectionMultiplexer> shardConnections)
    {
        var id = Encoding.UTF8.GetString(ea.Body.ToArray());
        try
        {
            var mainDb = mainRedis.GetDatabase();
            string region = mainDb.StringGet($"SHARD-{id}");
            if (string.IsNullOrEmpty(region))
            {
                Console.WriteLine($"регион для ID {id} не найден");
                await consumeChannel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }
            Console.WriteLine($"LOOKUP: {id}, {region}");

            if (!shardConnections.TryGetValue(region, out var shardRedis))
            {
                Console.WriteLine($"неизвестный регион {region}");
                await consumeChannel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }

            IDatabase shardDb = shardRedis.GetDatabase();
            var text = await shardDb.StringGetAsync($"TEXT-{id}");
            if (text.IsNullOrEmpty)
            {
                await consumeChannel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }

            double rank = CalculateRank(text);
            await shardDb.StringSetAsync($"RANK-{id}", rank.ToString());

            var eventData = JsonSerializer.Serialize(new { 
                Type = "RankCalculated", 
                Id = id, 
                Value = rank 
            });
            await publishChannel.BasicPublishAsync(
                exchange: EventsExchangeName, 
                routingKey: "", 
                body: Encoding.UTF8.GetBytes(eventData)
            );

            await consumeChannel.BasicAckAsync(ea.DeliveryTag, false);
            Console.WriteLine($"rank {rank} для {id} обработан и сохранён в сегмент {region}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка: {ex.Message}");
            await consumeChannel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    private static double CalculateRank(string text)
    {
        int letters = Regex.Matches(text, @"[а-яА-Яa-zA-ZёЁ]").Count;
        return text.Length == 0 ? 0 : (double)letters / text.Length;
    }
}
