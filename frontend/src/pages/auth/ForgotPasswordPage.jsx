import { useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MessageSquare, ArrowLeft, Mail, CheckCircle } from "lucide-react";
import { toast } from "sonner";

const ForgotPasswordPage = () => {
  const [email, setEmail] = useState("");
  const [loading, setLoading] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);
    
    setTimeout(() => {
      setLoading(false);
      setSubmitted(true);
      toast.success("Password reset link sent to your email!");
    }, 1500);
  };

  return (
    <div className="min-h-screen bg-slate-50 flex items-center justify-center p-8" data-testid="forgot-password-page">
      <div className="w-full max-w-md">
        <Link to="/" className="flex items-center gap-2 mb-8" data-testid="forgot-password-logo">
          <div className="w-10 h-10 bg-orange-500 rounded-lg flex items-center justify-center">
            <MessageSquare className="w-6 h-6 text-white" />
          </div>
          <span className="font-heading font-bold text-2xl text-slate-900">Textzy</span>
        </Link>

        <Card className="border-slate-200 shadow-card">
          {!submitted ? (
            <>
              <CardHeader className="space-y-1">
                <CardTitle className="text-2xl font-heading">Forgot password?</CardTitle>
                <CardDescription>
                  Enter your email address and we'll send you a link to reset your password.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <form onSubmit={handleSubmit} className="space-y-4">
                  <div className="space-y-2">
                    <Label htmlFor="email">Email Address</Label>
                    <Input
                      id="email"
                      type="email"
                      placeholder="name@company.com"
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      required
                      data-testid="forgot-email-input"
                    />
                  </div>

                  <Button
                    type="submit"
                    className="w-full bg-orange-500 hover:bg-orange-600 text-white h-11"
                    disabled={loading}
                    data-testid="forgot-submit-btn"
                  >
                    {loading ? "Sending..." : "Send Reset Link"}
                    {!loading && <Mail className="w-4 h-4 ml-2" />}
                  </Button>
                </form>

                <Link
                  to="/login"
                  className="mt-6 flex items-center justify-center gap-2 text-sm text-slate-600 hover:text-orange-500"
                  data-testid="back-to-login-link"
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
                Check your email
              </h3>
              <p className="text-slate-600 mb-6">
                We've sent a password reset link to{" "}
                <span className="font-medium text-slate-900">{email}</span>
              </p>
              <p className="text-sm text-slate-500 mb-6">
                Didn't receive the email? Check your spam folder or{" "}
                <button
                  onClick={() => setSubmitted(false)}
                  className="text-orange-500 hover:underline"
                  data-testid="try-again-btn"
                >
                  try another email
                </button>
              </p>
              <Link to="/login">
                <Button variant="outline" className="w-full" data-testid="return-to-login-btn">
                  <ArrowLeft className="w-4 h-4 mr-2" />
                  Return to Sign In
                </Button>
              </Link>
            </CardContent>
          )}
        </Card>
      </div>
    </div>
  );
};

export default ForgotPasswordPage;
