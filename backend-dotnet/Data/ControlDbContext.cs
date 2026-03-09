using Microsoft.EntityFrameworkCore;
using Textzy.Api.Models;

namespace Textzy.Api.Data;

public class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantOwnerGroup> TenantOwnerGroups => Set<TenantOwnerGroup>();
    public DbSet<TenantCompanyProfile> TenantCompanyProfiles => Set<TenantCompanyProfile>();
    public DbSet<User> Users => Set<User>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<SessionToken> SessionTokens => Set<SessionToken>();
    public DbSet<TwoFactorChallenge> TwoFactorChallenges => Set<TwoFactorChallenge>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TeamInvitation> TeamInvitations => Set<TeamInvitation>();
    public DbSet<TenantUserPermissionOverride> TenantUserPermissionOverrides => Set<TenantUserPermissionOverride>();
    public DbSet<BillingPlan> BillingPlans => Set<BillingPlan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantUsage> TenantUsages => Set<TenantUsage>();
    public DbSet<BillingInvoice> BillingInvoices => Set<BillingInvoice>();
    public DbSet<BillingPaymentAttempt> BillingPaymentAttempts => Set<BillingPaymentAttempt>();
    public DbSet<TenantUsageCreditBalance> TenantUsageCreditBalances => Set<TenantUsageCreditBalance>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<WabaErrorPolicy> WabaErrorPolicies => Set<WabaErrorPolicy>();
    public DbSet<PlatformRequestLog> PlatformRequestLogs => Set<PlatformRequestLog>();
    public DbSet<SmsGatewayRequestLog> SmsGatewayRequestLogs => Set<SmsGatewayRequestLog>();
    public DbSet<WebhookReplayGuard> WebhookReplayGuards => Set<WebhookReplayGuard>();
    public DbSet<SecuritySignal> SecuritySignals => Set<SecuritySignal>();
    public DbSet<TenantSecurityControl> TenantSecurityControls => Set<TenantSecurityControl>();
    public DbSet<UserPushSubscription> UserPushSubscriptions => Set<UserPushSubscription>();
    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();
    public DbSet<UserMobileDevice> UserMobileDevices => Set<UserMobileDevice>();
    public DbSet<TenantFeatureFlag> TenantFeatureFlags => Set<TenantFeatureFlag>();
    public DbSet<EmailOtpVerification> EmailOtpVerifications => Set<EmailOtpVerification>();
    public DbSet<MobilePairingRequest> MobilePairingRequests => Set<MobilePairingRequest>();
    public DbSet<MobileTelemetryEvent> MobileTelemetryEvents => Set<MobileTelemetryEvent>();
}
