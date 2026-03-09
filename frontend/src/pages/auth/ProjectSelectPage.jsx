import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { MessageSquare, Building2, CircleDot, UserCircle2 } from "lucide-react";
import { toast } from "sonner";
import { authProjects, createProject, getSession, switchProject } from "@/lib/api";

export default function ProjectSelectPage() {
  const navigate = useNavigate();
  const [projects, setProjects] = useState([]);
  const [name, setName] = useState("");
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [switchingSlug, setSwitchingSlug] = useState("");

  const session = getSession();

  const loadProjects = useCallback(async () => {
    setLoading(true);
    try {
      const data = await authProjects();
      setProjects(Array.isArray(data) ? data : []);
    } catch {
      toast.error("Failed to load projects");
      setProjects([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!session.email) {
      navigate("/login", { replace: true });
      return;
    }
    loadProjects();
  }, [loadProjects, navigate, session.email]);

  const slides = useMemo(() => {
    if (!projects.length) return [{ name: "Create your first project", role: "owner", slug: "first" }];
    return projects.slice(0, 3);
  }, [projects]);

  const getAccessMeta = (role) => {
    const normalized = String(role || "").toLowerCase();
    if (normalized === "super_admin") {
      return { label: "Global Access", className: "bg-amber-100 text-amber-700 border-amber-200" };
    }
    return { label: "Assigned Access", className: "bg-emerald-100 text-emerald-700 border-emerald-200" };
  };

  const onCreate = async () => {
    const next = name.trim();
    if (!next) {
      toast.error("Enter project name");
      return;
    }

    setCreating(true);
    try {
      await createProject(next);
      toast.success("Project created");
      window.location.assign("/dashboard");
    } catch (e) {
      toast.error(e.message || "Failed to create project");
    } finally {
      setCreating(false);
    }
  };

  const onView = async (slug) => {
    setSwitchingSlug(slug);
    try {
      await switchProject(slug);
      window.location.assign("/dashboard");
    } catch (e) {
      toast.error(e.message || "Failed to switch project");
    } finally {
      setSwitchingSlug("");
    }
  };

  return (
    <div className="min-h-screen bg-slate-50 relative overflow-hidden">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(251,146,60,0.18),transparent_40%),radial-gradient(circle_at_bottom_left,rgba(251,191,36,0.14),transparent_35%)]" />
      <div className="relative z-10 px-6 py-10 lg:px-16">
        <div className="max-w-7xl mx-auto grid lg:grid-cols-2 gap-8 items-center min-h-[78vh]">
          <div className="text-slate-900 space-y-6">
            <p className="text-4xl font-bold">Welcome {session.email?.split("@")[0] || "User"}..!</p>
            <h1 className="text-5xl lg:text-7xl font-heading font-bold leading-tight">Achieve Design Excellence</h1>
            <p className="text-slate-600 text-lg max-w-xl">One Business Project is associated with one WhatsApp Business API Number</p>
            <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-white px-4 py-2 text-sm text-slate-700">
              <span className={`inline-block h-2 w-2 rounded-full ${String(session.role || "").toLowerCase() === "super_admin" ? "bg-amber-500" : "bg-emerald-500"}`} />
              {String(session.role || "").toLowerCase() === "super_admin"
                ? "Platform Owner: you can access all projects"
                : "User Access: only assigned projects are visible"}
            </div>

            <div className="space-y-4 max-w-2xl">
              <div className="relative">
                <UserCircle2 className="w-5 h-5 text-slate-300 absolute left-4 top-1/2 -translate-y-1/2" />
                <Input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Enter Your Project Name"
                  className="h-14 pl-12 rounded-full border-slate-200 bg-white text-slate-900 placeholder:text-slate-400"
                />
              </div>
              <Button onClick={onCreate} disabled={creating} className="w-full h-14 rounded-full text-xl bg-orange-500 hover:bg-orange-600 text-white">
                {creating ? "Creating..." : "Create new"}
              </Button>
            </div>
          </div>

          <div className="space-y-4">
            <div className="grid md:grid-cols-2 gap-4">
              {slides.map((p, idx) => (
                <Card key={`${p.slug}-${idx}`} className={`rounded-3xl border-slate-200 ${idx === 1 ? "bg-white text-slate-900 scale-105 shadow-2xl shadow-orange-500/20" : "bg-slate-900 text-white"}`}>
                  <CardContent className="p-6 space-y-4">
                    <div className="flex items-center gap-3">
                      <div className={`w-12 h-12 rounded-xl flex items-center justify-center ${idx === 1 ? "bg-slate-100" : "bg-white/15"}`}>
                        <Building2 className={`w-6 h-6 ${idx === 1 ? "text-slate-700" : "text-white"}`} />
                      </div>
                      <div className="flex-1">
                        <div className="font-semibold text-2xl leading-tight">{p.name}</div>
                        <Badge className={`mt-2 border ${getAccessMeta(p.role).className}`}>{getAccessMeta(p.role).label}</Badge>
                      </div>
                    </div>
                    <div className={`border-t border-dashed ${idx === 1 ? "border-slate-300" : "border-white/25"}`} />
                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <p className={`text-sm ${idx === 1 ? "text-slate-500" : "text-slate-300"}`}>Status</p>
                        <p className="font-semibold">Created</p>
                      </div>
                      <div>
                        <p className={`text-sm ${idx === 1 ? "text-slate-500" : "text-slate-300"}`}>Active Plan</p>
                        <p className="font-semibold">TRIAL (pro + Flows)</p>
                      </div>
                    </div>
                    <p className={idx === 1 ? "text-slate-600" : "text-slate-200"}>Created at Feb 6, 2025</p>
                    <Button
                      onClick={() => onView(p.slug)}
                      disabled={switchingSlug === p.slug || loading || p.slug === "first"}
                      className="w-full rounded-full bg-orange-500 hover:bg-orange-600 text-white"
                    >
                      {switchingSlug === p.slug ? "Opening..." : "View"}
                    </Button>
                  </CardContent>
                </Card>
              ))}
            </div>

            <div className="flex items-center justify-center gap-2">
              {[0, 1, 2].map((x) => <CircleDot key={x} className={`w-4 h-4 ${x === 1 ? "text-orange-500" : "text-slate-300"}`} />)}
            </div>

            {!loading && !!projects.length && (
              <div className="flex flex-wrap gap-2">
                {projects.map((p) => (
                  <div key={p.slug} className="flex items-center gap-2 rounded-full bg-slate-200 px-3 py-1">
                    <Badge className="bg-transparent text-slate-700 hover:bg-transparent p-0 cursor-pointer" onClick={() => onView(p.slug)}>
                      {p.name}
                    </Badge>
                    <span className={`text-[11px] px-2 py-0.5 rounded-full border ${getAccessMeta(p.role).className}`}>{getAccessMeta(p.role).label}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
