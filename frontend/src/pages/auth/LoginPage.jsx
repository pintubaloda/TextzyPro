import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui/input-otp";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { MessageSquare, Eye, EyeOff, ArrowRight, Download, ShieldCheck, Smartphone } from "lucide-react";
import { toast } from "sonner";
import { authLogin, authRequestEmailOtp, authEmailOtpStatus, authVerifyEmailOtp, checkApiHealth, consumeAuthRedirectReason, getLastTenantSlug, getPublicMobileDownloadInfo, initializeMe, verifyLoginTwoFactor } from "@/lib/api";

const formatTwoFactorProvider = (value) => {
  const normalized = String(value || "authenticator")
    .trim()
    .replace(/[_-]+/g, " ")
    .toLowerCase();
  return normalized.replace(/\b\w/g, (match) => match.toUpperCase());
};

const LoginPage = () => {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [storedAuthReason] = useState(() => consumeAuthRedirectReason());
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [otpRequestBusy, setOtpRequestBusy] = useState(false);
  const [otpStatusBusy, setOtpStatusBusy] = useState(false);
  const [verifyBusy, setVerifyBusy] = useState(false);
  const [healthChecked, setHealthChecked] = useState(false);
  const [apkInfo, setApkInfo] = useState(null);
  const [otp, setOtp] = useState("");
  const [otpSent, setOtpSent] = useState(false);
  const [otpReady, setOtpReady] = useState(false);
  const [otpVerified, setOtpVerified] = useState(false);
  const [verificationId, setVerificationId] = useState("");
  const [verificationState, setVerificationState] = useState("");
  const [otpFlowStage, setOtpFlowStage] = useState("credentials"); // credentials | waiting | otp | authenticator
  const [twoFactor, setTwoFactor] = useState({ challengeToken: "", provider: "", expiresAtUtc: "", code: "", busy: false });
  const [formData, setFormData] = useState({
    email: "",
    password: "",
    rememberMe: false,
  });
  const authReason = (searchParams.get("reason") || storedAuthReason || "").trim().toLowerCase();
  const authReasonMessage = authReason === "ip_changed"
    ? "Your IP changed. Please login again."
    : authReason === "idle_timeout"
      ? "Your session expired due to inactivity. Please login again."
      : authReason === "ip_rejected"
        ? "Your current IP is not allowed. Please login again."
        : "";

  const completeLogin = async (emailVerificationId = "") => {
    const preferredTenantSlug = getLastTenantSlug();
    const loginPayload = {
      email: formData.email,
      password: formData.password,
      emailVerificationId,
      tenantSlug: preferredTenantSlug || undefined,
    };
    const loginResult = await authLogin(loginPayload);
    if (loginResult?.requiresTwoFactor) {
      setTwoFactor({
        challengeToken: loginResult.challengeToken || "",
        provider: loginResult.provider || "",
        expiresAtUtc: loginResult.expiresAtUtc || "",
        code: "",
        busy: false,
      });
      setOtpFlowStage("authenticator");
      toast.message(loginResult?.message || "Enter your authenticator code to continue.");
      return;
    }
    const me = await initializeMe();
    if (!me?.email) {
      throw new Error("Login succeeded but session/profile init failed. Check backend auth response.");
    }
    toast.success("Welcome back! Select your project...");
    navigate("/projects", { replace: true });
    setTimeout(() => {
      if (window.location.pathname !== "/projects") {
        window.location.assign("/projects");
      }
    }, 120);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (otpFlowStage === "otp" && !otpVerified) {
      toast.error("Please verify OTP first.");
      return;
    }
    if (otpFlowStage === "waiting") {
      toast.message("Waiting for email verification action.");
      return;
    }
    if (otpFlowStage === "authenticator") {
      toast.message("Enter your authenticator code to continue.");
      return;
    }
    setLoading(true);

    try {
      await completeLogin(verificationId);
      setLoading(false);
    } catch (err) {
      const msg = err?.message || "Login failed. Check email/password.";
      // Required UX: Sign In first -> waiting -> OTP after link click.
      if (msg.toLowerCase().includes("email verification required")) {
        try {
          setOtpRequestBusy(true);
          const data = await authRequestEmailOtp({ email: formData.email, purpose: "login" });
          setVerificationId(data?.verificationId || "");
          setVerificationState(data?.state || "waiting_user_action");
          setOtp("");
          setOtpSent(true);
          setOtpReady(false);
          setOtpVerified(false);
          setOtpFlowStage("waiting");
          toast.success("Please check your email and click Verify Now.");
        } catch (otpErr) {
          toast.error(
            otpErr?.message ||
            "Email verification could not start. If SMTP is not configured, ask admin to set emailVerificationMode=none temporarily."
          );
        } finally {
          setOtpRequestBusy(false);
        }
        setLoading(false);
        return;
      }
      setLoading(false);
      toast.error(msg);
    }
  };

  const checkOtpStatus = useCallback(async () => {
    if (!verificationId) return;
    setOtpStatusBusy(true);
    try {
      const data = await authEmailOtpStatus({ email: formData.email, verificationId, purpose: "login" });
      const state = data?.state || "";
      setVerificationState(state);
      const ready = state === "otp_ready" || state === "verified";
      setOtpReady(ready);
      if (ready) setOtpFlowStage("otp");
      if (state === "verified") {
        setOtpVerified(true);
      }
    } catch (err) {
      toast.error(err?.message || "Failed to check verification status.");
    } finally {
      setOtpStatusBusy(false);
    }
  }, [verificationId, formData.email]);

  useEffect(() => {
    if (!otpSent || !verificationId || otpFlowStage !== "waiting" || otpReady || otpVerified) return;
    const timer = setInterval(() => {
      checkOtpStatus();
    }, 2500);
    return () => clearInterval(timer);
  }, [otpSent, verificationId, otpFlowStage, otpReady, otpVerified, checkOtpStatus]);

  const verifyOtp = async () => {
    if (!verificationId || !otp) {
      toast.error("Enter OTP first.");
      return;
    }
    setVerifyBusy(true);
    try {
      await authVerifyEmailOtp({ email: formData.email, verificationId, otp, purpose: "login" });
      setOtpVerified(true);
      setOtpFlowStage("otp");
      await completeLogin(verificationId);
    } catch (err) {
      toast.error(err?.message || "Invalid OTP.");
    } finally {
      setVerifyBusy(false);
    }
  };

  const verifyAuthenticatorLogin = async () => {
    if (!twoFactor.challengeToken || !twoFactor.code.trim()) {
      toast.error("Enter the 6-digit authenticator code.");
      return;
    }
    setTwoFactor((prev) => ({ ...prev, busy: true }));
    try {
      await verifyLoginTwoFactor({
        challengeToken: twoFactor.challengeToken,
        code: twoFactor.code,
        tenantSlug: getLastTenantSlug() || undefined,
      });
      const me = await initializeMe();
      if (!me?.email) throw new Error("Login succeeded but session/profile init failed.");
      toast.success("Welcome back! Select your project...");
      navigate("/projects", { replace: true });
    } catch (err) {
      toast.error(err?.message || "Two-factor verification failed.");
    } finally {
      setTwoFactor((prev) => ({ ...prev, busy: false }));
    }
  };

  useEffect(() => {
    if (healthChecked) return;
    setHealthChecked(true);
    checkApiHealth().catch((err) => {
      toast.error(err?.message || "Backend health check failed. Verify API_BASE/env.js.");
    });
    getPublicMobileDownloadInfo()
      .then((data) => setApkInfo(data || null))
      .catch(() => {});
  }, [healthChecked]);

  useEffect(() => {
    if (!authReasonMessage) return;
    toast.error(authReasonMessage);
    const next = new URLSearchParams(searchParams);
    next.delete("reason");
    setSearchParams(next, { replace: true });
  }, [authReasonMessage, searchParams, setSearchParams]);

  return (
    <div className="min-h-screen bg-slate-50 flex" data-testid="login-page">
      {/* Left Side - Form */}
      <div className="flex-1 flex items-center justify-center p-8">
        <div className="w-full max-w-md">
          <Link to="/" className="flex items-center gap-2 mb-8" data-testid="login-logo">
            <div className="w-10 h-10 bg-orange-500 rounded-lg flex items-center justify-center">
              <MessageSquare className="w-6 h-6 text-white" />
            </div>
            <span className="font-heading font-bold text-2xl text-slate-900">Textzy</span>
          </Link>

          <Card className="border-slate-200 shadow-card">
            <CardHeader className="space-y-1">
              <CardTitle className="text-2xl font-heading">Welcome back</CardTitle>
              <CardDescription>
                Enter your credentials to access your account
              </CardDescription>
            </CardHeader>
            <CardContent>
              {authReasonMessage ? (
                <div className="mb-4 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                  {authReasonMessage}
                </div>
              ) : null}
              <form onSubmit={handleSubmit} className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="email">Email</Label>
                  <Input
                    id="email"
                    type="email"
                    placeholder="name@company.com"
                    value={formData.email}
                    onChange={(e) => {
                      setFormData({ ...formData, email: e.target.value });
                      setOtp("");
                      setOtpSent(false);
                      setOtpReady(false);
                      setOtpVerified(false);
                      setVerificationId("");
                      setVerificationState("");
                      setOtpFlowStage("credentials");
                      setTwoFactor({ challengeToken: "", provider: "", expiresAtUtc: "", code: "", busy: false });
                    }}
                    required
                    data-testid="login-email-input"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="password">Password</Label>
                  <div className="relative">
                    <Input
                      id="password"
                      type={showPassword ? "text" : "password"}
                      placeholder="Enter your password"
                      value={formData.password}
                      onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                      required
                      data-testid="login-password-input"
                    />
                    <button
                      type="button"
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600"
                      onClick={() => setShowPassword(!showPassword)}
                      data-testid="toggle-password-btn"
                    >
                      {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
                    </button>
                  </div>
                </div>

                <div className="flex items-center justify-between">
                  <div className="flex items-center space-x-2">
                    <Checkbox
                      id="remember"
                      checked={formData.rememberMe}
                      onCheckedChange={(checked) => setFormData({ ...formData, rememberMe: checked })}
                      data-testid="remember-me-checkbox"
                    />
                    <label htmlFor="remember" className="text-sm text-slate-600 cursor-pointer">
                      Remember me
                    </label>
                  </div>
                  <Link
                    to="/forgot-password"
                    className="text-sm text-orange-500 hover:text-orange-600 font-medium"
                    data-testid="forgot-password-link"
                  >
                    Forgot password?
                  </Link>
                </div>

                {(otpFlowStage === "waiting" || otpFlowStage === "otp" || otpFlowStage === "authenticator") ? (
                  <div className="space-y-2 rounded-md border border-slate-200 p-3">
                    <Label>{otpFlowStage === "authenticator" ? "Authenticator Verification" : "Email Verification"}</Label>
                    {otpFlowStage === "waiting" ? (
                      <>
                        <p className="text-xs text-slate-700">
                          Please check your mail. Waiting for other side to confirm.
                        </p>
                        <div className="text-xs text-slate-500">
                          {otpStatusBusy ? "Checking verification status..." : "Auto-checking verification status..."}
                        </div>
                      </>
                    ) : otpFlowStage === "otp" ? (
                      <div className="grid grid-cols-1 gap-2 sm:grid-cols-[1fr_auto]">
                        <Input
                          type="text"
                          placeholder="Enter OTP"
                          value={otp}
                          onChange={(e) => setOtp(e.target.value)}
                        />
                        <Button type="button" onClick={verifyOtp} disabled={verifyBusy || !otpSent} className="bg-orange-500 hover:bg-orange-600">
                          {verifyBusy ? "Verifying..." : "Verify"}
                        </Button>
                      </div>
                    ) : (
                      <div className="mx-auto flex w-full max-w-2xl justify-center">
                        <div className="w-full rounded-3xl border border-orange-100 bg-[linear-gradient(145deg,rgba(255,247,237,0.98),rgba(255,255,255,1))] p-5 shadow-[0_20px_60px_rgba(249,115,22,0.12)]">
                        <div className="flex flex-col items-center gap-4 text-center">
                          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-orange-500 text-white shadow-sm">
                            <ShieldCheck className="h-5 w-5" />
                          </div>
                          <div className="flex min-w-0 w-full flex-col items-center">
                            <div className="flex flex-wrap items-center justify-center gap-2">
                              <p className="text-xl font-semibold text-slate-900">Confirm sign-in</p>
                              <span className="rounded-full border border-orange-100 bg-white px-3 py-1 text-[11px] font-medium uppercase tracking-[0.14em] text-orange-600 shadow-sm">
                                {formatTwoFactorProvider(twoFactor.provider)}
                              </span>
                            </div>
                            <p className="mt-2 max-w-md text-sm leading-6 text-slate-600">
                              Use the current 6-digit code from your authenticator app to finish signing in.
                            </p>
                            <div className="mt-4 grid w-full max-w-md gap-3 rounded-2xl border border-orange-100/80 bg-white/90 p-4">
                              <div className="flex flex-col items-center justify-center gap-2">
                                <div className="flex items-center justify-center gap-2 text-sm font-medium text-slate-700">
                                  <Smartphone className="h-4 w-4 text-orange-500" />
                                  6-digit authenticator code
                                </div>
                                {twoFactor.expiresAtUtc ? (
                                  <p className="text-xs text-slate-500">
                                    Expires at {new Date(twoFactor.expiresAtUtc).toLocaleTimeString()}
                                  </p>
                                ) : null}
                              </div>
                              <div className="flex w-full justify-center">
                                <InputOTP
                                  maxLength={6}
                                  value={twoFactor.code}
                                  onChange={(value) => setTwoFactor((prev) => ({ ...prev, code: value.replace(/\D/g, "").slice(0, 6) }))}
                                  className="w-full"
                                  containerClassName="w-full justify-center"
                                >
                                  <InputOTPGroup className="grid w-full max-w-[320px] grid-cols-6 gap-2">
                                    {[0, 1, 2, 3, 4, 5].map((index) => (
                                      <InputOTPSlot
                                        key={index}
                                        index={index}
                                        className="h-11 w-full min-w-0 rounded-2xl border border-orange-200 bg-orange-50 text-base font-semibold text-slate-900 shadow-none first:rounded-2xl first:border last:rounded-2xl sm:h-12"
                                      />
                                    ))}
                                  </InputOTPGroup>
                                </InputOTP>
                              </div>
                              <Button
                                type="button"
                                onClick={verifyAuthenticatorLogin}
                                disabled={twoFactor.busy || twoFactor.code.trim().length !== 6}
                                className="mx-auto h-11 w-full bg-orange-500 px-5 text-white hover:bg-orange-600 sm:w-auto sm:min-w-[220px]"
                              >
                                {twoFactor.busy ? "Verifying..." : "Verify & Continue"}
                              </Button>
                            </div>
                          </div>
                        </div>
                        </div>
                      </div>
                    )}
                  </div>
                ) : null}

                <Button
                  type="submit"
                  className="w-full bg-orange-500 hover:bg-orange-600 text-white h-11"
                  disabled={loading || otpRequestBusy}
                  data-testid="login-submit-btn"
                >
                  {loading ? "Signing in..." : "Sign In"}
                  {!loading && <ArrowRight className="w-4 h-4 ml-2" />}
                </Button>
              </form>

              <div className="mt-6">
                <div className="relative">
                  <div className="absolute inset-0 flex items-center">
                    <span className="w-full border-t border-slate-200" />
                  </div>
                  <div className="relative flex justify-center text-sm">
                    <span className="bg-white px-2 text-slate-500">Or continue with</span>
                  </div>
                </div>

                <div className="mt-4 grid grid-cols-2 gap-4">
                  <Button variant="outline" className="h-11" data-testid="google-login-btn">
                    <svg className="w-5 h-5 mr-2" viewBox="0 0 24 24">
                      <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"/>
                      <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"/>
                      <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"/>
                      <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"/>
                    </svg>
                    Google
                  </Button>
                  <Button variant="outline" className="h-11" data-testid="microsoft-login-btn">
                    <svg className="w-5 h-5 mr-2" viewBox="0 0 24 24" fill="none">
                      <path fill="#F25022" d="M1 1h10v10H1z"/>
                      <path fill="#00A4EF" d="M1 13h10v10H1z"/>
                      <path fill="#7FBA00" d="M13 1h10v10H13z"/>
                      <path fill="#FFB900" d="M13 13h10v10H13z"/>
                    </svg>
                    Microsoft
                  </Button>
                </div>
              </div>

              <p className="mt-6 text-center text-sm text-slate-600">
                Don't have an account?{" "}
                <Link to="/register" className="text-orange-500 hover:text-orange-600 font-medium" data-testid="register-link">
                  Sign up for free
                </Link>
              </p>

              {apkInfo?.enabled && apkInfo?.apkUrl && (
                <div className="mt-4 rounded-md border border-slate-200 bg-slate-50 p-3">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="text-sm font-medium text-slate-900">Android App</p>
                      <p className="text-xs text-slate-600">
                        {apkInfo.versionName ? `Version ${apkInfo.versionName}` : "Download latest APK"}
                      </p>
                    </div>
                    <a
                      href={apkInfo.apkUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-2 rounded-md bg-slate-900 px-3 py-2 text-xs font-medium text-white hover:bg-slate-800"
                      data-testid="download-android-apk-btn"
                    >
                      <Download className="h-4 w-4" />
                      Download APK
                    </a>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      {/* Right Side - Visual */}
      <div className="hidden lg:flex flex-1 bg-slate-900 relative overflow-hidden items-center justify-center p-12">
        <div className="absolute inset-0 opacity-20">
          <img
            src="https://images.pexels.com/photos/8867376/pexels-photo-8867376.jpeg"
            alt="Office environment"
            className="w-full h-full object-cover"
          />
        </div>
        <div className="relative z-10 text-center max-w-lg">
          <div className="w-20 h-20 bg-orange-500 rounded-2xl flex items-center justify-center mx-auto mb-8">
            <MessageSquare className="w-10 h-10 text-white" />
          </div>
          <h2 className="text-3xl font-heading font-bold text-white mb-4">
            Connect with millions of customers
          </h2>
          <p className="text-slate-400 text-lg">
            Join thousands of Indian businesses using Textzy to engage customers on WhatsApp and SMS.
          </p>
          <div className="mt-8 grid grid-cols-3 gap-6">
            <div>
              <p className="text-3xl font-bold text-orange-500">10M+</p>
              <p className="text-slate-400 text-sm">Messages Daily</p>
            </div>
            <div>
              <p className="text-3xl font-bold text-orange-500">5K+</p>
              <p className="text-slate-400 text-sm">Active Users</p>
            </div>
            <div>
              <p className="text-3xl font-bold text-orange-500">99.9%</p>
              <p className="text-slate-400 text-sm">Uptime</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default LoginPage;
