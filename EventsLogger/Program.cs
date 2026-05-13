using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventsLogger;

class Program
{
    private const string EventsExchangeName = "events_exchange";

    static async Task Main(string[] args)
    {
        string rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
        string rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest";
        string rabbitPass = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "guest";

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = rabbitHost,
                UserName = rabbitUser,
                Password = rabbitPass
            };

            IConnection connection = await ConnectToRabbitMQWithRetryAsync(factory);
            await using IChannel channel = await connection.CreateChannelAsync();

            await SetupEventListeningAsync(channel);
            await WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка логгера: {ex.Message}");
        }
    }

    private static async Task<IConnection> ConnectToRabbitMQWithRetryAsync(ConnectionFactory factory, int maxRetries = 10)
    {
        for (int i = 1; i <= maxRetries; i++)
        {
            try
            {
                return await factory.CreateConnectionAsync();
            }
            catch
            {
                await Task.Delay(2000);
            }
        }
        throw new Exception("не удалось подключиться к RabbitMQ после нескольких попыток");
    }

    private static async Task SetupEventListeningAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(
            exchange: EventsExchangeName,
            type: ExchangeType.Fanout
        );

        QueueDeclareOk queueDeclareResult = await channel.QueueDeclareAsync(
            queue: "",
            durable: false,
            exclusive: true,
            autoDelete: true
        );
        string myQueueName = queueDeclareResult.QueueName;

        await channel.QueueBindAsync(
            queue: myQueueName,
            exchange: EventsExchangeName,
            routingKey: ""
        );

        var consumer = CreateConsumer(channel);
        await channel.BasicConsumeAsync(
            queue: myQueueName,
            autoAck: false,
            consumer: consumer
        );

        Console.WriteLine("запущен и слушает события...");
    }

    private static AsyncEventingBasicConsumer CreateConsumer(IChannel channel)
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) => await OnMessageReceivedAsync(channel, ea);
        return consumer;
    }

    private static async Task OnMessageReceivedAsync(IChannel channel, BasicDeliverEventArgs ea)
    {
        byte[] body = ea.Body.ToArray();
        string message = Encoding.UTF8.GetString(body);

        try
        {
            using JsonDocument doc = JsonDocument.Parse(message);
            JsonElement root = doc.RootElement;

            string type = root.GetProperty("Type").GetString();
            string id = root.GetProperty("Id").GetString();
            double value = root.GetProperty("Value").GetDouble();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {type} | Id: {id} | Value: {value}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка парсинга события: {ex.Message}");
        }

        await channel.BasicAckAsync(ea.DeliveryTag, false);
    }

    private static async Task WaitForShutdownAsync()
    {
        await Task.Delay(-1);
    }
}