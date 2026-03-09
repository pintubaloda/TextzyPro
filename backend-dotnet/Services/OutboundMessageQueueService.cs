using System.Text.Json;
using System.Threading.Channels;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Textzy.Api.Services;

public sealed class OutboundMessageQueueItem
{
    public Guid MessageId { get; init; }
    public Guid TenantId { get; init; }
    public string TenantSlug { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
}

public class OutboundMessageQueueService(IConfiguration config, ILogger<OutboundMessageQueueService> logger)
{
    private readonly Channel<OutboundMessageQueueItem> _memory = Channel.CreateUnbounded<OutboundMessageQueueItem>();
    private int _memoryDepth = 0;
    private readonly string _provider = (config["OutboundQueue:Provider"] ?? "memory").Trim().ToLowerInvariant();
    private readonly string _redisConn = config["OutboundQueue:RedisConnection"] ?? config["REDIS_URL"] ?? string.Empty;
    private readonly string _redisKey = config["OutboundQueue:RedisListKey"] ?? "textzy:outbound:queue";
    private readonly string _rabbitConn = config["OutboundQueue:RabbitMq:ConnectionString"] ?? config["RABBITMQ_URL"] ?? string.Empty;
    private readonly string _rabbitQueue = config["OutboundQueue:RabbitMq:QueueName"] ?? "textzy.outbound.queue";
    private readonly string _rabbitExchange = config["OutboundQueue:RabbitMq:ExchangeName"] ?? "textzy.outbound.exchange";
    private readonly string _rabbitRoutingKey = config["OutboundQueue:RabbitMq:RoutingKey"] ?? "message.send";
    private readonly string _rabbitVhost = config["OutboundQueue:RabbitMq:VHost"] ?? string.Empty;
    private readonly string _rabbitUser = config["OutboundQueue:RabbitMq:Username"] ?? string.Empty;
    private readonly string _rabbitPass = config["OutboundQueue:RabbitMq:Password"] ?? string.Empty;
    private readonly bool _rabbitUseTls = bool.TryParse(config["OutboundQueue:RabbitMq:UseTls"], out var t1) ? t1 : true;
    private readonly string _sqsQueueUrl = config["OutboundQueue:Sqs:QueueUrl"] ?? config["AWS_SQS_OUTBOUND_QUEUE_URL"] ?? string.Empty;
    private readonly Lazy<ConnectionMultiplexer?> _redis = new(() =>
    {
        try
        {
            var conn = config["OutboundQueue:RedisConnection"] ?? config["REDIS_URL"] ?? string.Empty;
            return string.IsNullOrWhiteSpace(conn)
                ? null
                : ConnectionMultiplexer.Connect(conn);
        }
        catch
        {
            return null;
        }
    });
    private readonly Lazy<IConnection?> _rabbitConnLazy = new(() =>
    {
        try
        {
            var conn = config["OutboundQueue:RabbitMq:ConnectionString"] ?? config["RABBITMQ_URL"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(conn)) return null;
            var vhost = config["OutboundQueue:RabbitMq:VHost"] ?? string.Empty;
            var user = config["OutboundQueue:RabbitMq:Username"] ?? string.Empty;
            var pass = config["OutboundQueue:RabbitMq:Password"] ?? string.Empty;
            var useTls = bool.TryParse(config["OutboundQueue:RabbitMq:UseTls"], out var tlsFlag) ? tlsFlag : true;
            return BuildRabbitConnection(conn, vhost, user, pass, useTls);
        }
        catch
        {
            return null;
        }
    });
    private readonly Lazy<IAmazonSQS?> _sqs = new(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config["OutboundQueue:Sqs:QueueUrl"] ?? config["AWS_SQS_OUTBOUND_QUEUE_URL"])) return null;
            var sqsConfig = new AmazonSQSConfig();
            if (!string.IsNullOrWhiteSpace(config["OutboundQueue:Sqs:ServiceUrl"] ?? config["AWS_SQS_SERVICE_URL"]))
            {
                sqsConfig.ServiceURL = config["OutboundQueue:Sqs:ServiceUrl"] ?? config["AWS_SQS_SERVICE_URL"];
            }
            else if (!string.IsNullOrWhiteSpace(config["OutboundQueue:Sqs:Region"] ?? config["AWS_REGION"]))
            {
                sqsConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(config["OutboundQueue:Sqs:Region"] ?? config["AWS_REGION"]);
            }

