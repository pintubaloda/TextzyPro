import "@/App.css";
import { Suspense, lazy } from "react";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { Toaster } from "@/components/ui/sonner";
import { getSession, hasPermission } from "@/lib/api";

// Landing Page
const LandingPage = lazy(() => import("@/pages/LandingPage"));

// Auth Pages
const LoginPage = lazy(() => import("@/pages/auth/LoginPage"));
const ProjectSelectPage = lazy(() => import("@/pages/auth/ProjectSelectPage"));
const RegisterPage = lazy(() => import("@/pages/auth/RegisterPage"));
const ForgotPasswordPage = lazy(() => import("@/pages/auth/ForgotPasswordPage"));
const AcceptInvitePage = lazy(() => import("@/pages/auth/AcceptInvitePage"));

// Dashboard Pages
const DashboardLayout = lazy(() => import("@/layouts/DashboardLayout"));
const DashboardOverview = lazy(() => import("@/pages/dashboard/DashboardOverview"));
const InboxPage = lazy(() => import("@/pages/dashboard/InboxPage"));
const ContactsPage = lazy(() => import("@/pages/dashboard/ContactsPage"));
const CampaignsPage = lazy(() => import("@/pages/dashboard/CampaignsPage"));
const TemplatesPage = lazy(() => import("@/pages/dashboard/TemplatesPage"));
const AutomationsPage = lazy(() => import("@/pages/dashboard/AutomationsPage"));
const AnalyticsPage = lazy(() => import("@/pages/dashboard/AnalyticsPage"));
const IntegrationsPage = lazy(() => import("@/pages/dashboard/IntegrationsPage"));
const BillingPage = lazy(() => import("@/pages/dashboard/BillingPage"));
const SettingsPage = lazy(() => import("@/pages/dashboard/SettingsPage"));
const AdminPage = lazy(() => import("@/pages/dashboard/AdminPage"));
const TeamPage = lazy(() => import("@/pages/dashboard/TeamPage"));
const PlatformSettingsPage = lazy(() => import("@/pages/dashboard/PlatformSettingsPage"));
const PlatformBrandingPage = lazy(() => import("@/pages/dashboard/PlatformBrandingPage"));
const PlatformOwnerDashboard = lazy(() => import("@/pages/dashboard/PlatformOwnerDashboard"));
const SmsSetupPage = lazy(() => import("@/pages/dashboard/SmsSetupPage"));
const MobileDevicesPage = lazy(() => import("@/pages/dashboard/MobileDevicesPage"));
const WhatsAppOnboardingPage = lazy(() => import("@/pages/dashboard/WhatsAppOnboardingPage"));

function App() {
  const session = getSession();
  const authed = !!session.email;
  const isPlatformOwner = (session.role || "").toLowerCase() === "super_admin";
  const ownerMode = (() => {
    if (!isPlatformOwner) return "self";
    try {
      return localStorage.getItem("textzy_owner_mode") || "self";
    } catch {
      return "self";
    }
  })();
  const isPlatformView = isPlatformOwner && ownerMode === "platform";
  const can = (permission) => isPlatformOwner || hasPermission(permission, session);
  const canAnalytics = isPlatformOwner || (hasPermission("campaigns.read", session) && hasPermission("api.read", session));
  const canIntegrations = isPlatformOwner || hasPermission("api.write", session);
  const canSettings = isPlatformOwner || hasPermission("api.read", session);
  const canManageTeam = ["owner", "admin", "super_admin"].includes((session.role || "").toLowerCase());
  const firstTenantPath = can("inbox.read")
    ? "/dashboard/inbox"
    : can("contacts.read")
      ? "/dashboard/contacts"
      : can("campaigns.read")
        ? "/dashboard/campaigns"
        : can("automation.read")
          ? "/dashboard/automations"
          : can("billing.read")
            ? "/dashboard/billing"
            : can("api.read")
              ? "/dashboard/settings"
              : "/login";
  return (
    <div className="App">
      <BrowserRouter>
        <Suspense fallback={<div className="p-6 text-sm text-slate-600">Loading page…</div>}>
          <Routes>
            {/* Public Routes */}
            <Route path="/" element={<LandingPage />} />
            <Route path="/login" element={<LoginPage />} />
            <Route path="/projects" element={<ProjectSelectPage />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/forgot-password" element={<ForgotPasswordPage />} />
            <Route path="/accept-invite" element={<AcceptInvitePage />} />

            {/* Dashboard Routes */}
            <Route path="/dashboard" element={authed ? <DashboardLayout /> : <Navigate to="/login" replace />}>
              <Route index element={isPlatformView ? <PlatformOwnerDashboard /> : <DashboardOverview />} />
              <Route path="inbox" element={!isPlatformView && can("inbox.read") ? <InboxPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="contacts" element={!isPlatformView && can("contacts.read") ? <ContactsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="campaigns" element={!isPlatformView && can("campaigns.read") ? <CampaignsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="templates" element={!isPlatformView && can("templates.read") ? <TemplatesPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="sms-setup" element={!isPlatformView && can("templates.read") ? <SmsSetupPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="automations" element={!isPlatformView && can("automation.read") ? <AutomationsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="automations/workflow" element={!isPlatformView && can("automation.read") ? <AutomationsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="automations/qa" element={!isPlatformView && can("automation.read") ? <AutomationsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="analytics" element={canAnalytics ? <AnalyticsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="integrations" element={!isPlatformView && canIntegrations ? <IntegrationsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="whatsapp-onboarding" element={!isPlatformView && can("inbox.read") ? <WhatsAppOnboardingPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route
                path="billing"
                element={
                  !isPlatformView && can("billing.read")
                    ? <BillingPage />
                    : <Navigate to={isPlatformView ? "/dashboard/platform-settings?tab=billing-plans" : firstTenantPath} replace />
                }
              />
              <Route path="settings" element={!isPlatformView && canSettings ? <SettingsPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="mobile-devices" element={!isPlatformView && can("inbox.read") ? <MobileDevicesPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="team" element={!isPlatformView && canManageTeam ? <TeamPage /> : <Navigate to={isPlatformView ? "/dashboard" : firstTenantPath} replace />} />
              <Route path="admin" element={isPlatformView ? <AdminPage /> : <Navigate to={firstTenantPath} replace />} />
              <Route path="platform-settings" element={isPlatformView ? <PlatformSettingsPage /> : <Navigate to={firstTenantPath} replace />} />
              <Route path="platform-branding" element={isPlatformView ? <PlatformBrandingPage /> : <Navigate to={firstTenantPath} replace />} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Suspense>
      </BrowserRouter>
      <Toaster position="top-right" richColors />
    </div>
  );
}

export default App;
