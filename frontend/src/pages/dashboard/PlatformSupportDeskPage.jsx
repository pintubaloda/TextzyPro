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
import {
  getPlatformSupportTicketDetails,
  getPlatformSupportTickets,
  replyPlatformSupportTicket,
  updatePlatformSupportTicketStatus,
} from "@/lib/api";
import { toast } from "sonner";
import { LifeBuoy, RefreshCcw, Search, Send, TicketCheck, TicketX } from "lucide-react";

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

export default function PlatformSupportDeskPage() {
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [desk, setDesk] = useState({
    summary: {},
    items: [],
    serviceOptions: [],
    tenantOptions: [],
    page: 1,
    pageSize: 25,
    totalCount: 0,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [filters, setFilters] = useState({
    status: "",
    service: "",
    tenantId: "",
    q: "",
    page: 1,
    pageSize: 25,
  });
  const [selectedId, setSelectedId] = useState("");
  const [detail, setDetail] = useState(null);
  const [replyBody, setReplyBody] = useState("");
  const [replying, setReplying] = useState(false);
  const [statusDialog, setStatusDialog] = useState({ open: false, status: "closed", message: "" });
  const [statusSaving, setStatusSaving] = useState(false);

  const rows = Array.isArray(desk?.items) ? desk.items : [];
  const serviceOptions = Array.isArray(desk?.serviceOptions) ? desk.serviceOptions : [];
  const tenantOptions = Array.isArray(desk?.tenantOptions) ? desk.tenantOptions : [];
  const selectedTicket = detail?.ticket || null;
  const messages = Array.isArray(detail?.messages) ? detail.messages : [];

  const loadDesk = async (activeFilters = filters, preferredId = selectedId) => {
    const data = await getPlatformSupportTickets(activeFilters);
    setDesk(data || {});
    const items = Array.isArray(data?.items) ? data.items : [];
    const nextSelectedId = items.some((item) => item.id === preferredId) ? preferredId : items[0]?.id || "";
    setSelectedId(nextSelectedId);
  };

  const loadDetail = async (ticketId) => {
    if (!ticketId) {
      setDetail(null);
      return;
    }
    const data = await getPlatformSupportTicketDetails(ticketId);
    setDetail(data || null);
  };

  const refreshAll = async (preferredId = selectedId) => {
    try {
      setRefreshing(true);
      await loadDesk(filters, preferredId);
    } catch (error) {
      toast.error(error?.message || "Failed to load support desk");
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
    loadDetail(selectedId).catch((error) => {
      toast.error(error?.message || "Failed to load ticket detail");
    });
  }, [selectedId]);

  const applyFilters = async (patch = {}) => {
    try {
      setRefreshing(true);
      const next = { ...filters, ...patch, page: patch.page ?? 1 };
      setFilters(next);
      await loadDesk(next, selectedId);
    } catch (error) {
      toast.error(error?.message || "Failed to load support desk");
    } finally {
      setRefreshing(false);
    }
  };

  const handleReply = async () => {
    if (!selectedId) return;
    try {
      setReplying(true);
      const data = await replyPlatformSupportTicket(selectedId, { body: replyBody });
      setReplyBody("");
      setDetail((prev) => (prev ? { ...prev, ...data } : prev));
      await loadDesk(filters, selectedId);
      await loadDetail(selectedId);
      toast.success("Reply sent. Ticket is now waiting on customer.");
    } catch (error) {
      toast.error(error?.message || "Failed to send reply");
    } finally {
      setReplying(false);
    }
  };

  const handleStatusUpdate = async () => {
    if (!selectedId) return;
    try {
      setStatusSaving(true);
      await updatePlatformSupportTicketStatus(selectedId, {
        status: statusDialog.status,
        message: statusDialog.message,
      });
      setStatusDialog({ open: false, status: "closed", message: "" });
      await loadDesk(filters, selectedId);
      await loadDetail(selectedId);
      toast.success(`Ticket moved to ${statusDialog.status.replaceAll("_", " ")}.`);
    } catch (error) {
      toast.error(error?.message || "Failed to update ticket status");
    } finally {
      setStatusSaving(false);
    }
  };

  const queueStats = useMemo(() => ({
    total: desk?.summary?.total,
    open: desk?.summary?.open,
    waitingOnCustomer: desk?.summary?.waitingOnCustomer,
    closed: desk?.summary?.closed,
    urgent: desk?.summary?.urgent,
  }), [desk]);

  return (
    <div className="space-y-6" data-testid="platform-support-desk-page">
      <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-[radial-gradient(circle_at_top_left,_rgba(15,23,42,0.25),_transparent_28%),linear-gradient(135deg,#0f172a_0%,#1e293b_48%,#f97316_160%)] p-6 text-white shadow-sm">
        <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <Badge className="border border-white/10 bg-white/10 text-white hover:bg-white/10">Platform Owner</Badge>
            <h1 className="mt-4 text-3xl font-bold tracking-tight md:text-4xl">Advanced support desk across every customer project</h1>
            <p className="mt-3 text-sm leading-6 text-slate-200 md:text-base">
              Review all customer support tickets, reply as platform owner, close or reopen issues, and monitor open queue pressure with service and tenant filters.
            </p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Button variant="outline" className="border-white/20 bg-white/5 text-white hover:bg-white/10" onClick={() => refreshAll(selectedId)} disabled={refreshing}>
              <RefreshCcw className={`mr-2 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
              Refresh
            </Button>
          </div>
        </div>
      </section>

      <div className="grid gap-4 md:grid-cols-5">
        <SummaryTile title="Total Tickets" value={queueStats.total} hint="All platform support conversations" />
        <SummaryTile title="Open" value={queueStats.open} hint="Needs platform action now" />
        <SummaryTile title="Waiting on Customer" value={queueStats.waitingOnCustomer} hint="Platform has replied" />
        <SummaryTile title="Closed" value={queueStats.closed} hint="Resolved or archived" />
        <SummaryTile title="Urgent" value={queueStats.urgent} hint="Priority queue requiring attention" />
      </div>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Desk filters</CardTitle>
          <CardDescription>Filter by ticket status, service, tenant, or requester details.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-5">
          <div className="space-y-2">
            <Label>Status</Label>
            <Select value={filters.status || "all"} onValueChange={(value) => setFilters((prev) => ({ ...prev, status: value === "all" ? "" : value }))}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All statuses</SelectItem>
                <SelectItem value="open">Open</SelectItem>
                <SelectItem value="waiting_on_customer">Waiting on customer</SelectItem>
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
            <Label>Tenant</Label>
            <Select value={filters.tenantId || "all"} onValueChange={(value) => setFilters((prev) => ({ ...prev, tenantId: value === "all" ? "" : value }))}>
              <SelectTrigger><SelectValue placeholder="All tenants" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All tenants</SelectItem>
                {tenantOptions.map((tenant) => (
                  <SelectItem key={tenant.tenantId} value={tenant.tenantId}>{tenant.tenantName}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2 md:col-span-2">
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
                placeholder="Ticket no, company, tenant, requester"
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label>Rows</Label>
            <Select value={String(filters.pageSize)} onValueChange={(value) => setFilters((prev) => ({ ...prev, pageSize: Number(value) }))}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="10">10</SelectItem>
                <SelectItem value="25">25</SelectItem>
                <SelectItem value="50">50</SelectItem>
                <SelectItem value="100">100</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="md:col-span-4 flex flex-wrap gap-2 self-end">
            <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={() => applyFilters()} disabled={refreshing}>
              Apply Filters
            </Button>
            <Button
              variant="outline"
              onClick={() => {
                const reset = { status: "", service: "", tenantId: "", q: "", page: 1, pageSize: 25 };
                setFilters(reset);
                applyFilters(reset);
              }}
            >
              Reset
            </Button>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-6 xl:grid-cols-[0.95fr_1.35fr]">
        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Queue list</CardTitle>
            <CardDescription>
              {loading ? "Loading support tickets..." : `${Number(desk.totalCount || 0).toLocaleString()} tickets across ${Number(desk.totalPages || 1).toLocaleString()} pages`}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {rows.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-slate-200 p-8 text-center text-sm text-slate-500">
                No support tickets found for the selected filters.
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
                    <p className="mt-1 text-sm text-slate-500">{row.companyName} • {row.serviceName}</p>
                    <p className="mt-1 text-xs text-slate-400">{row.createdByName} • {row.createdByEmail}</p>
                    <p className="mt-3 text-sm text-slate-600 line-clamp-2">{row.lastMessagePreview || "No preview available"}</p>
                  </div>
                  <div className="text-right text-xs text-slate-500">
                    <p>{fmtDateTime(row.lastMessageAtUtc)}</p>
                    <p className="mt-2">{row.messageCount} replies</p>
                  </div>
                </div>
              </button>
            ))}

            <div className="flex items-center justify-between rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
              <span>Page {Number(desk.page || 1).toLocaleString()} of {Number(desk.totalPages || 1).toLocaleString()}</span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" onClick={() => applyFilters({ page: Math.max(1, Number(desk.page || 1) - 1) })} disabled={!desk.hasPreviousPage || refreshing}>
                  Previous
                </Button>
                <Button variant="outline" size="sm" onClick={() => applyFilters({ page: Number(desk.page || 1) + 1 })} disabled={!desk.hasNextPage || refreshing}>
                  Next
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card className="border-slate-200 shadow-sm">
          <CardHeader>
            <CardTitle>Ticket workspace</CardTitle>
            <CardDescription>{selectedTicket ? `${selectedTicket.ticketNo} • ${selectedTicket.companyName}` : "Select a ticket to reply or close it"}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {!selectedTicket ? (
              <div className="rounded-2xl border border-dashed border-slate-200 p-10 text-center text-sm text-slate-500">
                Select a support ticket to review the conversation and send a platform response.
              </div>
            ) : (
              <>
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Tenant</p>
                    <p className="mt-2 font-semibold text-slate-950">{selectedTicket.tenantName}</p>
                    <p className="mt-1 text-xs text-slate-500">{selectedTicket.tenantSlug}</p>
                  </div>
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Service</p>
                    <p className="mt-2 font-semibold text-slate-950">{selectedTicket.serviceName}</p>
                  </div>
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Requester</p>
                    <p className="mt-2 font-semibold text-slate-950">{selectedTicket.createdByName}</p>
                    <p className="mt-1 text-xs text-slate-500">{selectedTicket.createdByEmail}</p>
                  </div>
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                    <p className="text-xs uppercase tracking-[0.16em] text-slate-500">Status</p>
                    <div className="mt-2"><Badge className={statusTone(selectedTicket.status)}>{selectedTicket.status.replaceAll("_", " ")}</Badge></div>
                  </div>
                </div>

                <div className="flex flex-wrap gap-2">
                  <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={() => setStatusDialog({ open: true, status: "closed", message: "" })}>
                    <TicketX className="mr-2 h-4 w-4" />
                    Close Ticket
                  </Button>
                  <Button variant="outline" onClick={() => setStatusDialog({ open: true, status: "open", message: "" })}>
                    <TicketCheck className="mr-2 h-4 w-4" />
                    Reopen / Mark Open
                  </Button>
                </div>

                <ScrollArea className="h-[420px] rounded-2xl border border-slate-200 bg-slate-50/50 p-4">
                  <div className="space-y-4">
                    {messages.map((message) => {
                      const isPlatform = message.authorType === "platform";
                      return (
                        <div key={message.id} className={`flex ${isPlatform ? "justify-end" : "justify-start"}`}>
                          <div className={`max-w-[80%] rounded-3xl px-4 py-3 shadow-sm ${isPlatform ? "bg-slate-900 text-white" : "bg-white text-slate-900 border border-slate-200"}`}>
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

                <div className="rounded-2xl border border-slate-200 bg-white p-4">
                  <Label>Reply as platform owner</Label>
                  <Textarea
                    rows={5}
                    className="mt-2"
                    value={replyBody}
                    onChange={(event) => setReplyBody(event.target.value)}
                    placeholder="Share the resolution, next action, or request the exact details you need from the customer..."
                  />
                  <div className="mt-3 flex justify-end">
                    <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={handleReply} disabled={replying || !replyBody.trim()}>
                      <Send className="mr-2 h-4 w-4" />
                      {replying ? "Sending..." : "Send Reply"}
                    </Button>
                  </div>
                </div>
              </>
            )}
          </CardContent>
        </Card>
      </div>

      <Dialog open={statusDialog.open} onOpenChange={(open) => setStatusDialog((prev) => ({ ...prev, open }))}>
        <DialogContent className="max-w-xl border-slate-200">
          <DialogHeader>
            <DialogTitle>Update ticket status</DialogTitle>
            <DialogDescription>Optionally add a closing or reopening note that will appear in the customer conversation thread.</DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <div className="space-y-2">
              <Label>Status</Label>
              <Select value={statusDialog.status} onValueChange={(value) => setStatusDialog((prev) => ({ ...prev, status: value }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="open">Open</SelectItem>
                  <SelectItem value="waiting_on_customer">Waiting on customer</SelectItem>
                  <SelectItem value="closed">Closed</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Note</Label>
              <Textarea rows={5} value={statusDialog.message} onChange={(event) => setStatusDialog((prev) => ({ ...prev, message: event.target.value }))} placeholder="Optional note for the customer..." />
            </div>
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={() => setStatusDialog({ open: false, status: "closed", message: "" })}>Cancel</Button>
            <Button className="bg-orange-500 text-white hover:bg-orange-600" onClick={handleStatusUpdate} disabled={statusSaving}>
              <LifeBuoy className="mr-2 h-4 w-4" />
              {statusSaving ? "Saving..." : "Save Status"}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
