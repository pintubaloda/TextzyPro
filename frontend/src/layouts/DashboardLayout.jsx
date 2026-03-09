import { useCallback, useEffect, useRef, useState } from "react";
import { Outlet, Link, useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  MessageSquare,
  LayoutDashboard,
  Inbox,
  Users,
  Megaphone,
  FileText,
  Zap,
  BarChart3,
  Plug,
  CreditCard,
  Settings,
  Shield,
  ShieldAlert,
  ReceiptText,
  Palette,
  ChevronDown,
  Search,
  Bell,
  Menu,
  X,
  LogOut,
  User,
  HelpCircle,
  LifeBuoy,
  Moon,
  Sun,
  UsersRound,
  Check,
  Send,
  GitBranch,
  Smartphone,
} from "lucide-react";
import { Input } from "@/components/ui/input";
import StepUpAuthHost from "@/components/security/StepUpAuthHost";
import {
  apiGet,
  authProjects,
  authLogout,
  clearSession,
  getAppBootstrap,
  getSessionIdleTimeoutMs,
  getSessionIdleWarningMs,
  getBillingUsage,
  getCurrentBillingPlan,
  getPlatformSettings,
  getSession,
  hasPermission,
  initializeMe,
  refreshSession,
  switchProject,
} from "@/lib/api";
import { isNotificationAudioUnlocked, isNotificationSoundEnabled, unlockNotificationAudio } from "@/lib/notificationAudio";