            var access = config["OutboundQueue:Sqs:AccessKey"] ?? config["AWS_ACCESS_KEY_ID"];
            var secret = config["OutboundQueue:Sqs:SecretKey"] ?? config["AWS_SECRET_ACCESS_KEY"];
            if (!string.IsNullOrWhiteSpace(access) && !string.IsNullOrWhiteSpace(secret))
            {
                return new AmazonSQSClient(new BasicAWSCredentials(access, secret), sqsConfig);
            }
            return new AmazonSQSClient(sqsConfig);
        }
        catch
        {
            return null;
        }
    });

    public string ActiveProvider
    {
        get
        {
            if (_provider == "redis" && !string.IsNullOrWhiteSpace(_redisConn) && _redis.Value is not null) return "redis";
            if (_provider == "rabbitmq" && !string.IsNullOrWhiteSpace(_rabbitConn) && _rabbitConnLazy.Value is not null) return "rabbitmq";
            if (_provider == "sqs" && !string.IsNullOrWhiteSpace(_sqsQueueUrl) && _sqs.Value is not null) return "sqs";
            if (_provider is "rabbitmq" or "sqs") logger.LogWarning("Outbound queue provider {Provider} unavailable; falling back to memory.", _provider);
            return "memory";
        }
    }

    private static IConnection? BuildRabbitConnection(string connectionString, string vhost, string user, string pass, bool useTls)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)) return null;
        var factory = new ConnectionFactory
        {
            Uri = uri,
            DispatchConsumersAsync = false,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
        if (!string.IsNullOrWhiteSpace(vhost)) factory.VirtualHost = vhost;
        if (!string.IsNullOrWhiteSpace(user)) factory.UserName = user;
        if (!string.IsNullOrWhiteSpace(pass)) factory.Password = pass;
        if (useTls || string.Equals(uri.Scheme, "amqps", StringComparison.OrdinalIgnoreCase))
        {
            factory.Ssl = new SslOption
            {
                Enabled = true,
                ServerName = uri.Host
            };
        }
        return factory.CreateConnection();
    }

    public async ValueTask EnqueueAsync(OutboundMessageQueueItem item, CancellationToken ct = default)
    {
        if (ActiveProvider == "redis")
        {
            var db = _redis.Value!.GetDatabase();
            var payload = JsonSerializer.Serialize(item);
            await db.ListRightPushAsync(_redisKey, payload);
            return;
        }
        if (ActiveProvider == "rabbitmq")
        {
            using var ch = _rabbitConnLazy.Value!.CreateModel();
            ch.ExchangeDeclare(_rabbitExchange, ExchangeType.Topic, durable: true, autoDelete: false);
            var args = new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = $"{_rabbitQueue}.dead"
            };
            ch.QueueDeclare(_rabbitQueue, durable: true, exclusive: false, autoDelete: false, arguments: args);
            ch.QueueDeclare($"{_rabbitQueue}.dead", durable: true, exclusive: false, autoDelete: false);
            ch.QueueBind(_rabbitQueue, _rabbitExchange, _rabbitRoutingKey);
            var payload = JsonSerializer.Serialize(item);
            var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            var props = ch.CreateBasicProperties();
            props.Persistent = true;
            ch.BasicPublish(_rabbitExchange, _rabbitRoutingKey, props, bytes);
            return;
        }
        if (ActiveProvider == "sqs")
        {
            await _sqs.Value!.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _sqsQueueUrl,
                MessageBody = JsonSerializer.Serialize(item)
            }, ct);
            return;
        }

        Interlocked.Increment(ref _memoryDepth);
        await _memory.Writer.WriteAsync(item, ct);
    }

    public async Task<long> GetDepthAsync(CancellationToken ct = default)
    {
        if (ActiveProvider == "redis")
        {
            var db = _redis.Value!.GetDatabase();
            return await db.ListLengthAsync(_redisKey);
        }
        if (ActiveProvider == "rabbitmq")
        {
            using var ch = _rabbitConnLazy.Value!.CreateModel();
            ch.ExchangeDeclare(_rabbitExchange, ExchangeType.Topic, durable: true, autoDelete: false);
            var args = new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = $"{_rabbitQueue}.dead"
            };
            ch.QueueDeclare(_rabbitQueue, durable: true, exclusive: false, autoDelete: false, arguments: args);
            ch.QueueDeclare($"{_rabbitQueue}.dead", durable: true, exclusive: false, autoDelete: false);
            ch.QueueBind(_rabbitQueue, _rabbitExchange, _rabbitRoutingKey);
            return ch.MessageCount(_rabbitQueue);
        }
        if (ActiveProvider == "sqs")
        {
            var attrs = await _sqs.Value!.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = _sqsQueueUrl,
                AttributeNames = new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" }
            }, ct);
            var visible = attrs.ApproximateNumberOfMessages;
            var inFlight = attrs.ApproximateNumberOfMessagesNotVisible;
            return visible + inFlight;
        }
        return Math.Max(0, Interlocked.CompareExchange(ref _memoryDepth, 0, 0));
    }

    public async ValueTask<OutboundMessageQueueItem?> DequeueAsync(CancellationToken ct = default)
    {
        if (ActiveProvider == "redis")
        {
            var db = _redis.Value!.GetDatabase();
            while (!ct.IsCancellationRequested)
            {
                var popped = await db.ListLeftPopAsync(_redisKey);
                if (popped.HasValue)
                {
                    try { return JsonSerializer.Deserialize<OutboundMessageQueueItem>(popped!); } catch { return null; }
                }
                await Task.Delay(200, ct);
            }
            return null;
        }
        if (ActiveProvider == "rabbitmq")
        {
            using var ch = _rabbitConnLazy.Value!.CreateModel();
            ch.ExchangeDeclare(_rabbitExchange, ExchangeType.Topic, durable: true, autoDelete: false);
            var args = new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = $"{_rabbitQueue}.dead"
            };
            ch.QueueDeclare(_rabbitQueue, durable: true, exclusive: false, autoDelete: false, arguments: args);
            ch.QueueDeclare($"{_rabbitQueue}.dead", durable: true, exclusive: false, autoDelete: false);
            ch.QueueBind(_rabbitQueue, _rabbitExchange, _rabbitRoutingKey);
            while (!ct.IsCancellationRequested)
            {
                var result = ch.BasicGet(_rabbitQueue, autoAck: false);
                if (result is not null)
                {
                    var payload = System.Text.Encoding.UTF8.GetString(result.Body.ToArray());
                    try
                    {
                        var item = JsonSerializer.Deserialize<OutboundMessageQueueItem>(payload);
                        ch.BasicAck(result.DeliveryTag, multiple: false);
                        return item;
                    }
                    catch
                    {
                        ch.BasicNack(result.DeliveryTag, multiple: false, requeue: false);
                        return null;
                    }
                }
                await Task.Delay(200, ct);
            }
            return null;
        }
        if (ActiveProvider == "sqs")
        {
            while (!ct.IsCancellationRequested)
            {
                var received = await _sqs.Value!.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _sqsQueueUrl,
                    MaxNumberOfMessages = 1,
                    WaitTimeSeconds = 5,
                    VisibilityTimeout = 30
                }, ct);
                var msg = received.Messages.FirstOrDefault();
                if (msg is null)
                {
                    await Task.Delay(200, ct);
                    continue;
                }
                OutboundMessageQueueItem? item = null;
                try { item = JsonSerializer.Deserialize<OutboundMessageQueueItem>(msg.Body); } catch { }
                await _sqs.Value.DeleteMessageAsync(_sqsQueueUrl, msg.ReceiptHandle, ct);
                return item;
            }
            return null;
        }

        while (await _memory.Reader.WaitToReadAsync(ct))
        {
            if (_memory.Reader.TryRead(out var item))
            {
                Interlocked.Decrement(ref _memoryDepth);
                return item;
            }
        }

        return null;
    }

    public async Task PurgeAsync(CancellationToken ct = default)
    {
        if (ActiveProvider == "redis")
        {
            await _redis.Value!.GetDatabase().KeyDeleteAsync(_redisKey);
            return;
        }
        if (ActiveProvider == "rabbitmq")
        {
            using var ch = _rabbitConnLazy.Value!.CreateModel();
            ch.QueuePurge(_rabbitQueue);
            return;
        }
        if (ActiveProvider == "sqs")
        {
            await _sqs.Value!.PurgeQueueAsync(new PurgeQueueRequest { QueueUrl = _sqsQueueUrl }, ct);
            return;
        }

        while (_memory.Reader.TryRead(out _)) { }
        Interlocked.Exchange(ref _memoryDepth, 0);
    }
}
