import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Checkbox } from "@/components/ui/checkbox";
import { Crown, Edit, MessageSquare, MoreVertical, Search, Shield, Trash2, UserCheck, UserCog, UserPlus } from "lucide-react";
import { toast } from "sonner";
import { apiDelete, apiGet, apiPatch, apiPost, apiPut, getSession } from "@/lib/api";

const ROLE_OPTIONS = [
  { value: "owner", label: "Owner" },
  { value: "admin", label: "Admin" },
  { value: "manager", label: "Manager" },
  { value: "support", label: "Support" },
  { value: "marketing", label: "Marketing" },
  { value: "finance", label: "Finance" }
];

const formatRole = (role) => (role || "unknown").split("_").map((x) => x[0]?.toUpperCase() + x.slice(1)).join(" ");
const initials = (name, email) => ((name || email || "NA").split(/\s+/).slice(0, 2).map((x) => x[0]?.toUpperCase()).join("") || "NA");

const roleBadge = (role) => {
  const v = (role || "").toLowerCase();
  if (v === "owner") return <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100 gap-1"><Crown className="w-3 h-3" />Owner</Badge>;
  if (v === "admin") return <Badge className="bg-purple-100 text-purple-700 hover:bg-purple-100 gap-1"><Shield className="w-3 h-3" />Admin</Badge>;
  if (v === "manager") return <Badge className="bg-blue-100 text-blue-700 hover:bg-blue-100 gap-1"><UserCog className="w-3 h-3" />Manager</Badge>;
  return <Badge className="bg-slate-100 text-slate-700 hover:bg-slate-100 gap-1"><UserCheck className="w-3 h-3" />{formatRole(role)}</Badge>;
};

