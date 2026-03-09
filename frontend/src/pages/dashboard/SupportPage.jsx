import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import SupportContactCard from "@/components/support/SupportContactCard";
import {
  createSupportTicket,
  getSupportContext,
  getSupportTicketDetails,
  getSupportTickets,
  reopenSupportTicket,
  replySupportTicket,
} from "@/lib/api";
import { toast } from "sonner";
import { Clock3, FolderKanban, Plus, RefreshCcw, Search, Send, Ticket, Undo2 } from "lucide-react";

const fmtDateTime = (value) => {
  if (!value) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
};

const statusTone = (status) => {
  const normalized = String(status || "").toLowerCase();
  if (normalized === "open") return "bg-emerald-100 text-emerald-700";
  if (normalized === "waiting_on_customer") return "bg-amber-100 text-amber-700";
  if (normalized === "closed") return "bg-slate-200 text-slate-700";
  return "bg-slate-100 text-slate-700";
};

const priorityTone = (priority) => {
  const normalized = String(priority || "").toLowerCase();
  if (normalized === "urgent") return "bg-rose-100 text-rose-700";
  if (normalized === "high") return "bg-orange-100 text-orange-700";
  if (normalized === "low") return "bg-blue-100 text-blue-700";
  return "bg-slate-100 text-slate-700";
};

function SummaryTile({ title, value, hint }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">{title}</p>
      <p className="mt-2 text-3xl font-bold text-slate-950">{Number(value || 0).toLocaleString()}</p>
      <p className="mt-1 text-sm text-slate-500">{hint}</p>
    </div>
  );
}