const DashboardLayout = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [darkMode, setDarkMode] = useState(false);
  const [projects, setProjects] = useState([]);
  const [switchingProject, setSwitchingProject] = useState("");
  const [inboxUnreadCount, setInboxUnreadCount] = useState(0);
  const [ownerMode, setOwnerMode] = useState(() => {
    try {
      return localStorage.getItem("textzy_owner_mode") || "self";
    } catch {
      return "self";
    }
  });
  const [showNotificationPrompt, setShowNotificationPrompt] = useState(false);
  const [sidebarUsage, setSidebarUsage] = useState({
    whatsappUsed: 0,
    whatsappLimit: 0,
    smsUsed: 0,
    smsLimit: 0,
  });
  const [sessionPolicy, setSessionPolicy] = useState({
    idleTimeoutMs: getSessionIdleTimeoutMs(),
    warningMs: getSessionIdleWarningMs(),
  });
  const [sessionExpiryDialog, setSessionExpiryDialog] = useState({
    open: false,
    secondsLeft: 0,
    extending: false,
  });
  const lastActivityRef = useRef(Date.now());
  const idleLogoutRef = useRef(false);
  const warningShownRef = useRef(false);
  const session = getSession();
  const role = (session.role || "").toLowerCase();
  const canAccessPlatformSettings = role === "super_admin";
  const isPlatformOwner = role === "super_admin";
  const isPlatformView = isPlatformOwner && ownerMode === "platform";
  const canViewInbox = isPlatformOwner || hasPermission("inbox.read", session);
  const canViewContacts = isPlatformOwner || hasPermission("contacts.read", session);
  const canViewCampaigns = isPlatformOwner || hasPermission("campaigns.read", session);
  const canViewTemplates = isPlatformOwner || hasPermission("templates.read", session);
  const canViewAutomations = isPlatformOwner || hasPermission("automation.read", session);
  const canViewApi = isPlatformOwner || hasPermission("api.read", session);
  const canViewAnalytics = isPlatformOwner || (hasPermission("campaigns.read", session) && hasPermission("api.read", session));
  const canViewIntegrations = isPlatformOwner || hasPermission("api.write", session);
  const canViewSettings = isPlatformOwner || hasPermission("api.read", session);
  const canViewBilling = isPlatformOwner || hasPermission("billing.read", session);
  const canManageTeam = ["owner", "admin", "super_admin"].includes(role);
  const tenantHomePath = "/dashboard";
  const isSettingsPage = location.pathname.startsWith("/dashboard/settings");
  const isBrandingPage = location.pathname.startsWith("/dashboard/platform-branding");
  const isTemplatesPage = location.pathname.startsWith("/dashboard/templates");
  const isSmsSetupPage = location.pathname.startsWith("/dashboard/sms-setup");
  const isAutomationsPage = location.pathname.startsWith("/dashboard/automations");
  const currentTemplatesTab = new URLSearchParams(location.search).get("tab") || "whatsapp";
  const currentSettingsTab = new URLSearchParams(location.search).get("tab") || "profile";
  const currentPlatformTab = new URLSearchParams(location.search).get("tab") || "waba-master";
  const [platformBranding, setPlatformBranding] = useState({ platformName: "Textzy", logoUrl: "" });
  const displayedBranding = isPlatformView ? { platformName: "Textzy", logoUrl: "" } : platformBranding;
  const settingsMenus = [
    { key: "profile", label: "Profile" },
    { key: "company", label: "Company" },
    { key: "notifications", label: "Notification" },
    { key: "security", label: "Security" },
    { key: "whatsapp", label: "Waba" },
  ];

  useEffect(() => {
    let active = true;
    initializeMe();
    authProjects()
      .then(async (res) => {
        if (!active) return;
        const rows = Array.isArray(res) ? res : [];
        setProjects(rows);
        if (!rows.length) return;

        // In platform-control mode we should not force tenant switching or reloads.
        if (isPlatformView) return;

        const currentSlug = String(getSession().tenantSlug || "").trim().toLowerCase();
        const hasCurrent = !!rows.find((p) => String(p?.slug || "").trim().toLowerCase() === currentSlug);
        if (hasCurrent) return;

        const fallbackSlug = String(rows[0]?.slug || "").trim();
        if (!fallbackSlug) return;
        try {
          setSwitchingProject(fallbackSlug);
          await switchProject(fallbackSlug);
          navigate(tenantHomePath, { replace: true });
        } catch {
          if (!isPlatformOwner) {
            clearSession();
            navigate("/login", { replace: true });
          }
        } finally {
          setSwitchingProject("");
        }
      })
      .catch(() => {
        if (!active) return;
        setProjects([]);
      });
    return () => {
      active = false;
    };
  }, [navigate, isPlatformOwner, isPlatformView, tenantHomePath]);
  useEffect(() => {
    if (!session?.email) return;
    let active = true;
    getAppBootstrap()
      .then((bootstrap) => {
        if (!active) return;
        const timeoutMinutes = Number(bootstrap?.session?.idleTimeoutMinutes || 0);
        const warningSeconds = Number(bootstrap?.session?.idleWarningSeconds || 0);
        setSessionPolicy((prev) => ({
          idleTimeoutMs: Number.isFinite(timeoutMinutes) && timeoutMinutes > 0 ? timeoutMinutes * 60 * 1000 : prev.idleTimeoutMs,
          warningMs: Number.isFinite(warningSeconds) && warningSeconds > 0 ? warningSeconds * 1000 : prev.warningMs,
        }));
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [session?.email]);
  useEffect(() => {
    let active = true;
    apiGet("/api/inbox/conversations")
      .then((rows) => {
        if (!active) return;
        const unread = (rows || []).reduce((sum, x) => sum + Number(x?.unreadCount || 0), 0);
        setInboxUnreadCount(unread);
      })
      .catch(() => {
        if (!active) return;
        setInboxUnreadCount(0);
      });
    return () => {
      active = false;
    };
  }, [session.tenantSlug]);
  useEffect(() => {
    if (!session?.email) return;
    setShowNotificationPrompt(isNotificationSoundEnabled() && !isNotificationAudioUnlocked());
  }, [session?.email]);
  useEffect(() => {
    if (!isPlatformOwner) return;
    const path = location.pathname;
    const tenantOnly = [
      "/dashboard/inbox",
      "/dashboard/contacts",
      "/dashboard/campaigns",
      "/dashboard/templates",
      "/dashboard/sms-setup",
      "/dashboard/automations",
      "/dashboard/integrations",
      "/dashboard/whatsapp-onboarding",
      "/dashboard/billing",
      "/dashboard/support",
      "/dashboard/settings",
      "/dashboard/mobile-devices",
      "/dashboard/team",
    ];
    const platformOnly = [
      "/dashboard/admin",
      "/dashboard/platform-purchases",
      "/dashboard/platform-support",
      "/dashboard/platform-settings",
      "/dashboard/platform-branding",
    ];
    if (isPlatformView && tenantOnly.some((prefix) => path.startsWith(prefix))) {
      navigate("/dashboard", { replace: true });
      return;
    }
    if (!isPlatformView && platformOnly.some((prefix) => path.startsWith(prefix))) {
      navigate(tenantHomePath, { replace: true });
    }
  }, [isPlatformOwner, isPlatformView, location.pathname, navigate, tenantHomePath]);
  useEffect(() => {
    if (!isPlatformOwner) return;
    try {
      localStorage.setItem("textzy_owner_mode", ownerMode);
    } catch {}
  }, [isPlatformOwner, ownerMode]);
  useEffect(() => {
    let active = true;
    if (!canAccessPlatformSettings) return;
    (async () => {
      try {
        const res = await getPlatformSettings("platform-branding");
        if (!active) return;
        const values = res?.values || {};
        setPlatformBranding({
          platformName: String(values.platformName || "").trim() || "Textzy",
          logoUrl: String(values.logoUrl || "").trim(),
        });
      } catch {
        if (!active) return;
        setPlatformBranding({ platformName: "Textzy", logoUrl: "" });
      }
    })();
    return () => {
      active = false;
    };
  }, [canAccessPlatformSettings]);
  useEffect(() => {
    let active = true;
    if (!canViewBilling) return;
    (async () => {
      try {
        const [usageRes, planRes] = await Promise.all([
          getBillingUsage().catch(() => ({ values: {} })),
          getCurrentBillingPlan().catch(() => ({ plan: { limits: {} } })),
        ]);
        if (!active) return;
        const values = usageRes?.values || {};
        const limits = planRes?.plan?.limits || {};
        setSidebarUsage({
          whatsappUsed: Number(values.whatsappMessages || 0),
          whatsappLimit: Number(limits.whatsappMessages || 0),
          smsUsed: Number(values.smsCredits || 0),
          smsLimit: Number(limits.smsCredits || 0),
        });
      } catch {
        if (!active) return;
        setSidebarUsage({
          whatsappUsed: 0,
          whatsappLimit: 0,
          smsUsed: 0,
          smsLimit: 0,
        });
      }
    })();
    return () => {
      active = false;
    };
  }, [canViewBilling, session.tenantSlug]);

  const logoutToLogin = useCallback(async () => {
    if (idleLogoutRef.current) return;
    idleLogoutRef.current = true;
    await authLogout();
    navigate("/login", { replace: true });
  }, [navigate]);

  const handleContinueSession = useCallback(async () => {
    setSessionExpiryDialog((prev) => ({ ...prev, extending: true }));
    const ok = await refreshSession();
    if (!ok) {
      await logoutToLogin();
      return;
    }
    idleLogoutRef.current = false;
    lastActivityRef.current = Date.now();
    warningShownRef.current = false;
    setSessionExpiryDialog({ open: false, secondsLeft: 0, extending: false });
  }, [logoutToLogin]);

  useEffect(() => {
    if (!session?.email) return;

    idleLogoutRef.current = false;
    warningShownRef.current = false;
    lastActivityRef.current = Date.now();
    const idleTimeoutMs = Math.max(1000, Number(sessionPolicy.idleTimeoutMs || getSessionIdleTimeoutMs()));
    const warningMsRaw = Math.max(0, Number(sessionPolicy.warningMs || getSessionIdleWarningMs()));
    const warningMs = Math.min(warningMsRaw, Math.max(0, idleTimeoutMs - 1000));
    if (idleTimeoutMs <= 0) return;
    setSessionExpiryDialog({ open: false, secondsLeft: 0, extending: false });

    const markActivity = () => {
      lastActivityRef.current = Date.now();
    };

    const activityEvents = ["mousedown", "mousemove", "keydown", "scroll", "touchstart", "click"];
    activityEvents.forEach((eventName) => window.addEventListener(eventName, markActivity, { passive: true }));
    document.addEventListener("visibilitychange", markActivity);

    const timer = window.setInterval(() => {
      const remainingMs = idleTimeoutMs - (Date.now() - lastActivityRef.current);
      if (remainingMs <= 0) {
        logoutToLogin();
        return;
      }

      if (warningMs > 0 && remainingMs <= warningMs) {
        warningShownRef.current = true;
        setSessionExpiryDialog((prev) => ({
          open: true,
          secondsLeft: Math.max(1, Math.ceil(remainingMs / 1000)),
          extending: prev.extending,
        }));
      } else if (warningShownRef.current) {
        warningShownRef.current = false;
        setSessionExpiryDialog({ open: false, secondsLeft: 0, extending: false });
      }
    }, 1000);

    return () => {
      activityEvents.forEach((eventName) => window.removeEventListener(eventName, markActivity));
      document.removeEventListener("visibilitychange", markActivity);
      window.clearInterval(timer);
    };
  }, [logoutToLogin, session?.email, sessionPolicy.idleTimeoutMs, sessionPolicy.warningMs]);

  const sidebarPct = (used, limit) => {
    const u = Number(used || 0);
    const l = Number(limit || 0);
    if (!l) return 0;
    return Math.max(0, Math.min(100, Math.round((u / l) * 100)));
  };

  const handleProjectSwitch = async (slug) => {
    if (!slug || slug === session.tenantSlug) return;
    const isAllowed = projects.some((p) => p.slug === slug);
    if (!isAllowed) return;
    try {
      setSwitchingProject(slug);
      await switchProject(slug);
      navigate(tenantHomePath, { replace: true });
    } finally {
      setSwitchingProject("");
    }
  };

  const handleOwnerModeSwitch = (nextMode) => {
    if (!isPlatformOwner) return;
    const normalized = nextMode === "platform" ? "platform" : "self";
    try {
      localStorage.setItem("textzy_owner_mode", normalized);
    } catch {}
    setOwnerMode(normalized);
    if (normalized === "platform") {
      navigate("/dashboard", { replace: true });
    } else {
      navigate("/dashboard", { replace: true });
    }
  };

  const tenantNavigation = [
    { name: "Dashboard", href: tenantHomePath, icon: LayoutDashboard },
    canViewInbox ? { name: "Inbox", href: "/dashboard/inbox", icon: Inbox, badge: inboxUnreadCount > 0 ? String(inboxUnreadCount) : "" } : null,
    canViewContacts ? { name: "Contacts", href: "/dashboard/contacts", icon: Users } : null,
    canViewCampaigns ? { name: "Campaigns", href: "/dashboard/campaigns", icon: Megaphone } : null,
    canViewAutomations ? { name: "Automations", href: "/dashboard/automations", icon: Zap } : null,
    canViewAnalytics ? { name: "Analytics", href: "/dashboard/analytics", icon: BarChart3 } : null,
    canViewIntegrations ? { name: "Integrations", href: "/dashboard/integrations", icon: Plug } : null,
    canManageTeam ? { name: "Team", href: "/dashboard/team", icon: UsersRound } : null,
    canViewBilling ? { name: "Billing", href: "/dashboard/billing", icon: CreditCard } : null,
    { name: "Support", href: "/dashboard/support", icon: LifeBuoy },
    canViewSettings ? { name: "Settings", href: "/dashboard/settings", icon: Settings } : null,
    canViewInbox ? { name: "Mobile Devices", href: "/dashboard/mobile-devices", icon: Smartphone } : null,
  ].filter(Boolean);

  const platformNavigation = [
    { name: "Dashboard", href: "/dashboard", icon: LayoutDashboard },
    { name: "Analytics", href: "/dashboard/analytics", icon: BarChart3 },
    { name: "Admin", href: "/dashboard/admin", icon: Shield },
    { name: "Purchase Report", href: "/dashboard/platform-purchases", icon: ReceiptText },
    { name: "Support Desk", href: "/dashboard/platform-support", icon: LifeBuoy },
    { name: "Security Report", href: "/dashboard/platform-security-report", icon: ShieldAlert },
    { name: "Branding", href: "/dashboard/platform-branding", icon: Palette },
  ];

  const navigation = isPlatformView ? platformNavigation : tenantNavigation;

  const isActive = (href) => {
    if (href === "/dashboard") {
      return location.pathname === "/dashboard";
    }
    return location.pathname.startsWith(href);
  };

  const handleLogout = async () => {
    await authLogout();
    navigate("/login", { replace: true });
  };

  const handleEnableNotificationSound = async () => {
    const ok = await unlockNotificationAudio();
    if (ok) setShowNotificationPrompt(false);
  };

  return (
    <div className={`min-h-screen bg-slate-50 ${darkMode ? "dark" : ""}`} data-testid="dashboard-layout">
      {/* Top Navigation */}
      <header className="fixed top-0 left-0 right-0 z-50 h-16 bg-white border-b border-slate-200 flex items-center px-4">
        <div className="flex items-center gap-4 flex-1">
          {/* Mobile Menu Toggle */}
          <button
            className="lg:hidden p-2 rounded-lg hover:bg-slate-100"
            onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
            data-testid="mobile-sidebar-toggle"
          >
            {mobileMenuOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
          </button>

          {/* Logo */}
          <Link to="/dashboard" className="flex items-center gap-2" data-testid="dashboard-logo">
            {displayedBranding.logoUrl ? (
              <img src={displayedBranding.logoUrl} alt={displayedBranding.platformName || "Textzy"} className="w-8 h-8 rounded-lg object-cover" />
            ) : (
              <div className="w-8 h-8 bg-orange-500 rounded-lg flex items-center justify-center">
                <MessageSquare className="w-5 h-5 text-white" />
              </div>
            )}
            <span className="font-heading font-bold text-xl text-slate-900 hidden sm:block">{displayedBranding.platformName || "Textzy"}</span>
          </Link>

          {/* Sidebar Toggle */}
          <button
            className="hidden lg:flex p-2 rounded-lg hover:bg-slate-100"
            onClick={() => setSidebarOpen(!sidebarOpen)}
            data-testid="sidebar-toggle"
          >
            <Menu className="w-5 h-5 text-slate-500" />
          </button>

          {/* Search */}
          <div className="hidden md:flex flex-1 max-w-md">
            <div className="relative w-full">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <Input
                placeholder="Search contacts, campaigns..."
                className="pl-10 bg-slate-50 border-slate-200 h-9"
                data-testid="global-search-input"
              />
            </div>
          </div>

          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" className="hidden md:flex border-orange-200 bg-orange-50 text-orange-700" data-testid="project-switch-btn">
                Project: {session.projectName || session.tenantSlug || "Select"}
                <ChevronDown className="w-4 h-4 ml-2" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="w-72">
              <DropdownMenuLabel>Switch Project / Company</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {projects.map((p) => (
                <DropdownMenuItem key={p.slug} onClick={() => handleProjectSwitch(p.slug)} disabled={switchingProject === p.slug}>
                  <div className="flex w-full items-center justify-between gap-3">
                    <div className="min-w-0">
                      <p className="font-medium truncate">{p.name}</p>
                      <p className="text-xs text-slate-400 truncate">{p.slug}</p>
                    </div>
                    {p.slug === session.tenantSlug ? (
                      <span className="inline-flex items-center gap-1 text-xs px-2 py-1 rounded-full bg-green-100 text-green-700">
                        <Check className="w-3 h-3" /> Current
                      </span>
                    ) : (
                      <span className="text-xs text-slate-400">{p.role}</span>
                    )}
                  </div>
                </DropdownMenuItem>
              ))}
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={() => navigate("/projects")}>Manage Projects</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>

          {isPlatformOwner && (
            <div className="hidden lg:flex items-center rounded-lg border border-slate-200 bg-white p-1">
              <button
                className={`px-2.5 py-1 text-xs rounded-md transition-colors ${
                  !isPlatformView ? "bg-orange-50 text-orange-600 font-medium" : "text-slate-600 hover:bg-slate-50"
                }`}
                onClick={() => handleOwnerModeSwitch("self")}
                data-testid="owner-mode-self"
              >
                Self Use
              </button>
              <button
                className={`px-2.5 py-1 text-xs rounded-md transition-colors ${
                  isPlatformView ? "bg-orange-50 text-orange-600 font-medium" : "text-slate-600 hover:bg-slate-50"
                }`}
                onClick={() => handleOwnerModeSwitch("platform")}
                data-testid="owner-mode-platform"
              >
                Platform Control
              </button>
            </div>
          )}
        </div>

        <div className="flex items-center gap-2">
          {/* Dark Mode Toggle */}
          <Button
            variant="ghost"
            size="icon"
            onClick={() => setDarkMode(!darkMode)}
            className="text-slate-500"
            data-testid="dark-mode-toggle"
          >
            {darkMode ? <Sun className="w-5 h-5" /> : <Moon className="w-5 h-5" />}
          </Button>

          {/* Help */}
          <Button variant="ghost" size="icon" className="text-slate-500" data-testid="help-btn">
            <HelpCircle className="w-5 h-5" />
          </Button>

          {/* Notifications */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="relative text-slate-500" data-testid="notifications-btn">
                <Bell className="w-5 h-5" />
                <span className="absolute top-1 right-1 w-2 h-2 bg-orange-500 rounded-full"></span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-80">
              <DropdownMenuLabel>Notifications</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <div className="py-2 px-4 text-sm text-slate-500">
                <p className="font-medium text-slate-900 mb-1">Campaign "Summer Sale" delivered</p>
                <p className="text-xs">5 minutes ago</p>
              </div>
              <DropdownMenuSeparator />
              <div className="py-2 px-4 text-sm text-slate-500">
                <p className="font-medium text-slate-900 mb-1">New message from +91 98765 43210</p>
                <p className="text-xs">15 minutes ago</p>
              </div>
              <DropdownMenuSeparator />
              <div className="py-2 px-4 text-sm text-slate-500">
                <p className="font-medium text-slate-900 mb-1">Template "Order Update" approved</p>
                <p className="text-xs">1 hour ago</p>
              </div>
            </DropdownMenuContent>
          </DropdownMenu>

          {/* User Menu */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" className="flex items-center gap-2 px-2" data-testid="user-menu-btn">
                <Avatar className="w-8 h-8">
                  <AvatarImage src="" />
                  <AvatarFallback className="bg-orange-100 text-orange-600 text-sm font-medium">
                    {(session.email || "RK").slice(0, 2).toUpperCase()}
                  </AvatarFallback>
                </Avatar>
                <div className="hidden md:block text-left">
                  <p className="text-sm font-medium text-slate-900">{session.email || "User"}</p>
                  <p className="text-xs text-slate-500">{session.role || "agent"}</p>
                </div>
                <ChevronDown className="w-4 h-4 text-slate-400 hidden md:block" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-56">
              <DropdownMenuLabel>My Account</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem data-testid="profile-menu-item">
                <User className="w-4 h-4 mr-2" />
                Profile
              </DropdownMenuItem>
              {canViewSettings && (
                <DropdownMenuItem data-testid="settings-menu-item">
                  <Settings className="w-4 h-4 mr-2" />
                  Settings
                </DropdownMenuItem>
              )}
              {canViewBilling && (
                <DropdownMenuItem data-testid="billing-menu-item">
                  <CreditCard className="w-4 h-4 mr-2" />
                  Billing
                </DropdownMenuItem>
              )}
              {isPlatformOwner && (
                <>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem onClick={() => handleOwnerModeSwitch("self")}>
                    <User className="w-4 h-4 mr-2" />
                    Self Use Mode
                  </DropdownMenuItem>
                  <DropdownMenuItem onClick={() => handleOwnerModeSwitch("platform")}>
                    <Shield className="w-4 h-4 mr-2" />
                    Platform Control Mode
                  </DropdownMenuItem>
                </>
              )}
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={handleLogout} className="text-red-600" data-testid="logout-menu-item">
                <LogOut className="w-4 h-4 mr-2" />
                Log out
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </header>

      {/* Mobile Sidebar Overlay */}
      {mobileMenuOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/50 lg:hidden"
          onClick={() => setMobileMenuOpen(false)}
        />
      )}

      {/* Sidebar */}
      <aside
        className={`fixed left-0 top-16 bottom-0 z-40 bg-white border-r border-slate-200 transition-all duration-300 ${
          sidebarOpen ? "w-64" : "w-20"
        } ${mobileMenuOpen ? "translate-x-0" : "-translate-x-full lg:translate-x-0"}`}
        data-testid="sidebar"
      >
        <ScrollArea className="h-full py-4">
          <nav className="px-3 space-y-1">
            {navigation.map((item) => (
              <div key={item.name}>
                <Link
                  to={item.href}
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    isActive(item.href)
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                  data-testid={`nav-${item.name.toLowerCase()}`}
                >
                  <item.icon className={`w-5 h-5 flex-shrink-0 ${isActive(item.href) ? "text-orange-500" : ""}`} />
                  {sidebarOpen && (
                    <>
                      <span className="flex-1">{item.name}</span>
                      {item.badge && (
                        <Badge className="bg-orange-500 hover:bg-orange-500 text-white text-xs">
                          {item.badge}
                        </Badge>
                      )}
                    </>
                  )}
                </Link>
                {sidebarOpen && item.name === "Campaigns" && (
                  <div className="pt-2 pb-1">
                    <p className="px-3 pb-1 text-[11px] uppercase tracking-wide text-slate-400 font-semibold">Templates</p>
                    <Link
                      to="/dashboard/templates?tab=whatsapp"
                      className={`flex items-center gap-3 px-3 py-2 rounded-lg transition-colors ${
                        isTemplatesPage
                          ? "bg-orange-50 text-orange-600 font-medium"
                          : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                      }`}
                      onClick={() => setMobileMenuOpen(false)}
                    >
                      <FileText className="w-4 h-4 flex-shrink-0" />
                      <span className="flex-1 text-sm">Templates</span>
                    </Link>
                    <Link
                      to="/dashboard/sms-setup?panel=templates"
                      className={`flex items-center gap-3 px-3 py-2 rounded-lg transition-colors ${
                        isSmsSetupPage
                          ? "bg-orange-50 text-orange-600 font-medium"
                          : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                      }`}
                      onClick={() => setMobileMenuOpen(false)}
                    >
                      <Settings className="w-4 h-4 flex-shrink-0" />
                      <span className="flex-1 text-sm">SMS Registry</span>
                    </Link>
                  </div>
                )}
                {sidebarOpen && item.name === "Automations" && (
                  <div className="pt-2 pb-1">
                    <Link
                      to="/dashboard/automations/workflow"
                      className={`ml-4 flex items-center gap-3 px-3 py-2 rounded-lg transition-colors ${
                        isAutomationsPage && location.pathname.includes("/workflow")
                          ? "bg-orange-50 text-orange-600 font-medium"
                          : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                      }`}
                      onClick={() => setMobileMenuOpen(false)}
                    >
                      <GitBranch className="w-4 h-4 flex-shrink-0" />
                      <span className="flex-1 text-sm">Work Flow</span>
                    </Link>
                    <Link
                      to="/dashboard/automations/qa"
                      className={`ml-4 flex items-center gap-3 px-3 py-2 rounded-lg transition-colors ${
                        isAutomationsPage && location.pathname.includes("/qa")
                          ? "bg-orange-50 text-orange-600 font-medium"
                          : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                      }`}
                      onClick={() => setMobileMenuOpen(false)}
                    >
                      <HelpCircle className="w-4 h-4 flex-shrink-0" />
                      <span className="flex-1 text-sm">Q&A</span>
                    </Link>
                  </div>
                )}
              </div>
            ))}
            {canAccessPlatformSettings && sidebarOpen && isPlatformView && (
              <div className="pt-3 mt-3 border-t border-slate-200">
                <p className="px-3 pb-2 text-[11px] uppercase tracking-wide text-slate-400 font-semibold">
                  Platform Setting
                </p>
                <Link
                  to="/dashboard/platform-branding"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    isBrandingPage
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Palette className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Platform Branding</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=waba-master"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "waba-master"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Settings className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Waba Master Config</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=app-settings"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "app-settings"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Settings className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">App Base Settings</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=smtp-settings"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "smtp-settings"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Settings className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">SMTP Settings</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=sms-gateway"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "sms-gateway"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Settings className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">SMS Gateway</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=payment-gateway"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "payment-gateway"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <CreditCard className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Payment Gateway Setup</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=integration-catalog"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "integration-catalog"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Plug className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Integration Catalog</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=webhook-logs"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "webhook-logs"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Plug className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Webhook Logs</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=request-logs"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "request-logs"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <FileText className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Request Logs</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=billing-plans"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "billing-plans"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <CreditCard className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Billing Plans</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=waba-onboarding"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "waba-onboarding"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Shield className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Waba Onboarding</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=waba-lookup"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "waba-lookup"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Search className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Waba Lookup</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=waba-policies"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "waba-policies"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Shield className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Waba Error Policies</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=idempotency-diagnostics"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "idempotency-diagnostics"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Check className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Idempotency Diagnostics</span>
                </Link>
                <Link
                  to="/dashboard/platform-settings?tab=security-ops"
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                    location.pathname.startsWith("/dashboard/platform-settings") && currentPlatformTab === "security-ops"
                      ? "bg-orange-50 text-orange-600 font-medium"
                      : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                  }`}
                  onClick={() => setMobileMenuOpen(false)}
                >
                  <Shield className="w-4 h-4 flex-shrink-0" />
                  <span className="flex-1 text-sm">Security Ops</span>
                </Link>
              </div>
            )}
          </nav>

          {/* Usage Stats */}
          {sidebarOpen && canViewBilling && !isPlatformView && (
            <div className="mx-3 mt-8 p-4 bg-slate-50 rounded-xl">
              <p className="text-sm font-medium text-slate-900 mb-3">Monthly Usage</p>
              <div className="space-y-3">
                <div>
                  <div className="flex justify-between text-xs mb-1">
                    <span className="text-slate-600">WhatsApp Messages</span>
                    <span className="text-slate-900 font-medium">
                      {sidebarUsage.whatsappUsed.toLocaleString()} / {sidebarUsage.whatsappLimit > 0 ? sidebarUsage.whatsappLimit.toLocaleString() : "∞"}
                    </span>
                  </div>
                  <div className="progress-bar">
                    <div className="progress-fill" style={{ width: `${sidebarPct(sidebarUsage.whatsappUsed, sidebarUsage.whatsappLimit)}%` }}></div>
                  </div>
                </div>
                <div>
                  <div className="flex justify-between text-xs mb-1">
                    <span className="text-slate-600">SMS Credits</span>
                    <span className="text-slate-900 font-medium">
                      {sidebarUsage.smsUsed.toLocaleString()} / {sidebarUsage.smsLimit > 0 ? sidebarUsage.smsLimit.toLocaleString() : "∞"}
                    </span>
                  </div>
                  <div className="progress-bar">
                    <div className="progress-fill" style={{ width: `${sidebarPct(sidebarUsage.smsUsed, sidebarUsage.smsLimit)}%` }}></div>
                  </div>
                </div>
              </div>
              <Button variant="outline" size="sm" className="w-full mt-4" data-testid="upgrade-plan-btn">
                Upgrade Plan
              </Button>
            </div>
          )}
        </ScrollArea>
      </aside>

      {/* Main Content */}
      <main
        className={`pt-16 transition-all duration-300 ${
          sidebarOpen ? "lg:pl-64" : "lg:pl-20"
        }`}
      >
        <div className="p-6">
          {showNotificationPrompt && (
            <div className="mb-4 rounded-xl border border-orange-200 bg-orange-50 px-4 py-3 flex items-center justify-between gap-3">
              <div>
                <p className="text-sm font-medium text-slate-900">Enable notification sounds</p>
                <p className="text-xs text-slate-600">One-time click after login to allow browser audio notifications.</p>
              </div>
              <div className="flex items-center gap-2">
                <Button size="sm" className="bg-orange-500 hover:bg-orange-600 text-white" onClick={handleEnableNotificationSound}>
                  Enable Sound
                </Button>
                <Button size="sm" variant="outline" onClick={() => setShowNotificationPrompt(false)}>
                  Not now
                </Button>
              </div>
            </div>
          )}
          {isSettingsPage && (
            <div className="mb-4 h-12 bg-white border border-slate-200 rounded-xl flex items-center px-4 lg:px-6">
              <div className="flex items-center gap-2 overflow-x-auto">
                {settingsMenus.map((item) => (
                  <Link
                    key={item.key}
                    to={`/dashboard/settings?tab=${item.key}`}
                    className={`px-3 py-1.5 rounded-md text-sm whitespace-nowrap ${
                      currentSettingsTab === item.key
                        ? "bg-orange-100 text-orange-700 font-medium"
                        : "text-slate-600 hover:bg-slate-100"
                    }`}
                  >
                    {item.label}
                  </Link>
                ))}
              </div>
            </div>
          )}
          <Outlet />
        </div>
        <StepUpAuthHost />
        <Dialog open={sessionExpiryDialog.open}>
          <DialogContent className="max-w-md">
            <DialogHeader>
              <DialogTitle>Session expiring soon</DialogTitle>
              <DialogDescription>
                You will be signed out in {sessionExpiryDialog.secondsLeft} second{sessionExpiryDialog.secondsLeft === 1 ? "" : "s"} due to inactivity.
              </DialogDescription>
            </DialogHeader>
            <div className="rounded-xl border border-orange-100 bg-orange-50 px-4 py-3 text-sm text-slate-700">
              Click Continue Session to refresh your login and keep working.
            </div>
            <DialogFooter className="sm:justify-between">
              <Button variant="outline" onClick={async () => { await authLogout(); navigate("/login", { replace: true }); }}>
                Logout now
              </Button>
              <Button
                className="bg-orange-500 hover:bg-orange-600"
                disabled={sessionExpiryDialog.extending}
                onClick={handleContinueSession}
              >
                {sessionExpiryDialog.extending ? "Extending..." : "Continue Session"}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </main>
    </div>
  );
};

export default DashboardLayout;
