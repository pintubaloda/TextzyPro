import { useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MessageSquare, Eye, EyeOff, ArrowLeft, CheckCircle, ArrowRight } from "lucide-react";
import { toast } from "sonner";
import { authResetPassword } from "@/lib/api";

const ResetPasswordPage = () => {
  const [searchParams] = useSearchParams();
  const verificationId = (searchParams.get("verificationId") || "").trim();
  const token = (searchParams.get("token") || "").trim();
  const hasToken = !!verificationId && !!token;

  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [done, setDone] = useState(false);
  const [form, setForm] = useState({ password: "", confirmPassword: "" });

  const errorHint = useMemo(() => {
    if (!hasToken) return "This reset link is invalid or incomplete. Please request a new password reset email.";
    return "";
  }, [hasToken]);

  const onSubmit = async (e) => {
    e.preventDefault();
    if (!hasToken) {
      toast.error("Invalid reset link. Please request a new email.");
      return;
    }
    if ((form.password || "").trim().length < 8) {
      toast.error("Password must be at least 8 characters.");
      return;
    }
    if (String(form.password) !== String(form.confirmPassword)) {
      toast.error("Passwords do not match.");
      return;
    }

    setLoading(true);
    try {
      await authResetPassword({
        verificationId,
        token,
        password: form.password,
        confirmPassword: form.confirmPassword,
      });
      setDone(true);
      toast.success("Password updated. You can login now.");
    } catch (err) {
      toast.error(err?.message || "Failed to reset password.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-50 flex items-center justify-center p-8" data-testid="reset-password-page">
      <div className="w-full max-w-md">
        <Link to="/" className="flex items-center gap-2 mb-8" data-testid="reset-password-logo">
          <div className="w-10 h-10 bg-orange-500 rounded-lg flex items-center justify-center">
            <MessageSquare className="w-6 h-6 text-white" />
          </div>
          <span className="font-heading font-bold text-2xl text-slate-900">Textzy</span>
        </Link>

        <Card className="border-slate-200 shadow-card">
          {!done ? (
            <>
              <CardHeader className="space-y-1">
                <CardTitle className="text-2xl font-heading">Reset your password</CardTitle>
                <CardDescription>
                  Choose a new password for your account.
                </CardDescription>
              </CardHeader>
              <CardContent>
                {!hasToken ? (
                  <div className="text-sm text-slate-600" data-testid="reset-invalid">
                    {errorHint}
                  </div>
                ) : (
                  <form onSubmit={onSubmit} className="space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="password">New password</Label>
                      <div className="relative">
                        <Input
                          id="password"
                          type={showPassword ? "text" : "password"}
                          placeholder="Min. 8 characters"
                          value={form.password}
                          onChange={(e) => setForm({ ...form, password: e.target.value })}
                          required
                          minLength={8}
                          data-testid="reset-password-input"
                        />
                        <button
                          type="button"
                          className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600"
                          onClick={() => setShowPassword((v) => !v)}
                        >
                          {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
                        </button>
                      </div>
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="confirmPassword">Confirm password</Label>
                      <Input
                        id="confirmPassword"
                        type={showPassword ? "text" : "password"}
                        placeholder="Repeat password"
                        value={form.confirmPassword}
                        onChange={(e) => setForm({ ...form, confirmPassword: e.target.value })}
                        required
                        minLength={8}
                        data-testid="reset-confirm-input"
                      />
                    </div>

                    <Button
                      type="submit"
                      className="w-full bg-orange-500 hover:bg-orange-600 text-white h-11"
                      disabled={loading}
                      data-testid="reset-submit-btn"
                    >
                      {loading ? "Updating..." : "Update password"}
                      {!loading && <ArrowRight className="w-4 h-4 ml-2" />}
                    </Button>
                  </form>
                )}

                <Link
                  to="/login"
                  className="mt-6 flex items-center justify-center gap-2 text-sm text-slate-600 hover:text-orange-500"
                  data-testid="reset-back-to-login-link"
                >
                  <ArrowLeft className="w-4 h-4" />
                  Back to Sign In
                </Link>
              </CardContent>
            </>
          ) : (
            <CardContent className="pt-8 pb-8 text-center">
              <div className="w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mx-auto mb-6">
                <CheckCircle className="w-8 h-8 text-green-600" />
              </div>
              <h3 className="text-xl font-heading font-semibold text-slate-900 mb-2">
                Password updated
              </h3>
              <p className="text-slate-600 mb-6">
                Your password has been updated successfully.
              </p>
              <Link to="/login">
                <Button className="w-full bg-orange-500 hover:bg-orange-600 text-white" data-testid="reset-login-btn">
                  Go to Login
                </Button>
              </Link>
            </CardContent>
          )}
        </Card>
      </div>
    </div>
  );
};

export default ResetPasswordPage;