export default function TeamPage() {
  const session = getSession();
  const canManage = ["owner", "admin", "super_admin"].includes((session.role || "").toLowerCase());
  const myEmail = (session.email || "").toLowerCase();

  const [members, setMembers] = useState([]);
  const [rolesCatalog, setRolesCatalog] = useState([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");

  const [inviteOpen, setInviteOpen] = useState(false);
  const [inviteName, setInviteName] = useState("");
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState("support");
  const [inviteBusy, setInviteBusy] = useState(false);

  const [editOpen, setEditOpen] = useState(false);
  const [editMember, setEditMember] = useState(null);
  const [editRole, setEditRole] = useState("support");
  const [editBusy, setEditBusy] = useState(false);

  const [activityOpen, setActivityOpen] = useState(false);
  const [activityTitle, setActivityTitle] = useState("");
  const [activityRows, setActivityRows] = useState([]);
  const [activityBusy, setActivityBusy] = useState(false);

  const [permOpen, setPermOpen] = useState(false);
  const [permBusy, setPermBusy] = useState(false);
  const [permMember, setPermMember] = useState(null);
  const [permCatalog, setPermCatalog] = useState([]);
  const [permSelected, setPermSelected] = useState(new Set());
  const [permRoleDefaults, setPermRoleDefaults] = useState(new Set());

  const filteredMembers = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return members;
    return members.filter((m) =>
      (m.name || "").toLowerCase().includes(q) ||
      (m.email || "").toLowerCase().includes(q) ||
      (m.role || "").toLowerCase().includes(q)
    );
  }, [members, search]);

  const roleStats = useMemo(() => {
    const map = new Map();
    members.forEach((m) => map.set((m.role || "unknown").toLowerCase(), (map.get((m.role || "unknown").toLowerCase()) || 0) + 1));
    return ROLE_OPTIONS.map((r) => ({ ...r, count: map.get(r.value) || 0 }));
  }, [members]);

  const load = async () => {
    setLoading(true);
    try {
      const [m, c] = await Promise.all([
        apiGet("/api/team/members"),
        apiGet("/api/permissions/catalog").catch(() => ({ roles: ROLE_OPTIONS.map((x) => x.value) }))
      ]);
      setMembers(Array.isArray(m) ? m : []);
      setRolesCatalog(Array.isArray(c?.roles) ? c.roles : ROLE_OPTIONS.map((x) => x.value));
    } catch (e) {
      toast.error(e.message || "Failed to load team.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const inviteMember = async () => {
    if (!inviteEmail.trim()) return toast.error("Email is required.");
    setInviteBusy(true);
    try {
      await apiPost("/api/team/invite", { name: inviteName, email: inviteEmail, role: inviteRole });
      toast.success("Invitation sent.");
      setInviteName("");
      setInviteEmail("");
      setInviteRole("support");
      setInviteOpen(false);
      await load();
    } catch (e) {
      toast.error(e.message || "Invite failed.");
    } finally {
      setInviteBusy(false);
    }
  };

  const openEditRole = (member) => {
    setEditMember(member);
    setEditRole((member.role || "support").toLowerCase());
    setEditOpen(true);
  };

  const saveEditRole = async () => {
    if (!editMember) return;
    setEditBusy(true);
    try {
      if (editMember.inviteOnly) {
        await apiPost("/api/team/invite", {
          name: editMember.name || "",
          email: editMember.email || "",
          role: editRole
        });
      } else {
        await apiPatch(`/api/team/members/${editMember.id}/role`, { role: editRole });
      }
      toast.success("Role updated.");
      setEditOpen(false);
      await load();
    } catch (e) {
      toast.error(e.message || "Role update failed.");
    } finally {
      setEditBusy(false);
    }
  };

  const removeMember = async (member) => {
    if (!window.confirm(`Remove ${member.name || member.email} from this project?`)) return;
    try {
      if (member.inviteOnly) {
        await apiPost("/api/team/invitations/cancel", { email: member.email });
      } else {
        await apiDelete(`/api/team/members/${member.id}`);
      }
      toast.success("Member removed.");
      await load();
    } catch (e) {
      toast.error(e.message || "Remove failed.");
    }
  };

  const resendInvite = async (member) => {
    try {
      await apiPost("/api/team/invitations/resend", { email: member.email });
      toast.success("Invitation resent.");
      await load();
    } catch (e) {
      toast.error(e.message || "Resend failed.");
    }
  };

  const openActivity = async (member) => {
    setActivityTitle(`${member.name || member.email} activity`);
    setActivityOpen(true);
    setActivityBusy(true);
    try {
      const rows = await apiGet(`/api/team/members/${member.id}/activity?limit=50`);
      setActivityRows(Array.isArray(rows) ? rows : []);
    } catch (e) {
      setActivityRows([]);
      toast.error(e.message || "Failed to load activity.");
    } finally {
      setActivityBusy(false);
    }
  };

  const openPermissions = async (member) => {
    setPermMember(member);
    setPermOpen(true);
    setPermBusy(true);
    try {
      const data = await apiGet(`/api/team/members/${member.id}/permissions`);
      setPermCatalog(Array.isArray(data?.catalog) ? data.catalog : []);
      setPermSelected(new Set(Array.isArray(data?.effectivePermissions) ? data.effectivePermissions : []));
      setPermRoleDefaults(new Set(Array.isArray(data?.rolePermissions) ? data.rolePermissions : []));
    } catch (e) {
      setPermCatalog([]);
      setPermSelected(new Set());
      setPermRoleDefaults(new Set());
      toast.error(e.message || "Failed to load permissions.");
    } finally {
      setPermBusy(false);
    }
  };

  const togglePermission = (permission, checked) => {
    setPermSelected((prev) => {
      const n = new Set(prev);
      if (checked) n.add(permission);
      else n.delete(permission);
      return n;
    });
  };

  const savePermissionOverrides = async () => {
    if (!permMember) return;
    setPermBusy(true);
    try {
      const overrides = [];
      for (const permission of permCatalog) {
        const desired = permSelected.has(permission);
        const base = permRoleDefaults.has(permission);
        if (desired === base) continue;
        overrides.push({ permission, isAllowed: desired });
      }
      await apiPut(`/api/team/members/${permMember.id}/permissions`, { overrides });
      toast.success("Permission overrides saved.");
      setPermOpen(false);
    } catch (e) {
      toast.error(e.message || "Failed to save permission overrides.");
    } finally {
      setPermBusy(false);
    }
  };

  return (
    <div className="space-y-6" data-testid="team-page">
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Team</h1>
          <p className="text-slate-600">Invite members, edit role, view activity, and manage permission overrides.</p>
        </div>
        {canManage && (
          <Dialog open={inviteOpen} onOpenChange={setInviteOpen}>
            <DialogTrigger asChild>
              <Button className="bg-orange-500 hover:bg-orange-600 text-white gap-2"><UserPlus className="w-4 h-4" />Invite Member</Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Invite Team Member</DialogTitle>
                <DialogDescription>Invitation is sent by email with secure token link.</DialogDescription>
              </DialogHeader>
              <div className="space-y-3 py-2">
                <div className="space-y-2"><Label>Name</Label><Input value={inviteName} onChange={(e) => setInviteName(e.target.value)} placeholder="Rakesh Kumar" /></div>
                <div className="space-y-2"><Label>Email</Label><Input type="email" value={inviteEmail} onChange={(e) => setInviteEmail(e.target.value)} placeholder="user@company.com" /></div>
                <div className="space-y-2">
                  <Label>Role</Label>
                  <Select value={inviteRole} onValueChange={setInviteRole}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>{ROLE_OPTIONS.filter((r) => rolesCatalog.includes(r.value)).map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
                  </Select>
                </div>
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => setInviteOpen(false)}>Cancel</Button>
                <Button className="bg-orange-500 hover:bg-orange-600" disabled={inviteBusy} onClick={inviteMember}>{inviteBusy ? "Sending..." : "Send Invite"}</Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        )}
      </div>

      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3">
        {roleStats.map((r) => (
          <Card key={r.value} className="border-slate-200"><CardContent className="pt-4"><p className="text-xs text-slate-500">{r.label}</p><p className="text-2xl font-bold text-slate-900">{r.count}</p></CardContent></Card>
        ))}
      </div>

      <Card className="border-slate-200">
        <CardHeader>
          <div className="flex items-center justify-between gap-3">
            <div><CardTitle>Team Members</CardTitle><CardDescription>{members.length} members in current project</CardDescription></div>
            <div className="relative"><Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" /><Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search name/email/role..." className="pl-10 w-72" /></div>
          </div>
        </CardHeader>
        <CardContent>
          {loading ? <div className="text-sm text-slate-500">Loading team...</div> : (
            <Table>
              <TableHeader><TableRow><TableHead>Member</TableHead><TableHead>Role</TableHead><TableHead>Status</TableHead><TableHead>Joined</TableHead><TableHead className="w-12" /></TableRow></TableHeader>
              <TableBody>
                {filteredMembers.map((member) => {
                  const self = (member.email || "").toLowerCase() === myEmail;
                  return (
                    <TableRow key={member.id}>
                      <TableCell>
                        <div className="flex items-center gap-3">
                          <Avatar className="w-10 h-10"><AvatarFallback className="bg-gradient-to-br from-orange-400 to-orange-600 text-white font-medium">{initials(member.name, member.email)}</AvatarFallback></Avatar>
                          <div><p className="font-medium text-slate-900">{member.name || member.email}</p><p className="text-sm text-slate-500">{member.email}</p></div>
                        </div>
                      </TableCell>
                      <TableCell>{roleBadge(member.role)}</TableCell>
                      <TableCell><Badge className={member.status === "pending" ? "bg-amber-100 text-amber-700 hover:bg-amber-100" : member.status === "active" ? "bg-green-100 text-green-700 hover:bg-green-100" : "bg-slate-100 text-slate-700 hover:bg-slate-100"}>{member.status || "active"}</Badge></TableCell>
                      <TableCell className="text-slate-500">{member.joinedAtUtc ? new Date(member.joinedAtUtc).toLocaleString() : (member.invitationSentAtUtc ? `Invited ${new Date(member.invitationSentAtUtc).toLocaleString()}` : "-")}</TableCell>
                      <TableCell>
                        {canManage && !self && (
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild><Button variant="ghost" size="icon"><MoreVertical className="w-4 h-4" /></Button></DropdownMenuTrigger>
                            <DropdownMenuContent align="end">
                              <DropdownMenuItem onClick={() => openEditRole(member)}><Edit className="w-4 h-4 mr-2" />Edit Role</DropdownMenuItem>
                              {member.inviteOnly
                                ? <DropdownMenuItem disabled><Shield className="w-4 h-4 mr-2" />Role & Permissions (after accept)</DropdownMenuItem>
                                : <DropdownMenuItem onClick={() => openPermissions(member)}><Shield className="w-4 h-4 mr-2" />Role & Permissions</DropdownMenuItem>}
                              {member.inviteOnly
                                ? <DropdownMenuItem disabled><MessageSquare className="w-4 h-4 mr-2" />View Activity (after accept)</DropdownMenuItem>
                                : <DropdownMenuItem onClick={() => openActivity(member)}><MessageSquare className="w-4 h-4 mr-2" />View Activity</DropdownMenuItem>}
                              {member.invitationStatus === "pending" && <DropdownMenuItem onClick={() => resendInvite(member)}><UserPlus className="w-4 h-4 mr-2" />Resend Invitation</DropdownMenuItem>}
                              <DropdownMenuSeparator />
                              <DropdownMenuItem className="text-red-600" onClick={() => removeMember(member)}>{member.inviteOnly ? <UserPlus className="w-4 h-4 mr-2" /> : <Trash2 className="w-4 h-4 mr-2" />}{member.inviteOnly ? "Cancel Invite" : "Remove"}</DropdownMenuItem>
                            </DropdownMenuContent>
                          </DropdownMenu>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent>
          <DialogHeader><DialogTitle>Edit Role</DialogTitle><DialogDescription>{editMember?.email}</DialogDescription></DialogHeader>
          <div className="space-y-2 py-2">
            <Label>Role</Label>
            <Select value={editRole} onValueChange={setEditRole}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>{ROLE_OPTIONS.filter((r) => rolesCatalog.includes(r.value)).map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
            </Select>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setEditOpen(false)}>Cancel</Button>
            <Button className="bg-orange-500 hover:bg-orange-600" disabled={editBusy} onClick={saveEditRole}>{editBusy ? "Saving..." : "Save"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={activityOpen} onOpenChange={setActivityOpen}>
        <DialogContent className="max-w-2xl">
          <DialogHeader><DialogTitle>{activityTitle}</DialogTitle><DialogDescription>Latest actions by this member in current project.</DialogDescription></DialogHeader>
          <div className="max-h-[380px] overflow-auto space-y-2 py-1">
            {activityBusy && <div className="text-sm text-slate-500">Loading activity...</div>}
            {!activityBusy && activityRows.length === 0 && <div className="text-sm text-slate-500">No activity found.</div>}
            {!activityBusy && activityRows.map((row) => <div key={row.id} className="rounded-md border border-slate-200 p-3"><div className="text-sm font-medium text-slate-900">{row.action}</div><div className="text-xs text-slate-600 mt-1">{row.details}</div><div className="text-xs text-slate-400 mt-2">{new Date(row.createdAtUtc).toLocaleString()}</div></div>)}
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={permOpen} onOpenChange={setPermOpen}>
        <DialogContent className="max-w-3xl">
          <DialogHeader><DialogTitle>Role & Permissions</DialogTitle><DialogDescription>{permMember?.email}</DialogDescription></DialogHeader>
          <div className="max-h-[460px] overflow-auto border rounded-lg p-3 space-y-2">
            {permBusy && <div className="text-sm text-slate-500">Loading permissions...</div>}
            {!permBusy && permCatalog.map((p) => (
              <label key={p} className="flex items-center justify-between rounded-md border px-3 py-2 cursor-pointer">
                <div><div className="font-medium text-sm text-slate-900">{p}</div><div className="text-xs text-slate-500">{permRoleDefaults.has(p) ? "Role default" : "Custom override allowed"}</div></div>
                <Checkbox checked={permSelected.has(p)} onCheckedChange={(v) => togglePermission(p, Boolean(v))} />
              </label>
            ))}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPermOpen(false)}>Cancel</Button>
            <Button className="bg-orange-500 hover:bg-orange-600" disabled={permBusy} onClick={savePermissionOverrides}>{permBusy ? "Saving..." : "Save Overrides"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
