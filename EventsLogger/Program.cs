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

        try
        {
            IConnection connection = await ConnectToRabbitMQWithRetryAsync(rabbitHost);
            using IChannel channel = await connection.CreateChannelAsync();

            await SetupEventListeningAsync(channel);
            await WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка логгера: {ex.Message}");
        }
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
        
        AsyncEventingBasicConsumer consumer = CreateConsumer(channel);
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
        consumer.ReceivedAsync += async (model, ea) => await OnMessageReceivedAsync(channel, ea);
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

            Console.WriteLine($"{type} {id} {value}");
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

    private static async Task<IConnection> ConnectToRabbitMQWithRetryAsync(string host, int maxRetries = 10)
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
}