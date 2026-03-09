import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { authAcceptInvite, authInvitePreview, setSession } from "@/lib/api";
import { toast } from "sonner";

export default function AcceptInvitePage() {
  const [params] = useSearchParams();
  const token = useMemo(() => params.get("token") || "", [params]);
  const [name, setName] = useState("");
  const [prefillLoaded, setPrefillLoaded] = useState(false);
  const [inviteMeta, setInviteMeta] = useState({ email: "", role: "", tenantName: "" });
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    let active = true;
    if (!token) return;
    authInvitePreview({ token })
      .then((data) => {
        if (!active) return;
        if (data?.fullName) setName(String(data.fullName));
        setInviteMeta({
          email: String(data?.email || ""),
          role: String(data?.role || ""),
          tenantName: String(data?.tenantName || "")
        });
      })
      .catch(() => {})
      .finally(() => {
        if (active) setPrefillLoaded(true);
      });
    return () => {
      active = false;
    };
  }, [token]);

  const onSubmit = async (e) => {
    e.preventDefault();
    if (!token) return toast.error("Invite token missing.");
    if (!password || password.length < 8) return toast.error("Password must be at least 8 characters.");
    setBusy(true);
    try {
      const data = await authAcceptInvite({ token, fullName: name, password });
      setSession({
        tenantSlug: data.tenantSlug || "",
        projectName: data.projectName || "",
        role: data.role || ""
      });
      toast.success("Invite accepted. Welcome!");
      window.location.assign("/dashboard");
    } catch (err) {
      toast.error(err.message || "Failed to accept invite.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-50 flex items-center justify-center p-4">
      <Card className="w-full max-w-md border-slate-200">
        <CardHeader>
          <CardTitle>Accept Team Invite</CardTitle>
          <CardDescription>Set your name and password to join the project.</CardDescription>
          {inviteMeta.tenantName && <CardDescription>Project: {inviteMeta.tenantName}</CardDescription>}
          {inviteMeta.email && <CardDescription>Email: {inviteMeta.email}</CardDescription>}
          {inviteMeta.role && <CardDescription>Role: {inviteMeta.role}</CardDescription>}
        </CardHeader>
        <CardContent>
          <form className="space-y-4" onSubmit={onSubmit}>
            <div className="space-y-2">
              <Label>Full Name</Label>
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Rakesh Kumar" disabled={!prefillLoaded} />
            </div>
            <div className="space-y-2">
              <Label>Password</Label>
              <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="At least 8 characters" />
            </div>
            <Button type="submit" className="w-full bg-orange-500 hover:bg-orange-600" disabled={busy}>
              {busy ? "Accepting..." : "Accept Invite"}
            </Button>
            <Button type="button" variant="outline" className="w-full" onClick={() => navigate("/login")}>
              Go to Login
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