export default function SupportPage() {
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [context, setContext] = useState(null);
  const [tickets, setTickets] = useState({
    summary: {},
    items: [],
    page: 1,
    pageSize: 20,
    totalCount: 0,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [filters, setFilters] = useState({
    status: "",
    service: "",
    q: "",
    page: 1,
    pageSize: 20,
  });
  const [selectedId, setSelectedId] = useState("");
  const [detail, setDetail] = useState(null);
  const [composeOpen, setComposeOpen] = useState(false);
  const [replying, setReplying] = useState(false);
  const [creating, setCreating] = useState(false);
  const [reopening, setReopening] = useState(false);
  const [composeForm, setComposeForm] = useState({
    serviceKey: "",
    serviceName: "",
    priority: "normal",
    subject: "",
    body: "",
  });
  const [replyBody, setReplyBody] = useState("");
  const [reopenBody, setReopenBody] = useState("");

  const serviceOptions = useMemo(() => (Array.isArray(context?.serviceOptions) ? context.serviceOptions : []), [context]);
  const summary = tickets?.summary || {};
  const rows = Array.isArray(tickets?.items) ? tickets.items : [];
  const selectedTicket = detail?.ticket || null;
  const messages = Array.isArray(detail?.messages) ? detail.messages : [];

  const loadContext = async () => {
    const res = await getSupportContext();
    setContext(res || null);
    setComposeForm((prev) => {
      const first = Array.isArray(res?.serviceOptions) && res.serviceOptions.length > 0 ? res.serviceOptions[0] : null;
      return {
        ...prev,
        serviceKey: prev.serviceKey || first?.key || "",
        serviceName: prev.serviceName || first?.name || "",
      };
    });
  };

  const loadTickets = async (activeFilters = filters, preferredId = selectedId) => {
    const data = await getSupportTickets(activeFilters);
    setTickets(data || { summary: {}, items: [] });
    const items = Array.isArray(data?.items) ? data.items : [];
    const nextSelectedId = items.some((item) => item.id === preferredId) ? preferredId : items[0]?.id || "";
    setSelectedId(nextSelectedId);
  };

  const loadDetail = async (ticketId) => {
    if (!ticketId) {
      setDetail(null);
      return;
    }
    const data = await getSupportTicketDetails(ticketId);
    setDetail(data || null);
  };

  const refreshAll = async (preferredId = selectedId) => {
    try {
      setRefreshing(true);
      await Promise.all([loadContext(), loadTickets(filters, preferredId)]);
    } catch (error) {
      toast.error(error?.message || "Failed to load support center");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    refreshAll("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!selectedId) {
      setDetail(null);
      return;
    }
    loadDetail(selectedId).catch((error) => {
      toast.error(error?.message || "Failed to load ticket detail");
    });
  }, [selectedId]);

  const openCompose = () => {
    const first = serviceOptions[0] || null;
    setComposeForm({
      serviceKey: first?.key || "",
      serviceName: first?.name || "",
      priority: "normal",
      subject: "",
      body: "",
    });
    setComposeOpen(true);
  };

  const applyFilters = async (patch = {}) => {
    try {
      setRefreshing(true);
      const next = { ...filters, ...patch, page: patch.page ?? 1 };
      setFilters(next);
      await loadTickets(next, selectedId);
    } catch (error) {
      toast.error(error?.message || "Failed to load tickets");
    } finally {
      setRefreshing(false);
    }
  };

  const handleCreateTicket = async () => {
    try {
      setCreating(true);
      const data = await createSupportTicket(composeForm);
      setComposeOpen(false);
      setSelectedId(data?.ticket?.id || "");
      await loadTickets(filters, data?.ticket?.id || "");
      if (data?.ticket?.id) await loadDetail(data.ticket.id);
      toast.success("Support ticket created.");
    } catch (error) {
      toast.error(error?.message || "Failed to create ticket");
    } finally {
      setCreating(false);
    }
  };

  const handleReply = async () => {
    if (!selectedId) return;
    try {
      setReplying(true);
      const data = await replySupportTicket(selectedId, { body: replyBody });
      setReplyBody("");
      setDetail(data || null);
      await loadTickets(filters, selectedId);
      toast.success("Reply sent to platform support.");
    } catch (error) {
      toast.error(error?.message || "Failed to send reply");
    } finally {
      setReplying(false);
    }
  };

  const handleReopen = async () => {
    if (!selectedId) return;
    try {
      setReopening(true);
      const data = await reopenSupportTicket(selectedId, { message: reopenBody });
      setReopenBody("");
      setDetail(data || null);
      await loadTickets(filters, selectedId);
      toast.success("Ticket reopened.");
    } catch (error) {
      toast.error(error?.message || "Failed to reopen ticket");
    } finally {
      setReopening(false);
    }
  };

  return (
    <div className="space-y-6" data-testid="support-page">
      <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-[radial-gradient(circle_at_top_left,_rgba(249,115,22,0.18),_transparent_30%),linear-gradient(135deg,#fff7ed_0%,#ffffff_40%,#f8fafc_100%)] p-6 shadow-sm">
        <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <Badge className="bg-white/80 text-orange-700 hover:bg-white/80">Tenant Support</Badge>
            <h1 className="mt-4 text-3xl font-bold tracking-tight text-slate-950 md:text-4xl">Professional support desk for your active project</h1>
            <p className="mt-3 text-sm leading-6 text-slate-600 md:text-base">
              Open structured tickets with project auto-selected, choose the affected service, track every reply, and reopen closed issues without leaving Textzy.
            </p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={openCompose}>
              <Plus className="mr-2 h-4 w-4" />
              Create Ticket
            </Button>
            <Button variant="outline" onClick={() => refreshAll(selectedId)} disabled={refreshing}>
              <RefreshCcw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
              Refresh
            </Button>
          </div>
        </div>
      </section>

      <div className="grid gap-4 md:grid-cols-4">
        <SummaryTile title="Total Tickets" value={summary.total} hint="All support requests in this project" />
        <SummaryTile title="Open" value={summary.open} hint="Awaiting platform action" />
        <SummaryTile title="Waiting on You" value={summary.waitingOnCustomer} hint="Platform replied and needs your input" />
        <SummaryTile title="Closed" value={summary.closed} hint="Resolved or archived issues" />
      </div>

      <div className="grid gap-6 2xl:grid-cols-[1.05fr_1.45fr]">
        <SupportContactCard
          support={context?.support}
          project={context?.project}
          onCreateTicket={openCompose}
          onOpenSupportDesk={() => {}}
        />

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Ticket filters</CardTitle>
            <CardDescription>Search by subject, ticket number, or service. Project is auto-bound to your current workspace.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-4">
            <div className="space-y-2">
              <Label>Status</Label>
              <Select value={filters.status || "all"} onValueChange={(value) => setFilters((prev) => ({ ...prev, status: value === "all" ? "" : value }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All statuses</SelectItem>
                  <SelectItem value="open">Open</SelectItem>
                  <SelectItem value="waiting_on_customer">Waiting on you</SelectItem>
                  <SelectItem value="closed">Closed</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Service</Label>
              <Select value={filters.service || "all"} onValueChange={(value) => setFilters((prev) => ({ ...prev, service: value === "all" ? "" : value }))}>
                <SelectTrigger><SelectValue placeholder="All services" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All services</SelectItem>
                  {serviceOptions.map((option) => (
                    <SelectItem key={option.key || option.name} value={option.key || option.name}>{option.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Search</Label>
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <Input
                  value={filters.q}
                  onChange={(event) => setFilters((prev) => ({ ...prev, q: event.target.value }))}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") applyFilters();
                  }}
                  className="pl-9"
                  placeholder="Ticket no, subject, service"
                />
              </div>
            </div>
            <div className="space-y-2">
              <Label>Rows</Label>
              <Select value={String(filters.pageSize)} onValueChange={(value) => setFilters((prev) => ({ ...prev, pageSize: Number(value) }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="10">10</SelectItem>
                  <SelectItem value="20">20</SelectItem>
                  <SelectItem value="50">50</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="md:col-span-4 flex flex-wrap gap-2">
              <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={() => applyFilters()} disabled={refreshing}>
                Apply Filters
              </Button>
              <Button
                variant="outline"
                onClick={() => {
                  const reset = { status: "", service: "", q: "", page: 1, pageSize: 20 };
                  setFilters(reset);
                  applyFilters(reset);
                }}
              >
                Reset
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[0.95fr_1.35fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Ticket queue</CardTitle>
            <CardDescription>
              {loading ? "Loading tickets..." : `${Number(tickets.totalCount || 0).toLocaleString()} tickets across ${Number(tickets.totalPages || 1).toLocaleString()} pages`}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {rows.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-slate-200 p-8 text-center text-sm text-slate-500">
                No tickets found for the current filters.
              </div>
            ) : rows.map((row) => (
              <button
                key={row.id}
                type="button"
                onClick={() => setSelectedId(row.id)}
                className={`w-full rounded-2xl border p-4 text-left transition ${
                  selectedId === row.id ? "border-orange-300 bg-orange-50/70" : "border-slate-200 bg-white hover:border-orange-200 hover:bg-orange-50/40"
                }`}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="font-semibold text-slate-950">{row.ticketNo}</p>
                      <Badge className={statusTone(row.status)}>{row.status.replaceAll("_", " ")}</Badge>
                      <Badge className={priorityTone(row.priority)}>{row.priority}</Badge>
                    </div>
                    <p className="mt-2 font-medium text-slate-900">{row.subject}</p>
                    <p className="mt-1 text-sm text-slate-500">{row.serviceName}</p>
                    <p className="mt-3 text-sm text-slate-600 line-clamp-2">{row.lastMessagePreview || "No message preview"}</p>
                  </div>
                  <div className="text-right text-xs text-slate-500">
                    <p>{fmtDateTime(row.lastMessageAtUtc)}</p>
                    <p className="mt-2">{row.messageCount} replies</p>
                  </div>
                </div>
              </button>
            ))}

            <div className="flex items-center justify-between rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
              <span>Page {Number(tickets.page || 1).toLocaleString()} of {Number(tickets.totalPages || 1).toLocaleString()}</span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" onClick={() => applyFilters({ page: Math.max(1, Number(tickets.page || 1) - 1) })} disabled={!tickets.hasPreviousPage || refreshing}>
                  Previous
                </Button>
                <Button variant="outline" size="sm" onClick={() => applyFilters({ page: Number(tickets.page || 1) + 1 })} disabled={!tickets.hasNextPage || refreshing}>
                  Next
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Conversation thread</CardTitle>
            <CardDescription>{selectedTicket ? `${selectedTicket.ticketNo} • ${selectedTicket.subject}` : "Select a ticket to view full conversation"}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {!selectedTicket ? (
              <div className="rounded-2xl border border-dashed border-slate-200 p-10 text-center text-sm text-slate-500">
                Pick a ticket from the queue to review replies and continue the conversation.
              </div>
            ) : (
              <>
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Project</p>
                    <p className="mt-2 font-semibold text-slate-950">{selectedTicket.tenantName}</p>
                  </div>
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Service</p>
                    <p className="mt-2 font-semibold text-slate-950">{selectedTicket.serviceName}</p>
                  </div>
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Status</p>
                    <div className="mt-2"><Badge className={statusTone(selectedTicket.status)}>{selectedTicket.status.replaceAll("_", " ")}</Badge></div>
                  </div>
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Updated</p>
                    <p className="mt-2 font-semibold text-slate-950">{fmtDateTime(selectedTicket.updatedAtUtc)}</p>
                  </div>
                </div>

                <ScrollArea className="h-[420px] rounded-2xl border border-slate-200 bg-slate-50/50 p-4">
                  <div className="space-y-4">
                    {messages.map((message) => {
                      const isCustomer = message.authorType === "customer";
                      return (
                        <div key={message.id} className={`flex ${isCustomer ? "justify-end" : "justify-start"}`}>
                          <div className={`max-w-[78%] rounded-3xl px-4 py-3 shadow-sm ${isCustomer ? "bg-orange-500 text-white" : "bg-white text-slate-900 border border-slate-200"}`}>
                            <div className="flex items-center gap-2 text-xs opacity-80">
                              <span>{message.authorName}</span>
                              <span>•</span>
                              <span>{fmtDateTime(message.createdAtUtc)}</span>
                            </div>
                            <p className="mt-2 whitespace-pre-line text-sm leading-6">{message.body}</p>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </ScrollArea>

                {selectedTicket.canReply ? (
                  <div className="rounded-2xl border border-slate-200 bg-white p-4">
                    <Label>Reply to platform support</Label>
                    <Textarea
                      rows={5}
                      className="mt-2"
                      value={replyBody}
                      onChange={(event) => setReplyBody(event.target.value)}
                      placeholder="Add the latest update, screenshots summary, or next action needed..."
                    />
                    <div className="mt-3 flex justify-end">
                      <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={handleReply} disabled={replying || !replyBody.trim()}>
                        <Send className="mr-2 h-4 w-4" />
                        {replying ? "Sending..." : "Send Reply"}
                      </Button>
                    </div>
                  </div>
                ) : (
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <Label>Reopen closed ticket</Label>
                    <Textarea
                      rows={4}
                      className="mt-2"
                      value={reopenBody}
                      onChange={(event) => setReopenBody(event.target.value)}
                      placeholder="Explain why this ticket should be reopened..."
                    />
                    <div className="mt-3 flex justify-end">
                      <Button variant="outline" onClick={handleReopen} disabled={reopening}>
                        <Undo2 className="mr-2 h-4 w-4" />
                        {reopening ? "Reopening..." : "Reopen Ticket"}
                      </Button>
                    </div>
                  </div>
                )}
              </>
            )}
          </CardContent>
        </Card>
      </div>

      <Dialog open={composeOpen} onOpenChange={setComposeOpen}>
        <DialogContent className="max-w-2xl border-slate-200">
          <DialogHeader>
            <DialogTitle>Create support ticket</DialogTitle>
            <DialogDescription>Project is auto-selected from your active workspace. Choose the service and describe the issue clearly.</DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-2 md:grid-cols-2">
            <div className="space-y-2">
              <Label>Project</Label>
              <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">
                <div className="flex items-center gap-2">
                  <FolderKanban className="h-4 w-4 text-slate-400" />
                  <span>{context?.project?.projectName || "Current project"}</span>
                </div>
              </div>
            </div>
            <div className="space-y-2">
              <Label>Priority</Label>
              <Select value={composeForm.priority} onValueChange={(value) => setComposeForm((prev) => ({ ...prev, priority: value }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="low">Low</SelectItem>
                  <SelectItem value="normal">Normal</SelectItem>
                  <SelectItem value="high">High</SelectItem>
                  <SelectItem value="urgent">Urgent</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2 md:col-span-2">
              <Label>Service</Label>
              <Select
                value={composeForm.serviceKey || "none"}
                onValueChange={(value) => {
                  const selected = serviceOptions.find((option) => option.key === value) || null;
                  setComposeForm((prev) => ({
                    ...prev,
                    serviceKey: value === "none" ? "" : value,
                    serviceName: selected?.name || prev.serviceName,
                  }));
                }}
              >
                <SelectTrigger><SelectValue placeholder="Select service" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Choose service</SelectItem>
                  {serviceOptions.map((option) => (
                    <SelectItem key={option.key || option.name} value={option.key || option.name}>{option.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2 md:col-span-2">
              <Label>Subject</Label>
              <Input value={composeForm.subject} onChange={(event) => setComposeForm((prev) => ({ ...prev, subject: event.target.value }))} placeholder="Example: Starter plan purchase invoice mismatch" />
            </div>
            <div className="space-y-2 md:col-span-2">
              <Label>Issue details</Label>
              <Textarea rows={6} value={composeForm.body} onChange={(event) => setComposeForm((prev) => ({ ...prev, body: event.target.value }))} placeholder="Describe the issue, expected behaviour, and any steps already taken..." />
            </div>
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={() => setComposeOpen(false)}>Cancel</Button>
            <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={handleCreateTicket} disabled={creating}>
              <Ticket className="mr-2 h-4 w-4" />
              {creating ? "Creating..." : "Create Ticket"}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
