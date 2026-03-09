import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { MessageSquare, Eye, EyeOff, ArrowRight, Check } from "lucide-react";
import { toast } from "sonner";

const RegisterPage = () => {
  const navigate = useNavigate();
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState({
    companyName: "",
    fullName: "",
    email: "",
    phone: "",
    password: "",
    industry: "",
    agreeTerms: false,
  });

  const industries = [
    "E-commerce",
    "Healthcare",
    "Finance & Banking",
    "Education",
    "Real Estate",
    "Travel & Hospitality",
    "Retail",
    "Technology",
    "Manufacturing",
    "Other",
  ];

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!formData.agreeTerms) {
      toast.error("Please agree to the terms and conditions");
      return;
    }
    setLoading(true);
    
    setTimeout(() => {
      setLoading(false);
      toast.success("Account created successfully! Welcome to Textzy.");
      navigate("/dashboard");
    }, 1500);
  };

  const benefits = [
    "1,000 free WhatsApp messages",
    "5,000 free SMS credits",
    "Full DLT compliance support",
    "24/7 customer support",
  ];

  return (
    <div className="min-h-screen bg-slate-50 flex" data-testid="register-page">
      {/* Left Side - Visual */}
      <div className="hidden lg:flex flex-1 bg-gradient-to-br from-orange-500 to-orange-600 relative overflow-hidden items-center justify-center p-12">
        <div className="relative z-10 text-center max-w-lg">
          <div className="w-20 h-20 bg-white/20 backdrop-blur-sm rounded-2xl flex items-center justify-center mx-auto mb-8">
            <MessageSquare className="w-10 h-10 text-white" />
          </div>
          <h2 className="text-3xl font-heading font-bold text-white mb-4">
            Start Your Free Trial Today
          </h2>
          <p className="text-orange-100 text-lg mb-8">
            No credit card required. Get instant access to all features for 14 days.
          </p>
          
          <div className="bg-white/10 backdrop-blur-sm rounded-xl p-6 text-left">
            <h3 className="font-semibold text-white mb-4">What you'll get:</h3>
            <ul className="space-y-3">
              {benefits.map((benefit, i) => (
                <li key={i} className="flex items-center gap-3 text-white">
                  <div className="w-5 h-5 bg-white/20 rounded-full flex items-center justify-center">
                    <Check className="w-3 h-3" />
                  </div>
                  {benefit}
                </li>
              ))}
            </ul>
          </div>

          <div className="mt-8 flex items-center justify-center gap-4">
            <div className="flex -space-x-2">
              {["AK", "RS", "PM", "VK"].map((initials, i) => (
                <div
                  key={i}
                  className="w-10 h-10 rounded-full bg-white/20 flex items-center justify-center text-white text-sm font-medium border-2 border-white/30"
                >
                  {initials}
                </div>
              ))}
            </div>
            <p className="text-orange-100 text-sm">
              Join 5,000+ businesses already on Textzy
            </p>
          </div>
        </div>
      </div>

      {/* Right Side - Form */}
      <div className="flex-1 flex items-center justify-center p-8 overflow-y-auto">
        <div className="w-full max-w-md">
          <Link to="/" className="flex items-center gap-2 mb-8" data-testid="register-logo">
            <div className="w-10 h-10 bg-orange-500 rounded-lg flex items-center justify-center">
              <MessageSquare className="w-6 h-6 text-white" />
            </div>
            <span className="font-heading font-bold text-2xl text-slate-900">Textzy</span>
          </Link>

          <Card className="border-slate-200 shadow-card">
            <CardHeader className="space-y-1">
              <CardTitle className="text-2xl font-heading">Create your account</CardTitle>
              <CardDescription>
                Start your 14-day free trial. No credit card required.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <form onSubmit={handleSubmit} className="space-y-4">
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label htmlFor="companyName">Company Name</Label>
                    <Input
                      id="companyName"
                      placeholder="Your company"
                      value={formData.companyName}
                      onChange={(e) => setFormData({ ...formData, companyName: e.target.value })}
                      required
                      data-testid="register-company-input"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="fullName">Full Name</Label>
                    <Input
                      id="fullName"
                      placeholder="John Doe"
                      value={formData.fullName}
                      onChange={(e) => setFormData({ ...formData, fullName: e.target.value })}
                      required
                      data-testid="register-name-input"
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="email">Work Email</Label>
                  <Input
                    id="email"
                    type="email"
                    placeholder="name@company.com"
                    value={formData.email}
                    onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                    required
                    data-testid="register-email-input"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="phone">Phone Number</Label>
                  <div className="flex gap-2">
                    <div className="w-20 flex items-center justify-center bg-slate-50 border border-slate-200 rounded-md text-sm text-slate-600">
                      +91
                    </div>
                    <Input
                      id="phone"
                      type="tel"
                      placeholder="9876543210"
                      value={formData.phone}
                      onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                      required
                      className="flex-1"
                      data-testid="register-phone-input"
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="industry">Industry</Label>
                  <Select
                    value={formData.industry}
                    onValueChange={(value) => setFormData({ ...formData, industry: value })}
                  >
                    <SelectTrigger data-testid="register-industry-select">
                      <SelectValue placeholder="Select your industry" />
                    </SelectTrigger>
                    <SelectContent>
                      {industries.map((industry) => (
                        <SelectItem key={industry} value={industry.toLowerCase()}>
                          {industry}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="password">Password</Label>
                  <div className="relative">
                    <Input
                      id="password"
                      type={showPassword ? "text" : "password"}
                      placeholder="Min. 8 characters"
                      value={formData.password}
                      onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                      required
                      minLength={8}
                      data-testid="register-password-input"
                    />
                    <button
                      type="button"
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600"
                      onClick={() => setShowPassword(!showPassword)}
                    >
                      {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
                    </button>
                  </div>
                </div>

                <div className="flex items-start space-x-2">
                  <Checkbox
                    id="terms"
                    checked={formData.agreeTerms}
                    onCheckedChange={(checked) => setFormData({ ...formData, agreeTerms: checked })}
                    className="mt-1"
                    data-testid="register-terms-checkbox"
                  />
                  <label htmlFor="terms" className="text-sm text-slate-600 cursor-pointer">
                    I agree to the{" "}
                    <a href="#" className="text-orange-500 hover:underline">Terms of Service</a>
                    {" "}and{" "}
                    <a href="#" className="text-orange-500 hover:underline">Privacy Policy</a>
                  </label>
                </div>

                <Button
                  type="submit"
                  className="w-full bg-orange-500 hover:bg-orange-600 text-white h-11"
                  disabled={loading}
                  data-testid="register-submit-btn"
                >
                  {loading ? "Creating account..." : "Create Account"}
                  {!loading && <ArrowRight className="w-4 h-4 ml-2" />}
                </Button>
              </form>

              <p className="mt-6 text-center text-sm text-slate-600">
                Already have an account?{" "}
                <Link to="/login" className="text-orange-500 hover:text-orange-600 font-medium" data-testid="login-link">
                  Sign in
                </Link>
              </p>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
};

export default RegisterPage;
