using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Textzy.Api.Controllers;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.BillingTests;

public class ControllerInvoiceTests
{
    [Fact]
    public async Task InvoiceAttachmentService_BuildsPdfAttachment_FromDatabaseState()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();

        var attachment = await fixture.InvoiceAttachmentService.BuildPdfAttachmentAsync(fixture.Invoice, fixture.HttpContext.Request);

        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal($"{fixture.Invoice.InvoiceNo}.pdf", attachment.FileName);
        Assert.True(attachment.ContentBytes.Length > 100);
        var header = System.Text.Encoding.ASCII.GetString(attachment.ContentBytes, 0, Math.Min(attachment.ContentBytes.Length, 8));
        Assert.StartsWith("%PDF-1.4", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvoiceAttachmentService_RepairsIntegrityHash_WhenInvoiceIsStale()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        fixture.Invoice.IntegrityHash = "stale-hash";
        await fixture.ControlDb.SaveChangesAsync();

        var attachment = await fixture.InvoiceAttachmentService.BuildPdfAttachmentAsync(fixture.Invoice, fixture.HttpContext.Request);
        var refreshed = await fixture.ControlDb.BillingInvoices.FirstAsync(x => x.Id == fixture.Invoice.Id);

        Assert.NotEqual("stale-hash", refreshed.IntegrityHash);
        Assert.Equal("application/pdf", attachment.ContentType);
    }

    [Fact]
    public async Task BillingController_DownloadInvoice_ReturnsHtmlArtifact()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        var controller = fixture.CreateBillingController();

        var result = await controller.DownloadInvoice(fixture.Invoice.Id, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/html; charset=utf-8", file.ContentType);
        Assert.Equal($"{fixture.Invoice.InvoiceNo}.html", file.FileDownloadName);
        var html = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains(fixture.Invoice.InvoiceNo, html, StringComparison.Ordinal);
        Assert.Contains("TAX INVOICE", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformPurchasesController_SendInvoice_SendsPdfAttachment()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        var controller = fixture.CreatePlatformPurchasesController();

        var result = await controller.SendInvoice(fixture.Invoice.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var email = Assert.Single(fixture.EmailService.BillingEvents);
        Assert.Equal("Invoice INV-TEST-001", email.EventTitle);
        var attachment = Assert.Single(email.Attachments);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal("INV-TEST-001.pdf", attachment.FileName);
        Assert.True(attachment.ContentBytes.Length > 100);
    }

    [Fact]
    public async Task PaymentWebhookController_AmountMismatch_SetsAttemptStatusAndSendsAlert()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        var controller = fixture.CreatePaymentWebhookController();
        var payload = fixture.CreateSignedWebhookPayload(amountPaise: 99999, currency: "INR", status: "captured");

        fixture.HttpContext.Request.Method = "POST";
        fixture.HttpContext.Request.Path = "/api/payments/webhook/razorpay";
        fixture.HttpContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload.Body));
        fixture.HttpContext.Request.Headers["X-Razorpay-Signature"] = payload.Signature;

        var result = await controller.Receive("razorpay", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var attempt = await fixture.ControlDb.BillingPaymentAttempts.FirstAsync(x => x.Id == fixture.Attempt.Id);
        Assert.Equal("amount_mismatch", attempt.Status);
        Assert.Contains("Expected", attempt.LastError ?? string.Empty, StringComparison.Ordinal);
        var email = Assert.Single(fixture.EmailService.BillingEvents, x => x.EventTitle == "Payment amount mismatch");
        Assert.Equal("Acme Workspace", email.CompanyName);
    }

    [Fact]
    public async Task BillingController_RazorpayVerify_InvalidSignature_SetsSignatureFailed()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        var controller = fixture.CreateBillingController();

