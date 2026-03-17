import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import PlatformPurchaseReportPage from "./PlatformPurchaseReportPage";
import PlatformSecurityReportPage from "./PlatformSecurityReportPage";

const REPORT_OPTIONS = [
  { value: "purchases", label: "Purchase Report", description: "Billing usage, credits, invoices, and recharge history." },
  { value: "security", label: "Security Report", description: "Login activity, IP policies, sessions, and audit actions." },
];

export default function PlatformReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const report = searchParams.get("report") || "purchases";
  const selected = useMemo(
    () => REPORT_OPTIONS.find((option) => option.value === report) || REPORT_OPTIONS[0],
    [report],
  );

  return (
    <div className="space-y-4" data-testid="platform-reports-page">
      <div>
        <h1 className="text-2xl font-semibold text-slate-900">Platform Reports</h1>
        <p className="text-sm text-slate-500">Switch between reports and use the built-in filters per report.</p>
      </div>

      <Card className="border-slate-200">
        <CardHeader>
          <CardTitle>Report Selector</CardTitle>
          <CardDescription>{selected?.description}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2">
          <div className="space-y-2">
            <Label>Report</Label>
            <Select value={report} onValueChange={(value) => setSearchParams({ report: value })}>
              <SelectTrigger>
                <SelectValue placeholder="Select report" />
              </SelectTrigger>
              <SelectContent>
                {REPORT_OPTIONS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-slate-500">Each report includes its own time/user filters and exports.</p>
          </div>
        </CardContent>
      </Card>

      {report === "security" ? <PlatformSecurityReportPage /> : <PlatformPurchaseReportPage />}
    </div>
  );
}
