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
        string redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        string rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
        var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
        IDatabase redisDb = redis.GetDatabase();

        try {
            IConnection connection = await ConnectToRabbitMQAsync(rabbitHost);
            IChannel consumeChannel = await connection.CreateChannelAsync();
            IChannel publishChannel = await connection.CreateChannelAsync();

            await DeclareSettings(consumeChannel, publishChannel, redisDb);
            await Task.Delay(-1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка логгера: {ex.Message}");
        }
    }

    private static async Task DeclareSettings(IChannel consumeChannel, IChannel publishChannel, IDatabase redisDb) {
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

        await StartConsumerAsync(consumeChannel, publishChannel, redisDb);
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

    private static async Task StartConsumerAsync(IChannel consumeChannel, IChannel publishChannel, IDatabase redisDb)
    {
        var consumer = new AsyncEventingBasicConsumer(consumeChannel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            await ProcessMessageAsync(consumeChannel, publishChannel, ea, redisDb);
        };
        await consumeChannel.BasicConsumeAsync(queue: QueueName, autoAck: false, consumer: consumer);
    }

    private static async Task ProcessMessageAsync(IChannel consumeChannel, IChannel publishChannel, BasicDeliverEventArgs ea, IDatabase redisDb)
    {
        var id = Encoding.UTF8.GetString(ea.Body.ToArray());
        try
        {
            var text = await redisDb.StringGetAsync($"TEXT-{id}");
            if (text.IsNullOrEmpty)
            {
                await consumeChannel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }

            TimeSpan interval = TimeSpan.FromSeconds(new Random().Next(3, 15));
            Console.WriteLine($"Waiting {interval}");
            await Task.Delay(interval);

            double rank = CalculateRank(text);
            await redisDb.StringSetAsync($"RANK-{id}", rank.ToString());

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
            Console.WriteLine($"rank {rank} для {id} обработан и событие отправлено.");
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

    private static async Task WaitForShutdownAsync()
    {
        await Task.Delay(-1);
    }
}
