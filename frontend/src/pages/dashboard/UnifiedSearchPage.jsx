import { useEffect, useMemo, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { unifiedSearch } from "@/lib/api";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";

function useQueryParam(name) {
  const location = useLocation();
  return useMemo(() => new URLSearchParams(location.search || "").get(name) || "", [location.search, name]);
}

function kindLabel(kind) {
  const k = String(kind || "").toLowerCase();
  if (k === "conversation") return "Conversation";
  if (k === "message") return "Message";
  if (k === "webhook") return "Webhook";
  if (k === "kyc") return "KYC";
  if (k === "invoice") return "Invoice";
  return k || "Result";
}

export default function UnifiedSearchPage() {
  const navigate = useNavigate();
  const q = useQueryParam("q");
  const [query, setQuery] = useState(q);
  const [busy, setBusy] = useState(false);
  const [data, setData] = useState({ results: [] });

  useEffect(() => setQuery(q), [q]);

  useEffect(() => {
    const trimmed = String(q || "").trim();
    if (trimmed.length < 2) {
      setData({ results: [] });
      return;
    }

    let cancelled = false;
    setBusy(true);
    unifiedSearch({ q: trimmed, take: 50 })
      .then((res) => {
        if (cancelled) return;
        setData(res || { results: [] });
      })
      .catch((e) => {
        if (cancelled) return;
        toast.error(e?.message || "Search failed");
        setData({ results: [] });
      })
      .finally(() => {
        if (cancelled) return;
        setBusy(false);
      });

    return () => {
      cancelled = true;
    };
  }, [q]);

  const results = Array.isArray(data?.results) ? data.results : [];

  const runSearch = () => {
    const trimmed = String(query || "").trim();
    navigate(`/dashboard/search?q=${encodeURIComponent(trimmed)}`);
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-heading font-bold text-slate-900">Unified Search</h1>
        <p className="text-slate-600">Search conversations, messages, webhooks, KYC sessions, and invoices.</p>
      </div>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Search</CardTitle>
          <CardDescription>Type at least 2 characters and press Enter.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="grid gap-2">
            <Label>Query</Label>
            <Input
              value={query}
              placeholder="Phone, wamid, invoice number, customer ref..."
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") runSearch();
              }}
            />
          </div>
          <div className="flex gap-2">
            <Button onClick={runSearch} disabled={busy}>Search</Button>
            {q ? <Button variant="outline" onClick={() => navigate("/dashboard/search")}>Clear</Button> : null}
          </div>
        </CardContent>
      </Card>

      <Card className="border-slate-200 shadow-sm">
        <CardHeader>
          <CardTitle>Results</CardTitle>
          <CardDescription>{busy ? "Searching..." : `${results.length} result(s)`}</CardDescription>
        </CardHeader>
        <CardContent>
          {results.length ? (
            <div className="divide-y divide-slate-100">
              {results.map((r) => (
                <div key={`${r.kind}-${r.id}`} className="py-3 flex items-start justify-between gap-4">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <Badge variant="outline">{kindLabel(r.kind)}</Badge>
                      <p className="font-medium text-slate-900 truncate">{r.title || "-"}</p>
                    </div>
                    <p className="text-sm text-slate-600 break-all">{r.subtitle || ""}</p>
                    {r.atUtc ? <p className="text-xs text-slate-400 mt-1">{new Date(r.atUtc).toLocaleString()}</p> : null}
                  </div>
                  {r.url ? (
                    <Button asChild variant="outline" className="shrink-0">
                      <Link to={r.url}>Open</Link>
                    </Button>
                  ) : null}
                </div>
              ))}
            </div>
          ) : (
            <div className="text-sm text-slate-600">
              {q && String(q).trim().length >= 2 ? "No matches." : "Enter a query to search."}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