        var result = await controller.RazorpayVerify(new BillingController.RazorpayVerifyRequest
        {
            RazorpayOrderId = fixture.Attempt.OrderId,
            RazorpayPaymentId = "pay_test_001",
            RazorpaySignature = "bad-signature",
            PlanCode = "growth",
            BillingCycle = "monthly"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid payment signature.", badRequest.Value);
        var attempt = await fixture.ControlDb.BillingPaymentAttempts.FirstAsync(x => x.Id == fixture.Attempt.Id);
        Assert.Equal("signature_failed", attempt.Status);
        Assert.Equal("Razorpay signature mismatch", attempt.LastError);
        Assert.Empty(fixture.RazorpayValidator.Calls);
        var email = Assert.Single(fixture.EmailService.BillingEvents, x => x.EventTitle == "Payment verification failed");
        Assert.Equal("Acme Workspace", email.CompanyName);
    }

    [Fact]
    public async Task BillingController_RazorpayVerify_FinalValidationFailure_SetsValidationFailed()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        fixture.RazorpayValidator.NextResult = new RazorpayPaymentValidationResult(false, "Payment order mismatch.", "{\"status\":\"captured\"}");
        var controller = fixture.CreateBillingController();

        var result = await controller.RazorpayVerify(new BillingController.RazorpayVerifyRequest
        {
            RazorpayOrderId = fixture.Attempt.OrderId,
            RazorpayPaymentId = "pay_test_001",
            RazorpaySignature = fixture.CreateCheckoutSignature(fixture.Attempt.OrderId, "pay_test_001"),
            PlanCode = "growth",
            BillingCycle = "monthly"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Payment order mismatch.", badRequest.Value);
        var attempt = await fixture.ControlDb.BillingPaymentAttempts.FirstAsync(x => x.Id == fixture.Attempt.Id);
        Assert.Equal("payment_validation_failed", attempt.Status);
        Assert.Equal("Payment order mismatch.", attempt.LastError);
        Assert.Equal("{\"status\":\"captured\"}", attempt.RawResponse);
        Assert.Equal("pay_test_001", attempt.PaymentId);
        Assert.Single(fixture.RazorpayValidator.Calls);
        var email = Assert.Single(fixture.EmailService.BillingEvents, x => x.EventTitle == "Payment validation failed");
        Assert.Equal("Acme Workspace", email.CompanyName);
    }

    [Fact]
    public async Task BillingController_RazorpayVerify_Success_ActivatesSubscription_GeneratesInvoice_AndSendsEmails()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        fixture.RazorpayValidator.NextResult = new RazorpayPaymentValidationResult(true, string.Empty, "{\"status\":\"captured\",\"order_id\":\"order_test_001\",\"amount\":118000,\"currency\":\"INR\"}");
        var controller = fixture.CreateBillingController();

        var result = await controller.RazorpayVerify(new BillingController.RazorpayVerifyRequest
        {
            RazorpayOrderId = fixture.Attempt.OrderId,
            RazorpayPaymentId = "pay_test_success_001",
            RazorpaySignature = fixture.CreateCheckoutSignature(fixture.Attempt.OrderId, "pay_test_success_001"),
            PlanCode = "growth",
            BillingCycle = "monthly"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"verified\":true", body, StringComparison.Ordinal);

        var attempt = await fixture.ControlDb.BillingPaymentAttempts.FirstAsync(x => x.Id == fixture.Attempt.Id);
        Assert.Equal("paid", attempt.Status);
        Assert.Equal("pay_test_success_001", attempt.PaymentId);
        Assert.Equal("{\"status\":\"captured\",\"order_id\":\"order_test_001\",\"amount\":118000,\"currency\":\"INR\"}", attempt.RawResponse);
        Assert.Single(fixture.RazorpayValidator.Calls);

        var subscription = await fixture.ControlDb.TenantSubscriptions
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(x => x.TenantId == fixture.Attempt.TenantId);
        Assert.NotNull(subscription);
        Assert.Equal(fixture.Attempt.PlanId, subscription!.PlanId);
        Assert.Equal("active", subscription.Status);
        Assert.Equal("monthly", subscription.BillingCycle);

        var generatedInvoice = await fixture.ControlDb.BillingInvoices
            .Where(x => x.TenantId == fixture.Attempt.TenantId
                && x.ReferenceNo == fixture.Attempt.OrderId
                && x.InvoiceNo != fixture.Invoice.InvoiceNo
                && x.InvoiceNo.StartsWith("INV-"))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();
        Assert.NotNull(generatedInvoice);
        Assert.Equal("tax_invoice", generatedInvoice!.InvoiceKind);
        Assert.Equal("paid", generatedInvoice.Status);
        Assert.Equal(1180m, generatedInvoice.Total);
        Assert.False(string.IsNullOrWhiteSpace(generatedInvoice.IntegrityHash));

        var invoiceEmail = Assert.Single(fixture.EmailService.BillingEvents, x => x.EventTitle == "Invoice generated");
        var invoiceAttachment = Assert.Single(invoiceEmail.Attachments);
        Assert.Equal($"{generatedInvoice.InvoiceNo}.pdf", invoiceAttachment.FileName);
        Assert.Equal("application/pdf", invoiceAttachment.ContentType);
        Assert.True(invoiceAttachment.ContentBytes.Length > 100);

        var successEmail = Assert.Single(fixture.EmailService.BillingEvents, x => x.EventTitle == "Payment successful");
        Assert.Empty(successEmail.Attachments);

        var audit = await fixture.ControlDb.AuditLogs.FirstOrDefaultAsync(x => x.Action == "billing.razorpay.verify.success");
        Assert.NotNull(audit);
        Assert.Contains(fixture.Attempt.OrderId, audit!.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BillingController_RazorpayVerify_PaidAttempt_ReturnsAlreadyProcessed_WithoutRevalidating()
    {
        await using var fixture = await TestBillingFixture.CreateAsync();
        fixture.Attempt.Status = "paid";
        fixture.Attempt.PaidAtUtc = new DateTime(2026, 3, 9, 10, 30, 0, DateTimeKind.Utc);
        fixture.Attempt.UpdatedAtUtc = fixture.Attempt.PaidAtUtc.Value;
        await fixture.ControlDb.SaveChangesAsync();
        var controller = fixture.CreateBillingController();

        var result = await controller.RazorpayVerify(new BillingController.RazorpayVerifyRequest
        {
            RazorpayOrderId = fixture.Attempt.OrderId,
            RazorpayPaymentId = "pay_test_repeat_001",
            RazorpaySignature = "not-used-on-paid-attempt",
            PlanCode = "growth",
            BillingCycle = "monthly"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"verified\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"alreadyProcessed\":true", body, StringComparison.Ordinal);
        Assert.Empty(fixture.RazorpayValidator.Calls);

        var subscription = await fixture.ControlDb.TenantSubscriptions
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(x => x.TenantId == fixture.Attempt.TenantId);
        Assert.NotNull(subscription);
        Assert.Equal("active", subscription!.Status);

        var invoiceEmail = Assert.Single(fixture.EmailService.BillingEvents, x => x.EventTitle == "Invoice generated");
        Assert.Single(invoiceEmail.Attachments);
        Assert.DoesNotContain(fixture.EmailService.BillingEvents, x => x.EventTitle == "Payment successful");
    }

    private sealed class TestBillingFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _controlConnection;
        private readonly SqliteConnection _tenantConnection;

        private TestBillingFixture(
            SqliteConnection controlConnection,
            SqliteConnection tenantConnection,
            ControlDbContext controlDb,
            TenantDbContext tenantDb,
            InvoiceAttachmentService invoiceAttachmentService,
            SecretCryptoService crypto,
            IConfiguration config,
            HttpContext httpContext,
            AuthContext auth,
            TenancyContext tenancy,
            BillingInvoice invoice)
        {
            _controlConnection = controlConnection;
            _tenantConnection = tenantConnection;
            ControlDb = controlDb;
            TenantDb = tenantDb;
            InvoiceAttachmentService = invoiceAttachmentService;
            Crypto = crypto;
            Config = config;
            HttpContext = httpContext;
            Auth = auth;
            Tenancy = tenancy;
            Invoice = invoice;
        }

        public ControlDbContext ControlDb { get; }
        public TenantDbContext TenantDb { get; }
        public InvoiceAttachmentService InvoiceAttachmentService { get; }
        public SecretCryptoService Crypto { get; }
        public IConfiguration Config { get; }
        public HttpContext HttpContext { get; }
        public AuthContext Auth { get; }
        public TenancyContext Tenancy { get; }
        public BillingInvoice Invoice { get; }
        public BillingPaymentAttempt Attempt { get; private set; } = null!;
        public FakeEmailService EmailService { get; private set; } = new();
        public FakeRazorpayPaymentValidator RazorpayValidator { get; private set; } = new();

        public static async Task<TestBillingFixture> CreateAsync()
        {
            var controlConnection = new SqliteConnection("Data Source=:memory:");
            await controlConnection.OpenAsync();
            var controlOptions = new DbContextOptionsBuilder<ControlDbContext>()
                .UseSqlite(controlConnection)
                .Options;
            var controlDb = new ControlDbContext(controlOptions);
            await controlDb.Database.EnsureCreatedAsync();

            var tenantConnection = new SqliteConnection("Data Source=:memory:");
            await tenantConnection.OpenAsync();
            var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlite(tenantConnection)
                .Options;
            var tenantDb = new TenantDbContext(tenantOptions);
            await tenantDb.Database.EnsureCreatedAsync();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secrets:MasterKey"] = "integration-test-master-key-12345678901234567890",
                    ["PUBLIC_API_BASE_URL"] = "https://billing.textzy.test",
                    ["AllowedOrigins"] = "https://billing.textzy.test"
                })
                .Build();

            var env = new TestHostEnvironment();
            var crypto = new SecretCryptoService(config, env);
            var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var userId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

            controlDb.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Acme Workspace",
                Slug = "acme",
                DataConnectionString = "Data Source=:memory:"
            });
            controlDb.TenantCompanyProfiles.Add(new TenantCompanyProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CompanyName = "Acme Workspace",
                LegalName = "Acme Private Limited",
                BillingEmail = "accounts@acme.test",
                Address = "Bengaluru, India",
                Gstin = "29ABCDE1234F1Z5",
                Pan = "ABCDE1234F",
                TaxRatePercent = 18m,
                IsTaxExempt = false,
                IsReverseCharge = false
            });
            controlDb.PlatformSettings.AddRange(
                EncryptSetting(crypto, "platform-branding", "platformName", "Textzy"),
                EncryptSetting(crypto, "platform-branding", "legalName", "Textzy Digital Solutions Private Limited"),
                EncryptSetting(crypto, "platform-branding", "address", "Mumbai, India"),
                EncryptSetting(crypto, "platform-branding", "gstin", "27AAFCU5055K1ZO"),
                EncryptSetting(crypto, "platform-branding", "pan", "AAFCU5055K"),
                EncryptSetting(crypto, "platform-branding", "cin", "U12345MH2020PTC000001"),
                EncryptSetting(crypto, "platform-branding", "billingEmail", "billing@textzy.com"),
                EncryptSetting(crypto, "platform-branding", "billingPhone", "+91-9999999999"),
                EncryptSetting(crypto, "platform-branding", "website", "https://textzy.com"),
                EncryptSetting(crypto, "platform-branding", "invoiceFooter", "Computer generated invoice."),
                EncryptSetting(crypto, "payment-gateway", "webhookSecret", "razorpay-test-secret"),
                EncryptSetting(crypto, "payment-gateway", "mode", "test"),
                EncryptSetting(crypto, "payment-gateway", "keyId", "rzp_test_key123"),
                EncryptSetting(crypto, "payment-gateway", "keySecret", "razorpay-test-secret"));

            controlDb.Users.Add(new User
            {
                Id = userId,
                Email = "owner@acme.test",
                FullName = "Owner User",
                PasswordHash = "hash",
                PasswordSalt = "salt",
                IsActive = true
            });
            controlDb.TenantUsers.Add(new TenantUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Role = "owner",
                CreatedAtUtc = DateTime.UtcNow
            });
            controlDb.BillingPlans.Add(new BillingPlan
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Code = "growth",
                Name = "Growth",
                Currency = "INR",
                TaxMode = "exclusive",
                PricingModel = "subscription",
                PriceMonthly = 1000m,
                PriceYearly = 10000m,
                IsActive = true,
                SortOrder = 1,
                LimitsJson = "{}",
                FeaturesJson = "[]"
            });

            var invoice = new BillingInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNo = "INV-TEST-001",
                TenantId = tenantId,
                InvoiceKind = "tax_invoice",
                BillingCycle = "monthly",
                TaxMode = "exclusive",
                ReferenceNo = "order_test_001",
                Description = "Growth plan purchase",
                PeriodStartUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEndUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                Subtotal = 1000m,
                TaxAmount = 180m,
                Total = 1180m,
                Status = "paid",
                PaidAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc),
                PdfUrl = string.Empty,
                IntegrityAlgo = "SHA256",
                IntegrityHash = InvoiceIntegrityHasher.Compute(new BillingInvoice
                {
                    InvoiceNo = "INV-TEST-001",
                    TenantId = tenantId,
                    InvoiceKind = "tax_invoice",
                    BillingCycle = "monthly",
                    TaxMode = "exclusive",
                    ReferenceNo = "order_test_001",
                    Description = "Growth plan purchase",
                    PeriodStartUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    PeriodEndUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                    Subtotal = 1000m,
                    TaxAmount = 180m,
                    Total = 1180m,
                    Status = "paid",
                    PaidAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc),
                    IssuedAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc)
                }),
                IssuedAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc)
            };
            controlDb.BillingInvoices.Add(invoice);
            var attempt = new BillingPaymentAttempt
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                BillingCycle = "monthly",
                Provider = "razorpay",
                OrderId = "order_test_001",
                Amount = 1180m,
                Currency = "INR",
                Status = "created",
                NotesJson = "{\"purchaseType\":\"plan\"}",
                RawResponse = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            controlDb.BillingPaymentAttempts.Add(attempt);
            await controlDb.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("billing.textzy.test");
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
            var auth = new AuthContext();
            auth.Set(userId, tenantId, "owner@acme.test", "owner", new[] { PermissionCatalog.BillingRead, PermissionCatalog.BillingWrite }, "Owner User");
            var tenancy = new TenancyContext();
            tenancy.SetTenant(tenantId, "acme", "Data Source=:memory:");

            var invoiceAttachmentService = new InvoiceAttachmentService(controlDb, crypto, config);
            var fixture = new TestBillingFixture(controlConnection, tenantConnection, controlDb, tenantDb, invoiceAttachmentService, crypto, config, httpContext, auth, tenancy, invoice)
            {
                Attempt = attempt,
                EmailService = new FakeEmailService(),
                RazorpayValidator = new FakeRazorpayPaymentValidator()
            };
            return fixture;
        }

        public BillingController CreateBillingController()
        {
            var rbac = new RbacService(Auth);
            var billingGuard = new BillingGuardService(ControlDb);
            var audit = new AuditLogService(ControlDb, Tenancy, Auth, new HttpContextAccessor { HttpContext = HttpContext });

            var controller = new BillingController(
                ControlDb,
                TenantDb,
                Auth,
                Tenancy,
                rbac,
                billingGuard,
                Crypto,
                EmailService,
                RazorpayValidator,
                InvoiceAttachmentService,
                Config,
                new SensitiveDataRedactor(),
                audit,
                NullLogger<BillingController>.Instance);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = HttpContext
            };

            return controller;
        }

        public PlatformPurchasesController CreatePlatformPurchasesController()
        {
            var auth = new AuthContext();
            auth.Set(Auth.UserId, Auth.TenantId, Auth.Email, "super_admin", new[] { PermissionCatalog.PlatformSettingsRead, PermissionCatalog.PlatformSettingsWrite }, Auth.FullName);
            var rbac = new RbacService(auth);
            var audit = new AuditLogService(ControlDb, Tenancy, auth, new HttpContextAccessor { HttpContext = HttpContext });

            var controller = new PlatformPurchasesController(
                ControlDb,
                auth,
                rbac,
                audit,
                EmailService,
                InvoiceAttachmentService,
                Crypto,
                Config);

            controller.ControllerContext = new ControllerContext { HttpContext = HttpContext };
            return controller;
        }

        public string CreateCheckoutSignature(string orderId, string paymentId)
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("razorpay-test-secret"));
            var payload = $"{orderId}|{paymentId}";
            return Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        }

        public PaymentWebhookController CreatePaymentWebhookController()
        {
            var billingGuard = new BillingGuardService(ControlDb);
            var audit = new AuditLogService(ControlDb, Tenancy, Auth, new HttpContextAccessor { HttpContext = HttpContext });

            var controller = new PaymentWebhookController(
                ControlDb,
                Crypto,
                EmailService,
                InvoiceAttachmentService,
                billingGuard,
                audit,
                new SensitiveDataRedactor(),
                NullLogger<PaymentWebhookController>.Instance);

            controller.ControllerContext = new ControllerContext { HttpContext = HttpContext };
            return controller;
        }

        public (string Body, string Signature) CreateSignedWebhookPayload(int amountPaise, string currency, string status)
        {
            var body = $$"""
                {
                  "event": "payment.captured",
                  "payload": {
                    "payment": {
                      "entity": {
                        "id": "pay_test_001",
                        "order_id": "{{Attempt.OrderId}}",
                        "amount": {{amountPaise}},
                        "currency": "{{currency}}",
                        "status": "{{status}}"
                      }
                    }
                  }
                }
                """;
            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("razorpay-test-secret"));
            var signature = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            return (body, signature);
        }

        public async ValueTask DisposeAsync()
        {
            await ControlDb.DisposeAsync();
            await TenantDb.DisposeAsync();
            await _controlConnection.DisposeAsync();
            await _tenantConnection.DisposeAsync();
        }

        private static PlatformSetting EncryptSetting(SecretCryptoService crypto, string scope, string key, string value)
        {
            return new PlatformSetting
            {
                Id = Guid.NewGuid(),
                Scope = scope,
                Key = key,
                ValueEncrypted = crypto.Encrypt(value),
                UpdatedByUserId = Guid.Empty,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Textzy.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    public sealed class FakeEmailService : IEmailService
    {
        public List<BillingEventRecord> BillingEvents { get; } = [];

        public Task SendInviteAsync(string toEmail, string toName, string inviteUrl, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendVerificationOtpAsync(string toEmail, string displayName, string otp, string verificationCode, int expiryMinutes, string purpose, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendVerificationActionAsync(string toEmail, string displayName, string purpose, string verifyLink, int linkExpiryMinutes, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendBillingEventAsync(string toEmail, string displayName, string companyName, string eventTitle, string eventDescription, Dictionary<string, string>? details = null, CancellationToken ct = default, IReadOnlyCollection<EmailAttachment>? attachments = null)
        {
            BillingEvents.Add(new BillingEventRecord(toEmail, displayName, companyName, eventTitle, eventDescription, details ?? new Dictionary<string, string>(), attachments?.ToArray() ?? []));
            return Task.CompletedTask;
        }
    }

    public sealed record BillingEventRecord(
        string ToEmail,
        string DisplayName,
        string CompanyName,
        string EventTitle,
        string EventDescription,
        Dictionary<string, string> Details,
        EmailAttachment[] Attachments);

    public sealed class FakeRazorpayPaymentValidator : IRazorpayPaymentValidator
    {
        public List<(string PaymentId, string OrderId, decimal Amount, string Currency)> Calls { get; } = [];
        public RazorpayPaymentValidationResult NextResult { get; set; } = new(true, string.Empty, "{\"status\":\"captured\"}");

        public Task<RazorpayPaymentValidationResult> ValidateAsync(string keyId, string keySecret, string paymentId, string expectedOrderId, decimal expectedAmount, string expectedCurrency, CancellationToken ct = default)
        {
            Calls.Add((paymentId, expectedOrderId, expectedAmount, expectedCurrency));
            return Task.FromResult(NextResult);
        }
    }
}
