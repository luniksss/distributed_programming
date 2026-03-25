using System.Text;
using System.Text.RegularExpressions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

namespace RankCalculator;

class Program
{
    private const string QueueName = "rank_tasks";

    static async Task Main(string[] args)
    {
        string redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        string rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";

        var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
        IDatabase redisDb = redis.GetDatabase();

        IConnection connection = await ConnectToRabbitMQWithRetryAsync(rabbitHost);
        IChannel channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        await StartConsumerAsync(channel, redisDb);

        Console.WriteLine("ожидание заданий");
        Console.ReadLine();

        await channel.CloseAsync();
        await connection.CloseAsync();
    }

    private static async Task<IConnection> ConnectToRabbitMQWithRetryAsync(string host, int maxRetries = 10)
    {
        var factory = new ConnectionFactory { HostName = host };
        for (int i = 1; i <= maxRetries; i++)
        {
            try
            {
                return await factory.CreateConnectionAsync();
            }
            catch
            {
                Console.WriteLine($"попытка {i}/{maxRetries}: не удалось подключиться к RabbitMQ");
                await Task.Delay(2000);
            }
        }
        throw new Exception("не удалось подключиться к RabbitMQ");
    }

    private static async Task StartConsumerAsync(IChannel channel, IDatabase redisDb)
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            await ProcessMessageAsync(channel, ea, redisDb);
        };
        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer
        );
    }

    private static async Task ProcessMessageAsync(IChannel channel, BasicDeliverEventArgs ea, IDatabase redisDb)
    {
        var body = ea.Body.ToArray();
        var id = Encoding.UTF8.GetString(body);
        Console.WriteLine($"появилось задание для id={id}");

        try
        {
            string textKey = $"TEXT-{id}";
            var text = await redisDb.StringGetAsync(textKey);
            if (text.IsNullOrEmpty)
            {
                Console.WriteLine($"текст для {id} не найден");
                await channel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }

            double rank = CalculateRank(text);
            Console.WriteLine($"[DEBUG] Вычислен ранг: {rank}");
            string rankKey = $"RANK-{id}";
            Console.WriteLine($"[DEBUG] Сохраняем в Redis: ключ={rankKey}, значение={rank}");
            bool success = await redisDb.StringSetAsync(rankKey, rank.ToString());
            Console.WriteLine($"[DEBUG] Результат StringSet: {success}");
            if (success)
            {
                Console.WriteLine($"ранг {rank} для {id} сохранён в Redis");
            }
            else
            {
                Console.WriteLine($"не удалось сохранить ранг для {id} в Redis");
            }

            await channel.BasicAckAsync(ea.DeliveryTag, false);
            Console.WriteLine($"[DEBUG] BasicAck выполнен");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка при обработке {id}: {ex}");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    private static double CalculateRank(string text)
    {
        int letters = Regex.Matches(text, @"[а-яА-Яa-zA-ZёЁ]").Count;
        return (double)letters / text.Length;
    }
}