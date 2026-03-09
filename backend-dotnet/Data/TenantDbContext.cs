using Microsoft.EntityFrameworkCore;
using Textzy.Api.Models;

namespace Textzy.Api.Data;

public class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ChatbotConfig> ChatbotConfigs => Set<ChatbotConfig>();
    public DbSet<SmsFlow> SmsFlows => Set<SmsFlow>();
    public DbSet<SmsInputField> SmsInputFields => Set<SmsInputField>();
    public DbSet<SmsSender> SmsSenders => Set<SmsSender>();
    public DbSet<SmsOptOut> SmsOptOuts => Set<SmsOptOut>();
    public DbSet<SmsBillingLedger> SmsBillingLedgers => Set<SmsBillingLedger>();
    public DbSet<TenantWabaConfig> TenantWabaConfigs => Set<TenantWabaConfig>();
    public DbSet<ConversationWindow> ConversationWindows => Set<ConversationWindow>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationNote> ConversationNotes => Set<ConversationNote>();
    public DbSet<ContactCustomField> ContactCustomFields => Set<ContactCustomField>();
    public DbSet<ContactSegment> ContactSegments => Set<ContactSegment>();
    public DbSet<BroadcastJob> BroadcastJobs => Set<BroadcastJob>();
    public DbSet<AutomationFlow> AutomationFlows => Set<AutomationFlow>();
    public DbSet<AutomationFlowVersion> AutomationFlowVersions => Set<AutomationFlowVersion>();
    public DbSet<AutomationNode> AutomationNodes => Set<AutomationNode>();
    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();
    public DbSet<AutomationApproval> AutomationApprovals => Set<AutomationApproval>();
    public DbSet<AutomationUsageCounter> AutomationUsageCounters => Set<AutomationUsageCounter>();
    public DbSet<FaqKnowledgeItem> FaqKnowledgeItems => Set<FaqKnowledgeItem>();
    public DbSet<IdempotencyKeyRecord> IdempotencyKeys => Set<IdempotencyKeyRecord>();
    public DbSet<MessageEvent> MessageEvents => Set<MessageEvent>();
    public DbSet<OutboundDeadLetter> OutboundDeadLetters => Set<OutboundDeadLetter>();
    public DbSet<TriggerEvaluationAuditRecord> TriggerEvaluationAudit => Set<TriggerEvaluationAuditRecord>();
    public DbSet<WorkflowScheduledMessage> WorkflowScheduledMessages => Set<WorkflowScheduledMessage>();
    public DbSet<FlowRuntimeEvent> FlowRuntimeEvents => Set<FlowRuntimeEvent>();
}
