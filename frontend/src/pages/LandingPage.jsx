import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  MessageSquare,
  Send,
  Users,
  BarChart3,
  Zap,
  Shield,
  Globe,
  ChevronRight,
  Menu,
  X,
  Check,
  Star,
  ArrowRight,
  MessageCircle,
  Phone,
  Mail,
  MapPin,
  Play,
} from "lucide-react";
import { getPublicPlans } from "@/lib/api";

const LandingPage = () => {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  const features = [
    {
      icon: MessageSquare,
      title: "WhatsApp Business API",
      description: "Connect with customers on WhatsApp with official Business API integration. Send templates, media, and interactive messages.",
    },
    {
      icon: Send,
      title: "Bulk SMS (DLT Compliant)",
      description: "Send promotional and transactional SMS across India with full DLT compliance. Entity ID and template registration made easy.",
    },
    {
      icon: Users,
      title: "Contact Management",
      description: "Import, segment, and manage your contacts effortlessly. Create targeted audience groups for personalized campaigns.",
    },
    {
      icon: BarChart3,
      title: "Advanced Analytics",
      description: "Track delivery rates, read receipts, click-through rates, and campaign performance with real-time dashboards.",
    },
    {
      icon: Zap,
      title: "Automation Builder",
      description: "Create powerful chatbots and automated workflows with our visual drag-and-drop builder. No coding required.",
    },
    {
      icon: Shield,
      title: "Enterprise Security",
      description: "Bank-grade encryption, role-based access, and complete data isolation for multi-tenant deployments.",
    },
  ];

  const [pricingPlans, setPricingPlans] = useState([
    {
      name: "Starter",
      price: "₹2,999",
      period: "/month",
      description: "Perfect for small businesses getting started",
      features: [
        "1,000 WhatsApp messages/month",
        "5,000 SMS credits",
        "2 Team members",
        "Basic analytics",
        "Email support",
        "API access",
      ],
      popular: false,
    },
    {
      name: "Growth",
      price: "₹9,999",
      period: "/month",
      description: "For growing businesses with higher volumes",
      features: [
        "10,000 WhatsApp messages/month",
        "50,000 SMS credits",
        "10 Team members",
        "Advanced analytics",
        "Priority support",
        "Automation builder",
        "Custom templates",
        "Webhook integrations",
      ],
      popular: true,
    },
    {
      name: "Enterprise",
      price: "Custom",
      period: "",
      description: "For large organizations with custom needs",
      features: [
        "Unlimited messages",
        "Custom SMS rates",
        "Unlimited team members",
        "Dedicated account manager",
        "SLA guarantee",
        "Custom integrations",
        "On-premise deployment",
        "24/7 phone support",
      ],
      popular: false,
    },
  ]);

  useEffect(() => {
    (async () => {
      try {
        const rows = await getPublicPlans();
        if (!Array.isArray(rows) || rows.length === 0) return;
        const activeRows = rows
          .filter((p) => p?.isActive !== false)
          .sort((a, b) => (a?.sortOrder || 0) - (b?.sortOrder || 0));
        setPricingPlans(activeRows.map((p) => ({
          code: p.code,
          name: p.name,
          price: Number(p.priceMonthly || 0) <= 0 ? "Custom" : `₹${Number(p.priceMonthly || 0).toLocaleString()}`,
          period: Number(p.priceMonthly || 0) <= 0 ? "" : String(p.pricingModel || "").toLowerCase() === "usage_pack" ? "/pack" : "/month",
          taxLabel: String(p.taxMode || "exclusive").toLowerCase() === "inclusive" ? "incl. GST" : "+ GST",
          description: p.code === "starter" ? "Perfect for small businesses getting started" : p.code === "growth" ? "For growing businesses with higher volumes" : "For large organizations with custom needs",
          features: Array.isArray(p.features) && p.features.length > 0 ? p.features : [
            `${Number(p?.limits?.whatsappMessages || 0).toLocaleString()} WhatsApp messages`,
            `${Number(p?.limits?.smsCredits || 0).toLocaleString()} SMS credits`,
            `${Number(p?.limits?.contacts || 0).toLocaleString()} contacts`,
            `${Number(p?.limits?.teamMembers || 0).toLocaleString()} team members`,
          ],
          popular: p.code === "growth"
        })));
      } catch {
        // keep fallback plans
      }
    })();
  }, []);

  const testimonials = [
    {
      name: "Rajesh Kumar",
      role: "CEO, TechStart India",
      content: "Textzy transformed our customer communication. We saw a 40% increase in engagement within the first month.",
      avatar: "RK",
    },
    {
      name: "Priya Sharma",
      role: "Marketing Head, ShopEasy",
      content: "The DLT compliance features saved us countless hours. Finally, a platform that understands Indian regulations.",
      avatar: "PS",
    },
    {
      name: "Amit Patel",
      role: "Founder, FinanceFirst",
      content: "Best ROI we've seen from any communication platform. The automation builder is incredibly powerful.",
      avatar: "AP",
    },
  ];

  const stats = [
    { value: "10M+", label: "Messages Sent Daily" },
    { value: "5,000+", label: "Active Businesses" },
    { value: "99.9%", label: "Uptime SLA" },
    { value: "24/7", label: "Support Available" },
  ];

  return (
    <div className="min-h-screen bg-white" data-testid="landing-page">
      {/* Navigation */}
      <nav className="fixed top-0 left-0 right-0 z-50 bg-white/90 backdrop-blur-md border-b border-slate-200">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16">
            {/* Logo */}
            <Link to="/" className="flex items-center gap-2" data-testid="logo">
              <div className="w-8 h-8 bg-orange-500 rounded-lg flex items-center justify-center">
                <MessageSquare className="w-5 h-5 text-white" />
              </div>
              <span className="font-heading font-bold text-xl text-slate-900">Textzy</span>
            </Link>

            {/* Desktop Navigation */}
            <div className="hidden md:flex items-center gap-8">
              <a href="#features" className="text-slate-600 hover:text-orange-500 transition-colors font-medium">
                Features
              </a>
              <a href="#pricing" className="text-slate-600 hover:text-orange-500 transition-colors font-medium">
                Pricing
              </a>
              <a href="#testimonials" className="text-slate-600 hover:text-orange-500 transition-colors font-medium">
                Testimonials
              </a>
              <a href="#contact" className="text-slate-600 hover:text-orange-500 transition-colors font-medium">
                Contact
              </a>
            </div>

            {/* Auth Buttons */}
            <div className="hidden md:flex items-center gap-4">
              <Link to="/login">
                <Button variant="ghost" className="text-slate-700 hover:text-orange-500" data-testid="login-btn">
                  Login
                </Button>
              </Link>
              <Link to="/register">
                <Button className="bg-orange-500 hover:bg-orange-600 text-white" data-testid="get-started-btn">
                  Get Started Free
                </Button>
              </Link>
            </div>

            {/* Mobile Menu Button */}
            <button
              className="md:hidden p-2"
              onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
              data-testid="mobile-menu-btn"
            >
              {mobileMenuOpen ? <X className="w-6 h-6" /> : <Menu className="w-6 h-6" />}
            </button>
          </div>
        </div>

        {/* Mobile Menu */}
        {mobileMenuOpen && (
          <div className="md:hidden bg-white border-t border-slate-200 py-4">
            <div className="max-w-7xl mx-auto px-4 space-y-4">
              <a href="#features" className="block text-slate-600 hover:text-orange-500 font-medium">
                Features
              </a>
              <a href="#pricing" className="block text-slate-600 hover:text-orange-500 font-medium">
                Pricing
              </a>
              <a href="#testimonials" className="block text-slate-600 hover:text-orange-500 font-medium">
                Testimonials
              </a>
              <a href="#contact" className="block text-slate-600 hover:text-orange-500 font-medium">
                Contact
              </a>
              <div className="pt-4 space-y-2">
                <Link to="/login" className="block">
                  <Button variant="outline" className="w-full">Login</Button>
                </Link>
                <Link to="/register" className="block">
                  <Button className="w-full bg-orange-500 hover:bg-orange-600">Get Started Free</Button>
                </Link>
              </div>
            </div>
          </div>
        )}
      </nav>

      {/* Hero Section */}
      <section className="pt-32 pb-20 hero-gradient relative overflow-hidden">
        <div className="absolute inset-0 opacity-30">
          <img
            src="https://images.pexels.com/photos/17483870/pexels-photo-17483870.png"
            alt="Digital connectivity network"
            className="w-full h-full object-cover"
          />
        </div>
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative z-10">
          <div className="grid lg:grid-cols-2 gap-12 items-center">
            <div className="space-y-8 animate-slide-up">
              <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100 px-4 py-1">
                Trusted by 5,000+ Indian Businesses
              </Badge>
              <h1 className="font-heading text-4xl md:text-5xl lg:text-6xl font-bold text-slate-900 tracking-tight leading-tight">
                Connect with Your Customers on{" "}
                <span className="text-orange-500">WhatsApp & SMS</span>
              </h1>
              <p className="text-lg text-slate-600 max-w-xl leading-relaxed">
                India's most powerful business messaging platform. Send campaigns, automate conversations, 
                and grow your business with WhatsApp Business API and DLT-compliant SMS.
              </p>
              <div className="flex flex-wrap gap-4">
                <Link to="/register">
                  <Button size="lg" className="bg-orange-500 hover:bg-orange-600 text-white gap-2 h-12 px-6" data-testid="hero-cta-btn">
                    Start Free Trial <ArrowRight className="w-4 h-4" />
                  </Button>
                </Link>
                <Button size="lg" variant="outline" className="gap-2 h-12 px-6" data-testid="demo-btn">
                  <Play className="w-4 h-4" /> Watch Demo
                </Button>
              </div>
              <div className="flex items-center gap-6 pt-4">
                <div className="flex -space-x-2">
                  {["RK", "PS", "AM", "SK"].map((initials, i) => (
                    <div
                      key={i}
                      className="w-10 h-10 rounded-full bg-gradient-to-br from-orange-400 to-orange-600 flex items-center justify-center text-white text-sm font-medium border-2 border-white"
                    >
                      {initials}
                    </div>
                  ))}
                </div>
                <div>
                  <div className="flex items-center gap-1">
                    {[1, 2, 3, 4, 5].map((i) => (
                      <Star key={i} className="w-4 h-4 fill-yellow-400 text-yellow-400" />
                    ))}
                  </div>
                  <p className="text-sm text-slate-600">4.9/5 from 500+ reviews</p>
                </div>
              </div>
            </div>

            {/* Hero Image/Dashboard Preview */}
            <div className="relative animate-slide-up delay-200 hidden lg:block">
              <div className="bg-white rounded-2xl shadow-2xl border border-slate-200 p-6 transform rotate-2 hover:rotate-0 transition-transform duration-500">
                <div className="flex items-center gap-2 mb-4">
                  <div className="w-3 h-3 rounded-full bg-red-400"></div>
                  <div className="w-3 h-3 rounded-full bg-yellow-400"></div>
                  <div className="w-3 h-3 rounded-full bg-green-400"></div>
                </div>
                <div className="space-y-4">
                  <div className="flex items-center justify-between p-4 bg-slate-50 rounded-lg">
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 bg-green-500 rounded-full flex items-center justify-center">
                        <MessageCircle className="w-5 h-5 text-white" />
                      </div>
                      <div>
                        <p className="font-medium text-slate-900">WhatsApp Messages</p>
                        <p className="text-sm text-slate-500">Today's stats</p>
                      </div>
                    </div>
                    <p className="text-2xl font-bold text-slate-900">12,450</p>
                  </div>
                  <div className="grid grid-cols-2 gap-4">
                    <div className="p-4 bg-orange-50 rounded-lg">
                      <p className="text-sm text-slate-600">Delivered</p>
                      <p className="text-xl font-bold text-slate-900">98.5%</p>
                    </div>
                    <div className="p-4 bg-blue-50 rounded-lg">
                      <p className="text-sm text-slate-600">Read Rate</p>
                      <p className="text-xl font-bold text-slate-900">72.3%</p>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Stats Section */}
      <section className="py-16 bg-slate-900">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="grid grid-cols-2 md:grid-cols-4 gap-8">
            {stats.map((stat, index) => (
              <div key={index} className="text-center">
                <p className="text-3xl md:text-4xl font-bold text-orange-500">{stat.value}</p>
                <p className="text-slate-400 mt-2">{stat.label}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Features Section */}
      <section id="features" className="py-24 bg-slate-50">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="text-center max-w-3xl mx-auto mb-16">
            <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100 mb-4">Features</Badge>
            <h2 className="font-heading text-3xl md:text-4xl font-bold text-slate-900 mb-4">
              Everything You Need to Scale Customer Communication
            </h2>
            <p className="text-lg text-slate-600">
              From WhatsApp Business API to DLT-compliant SMS, Textzy provides all the tools you need to engage customers effectively.
            </p>
          </div>

          <div className="grid md:grid-cols-2 lg:grid-cols-3 gap-8">
            {features.map((feature, index) => (
              <Card
                key={index}
                className="bg-white border-slate-200 hover:border-orange-200 transition-all duration-300 card-hover"
                data-testid={`feature-card-${index}`}
              >
                <CardHeader>
                  <div className="feature-icon mb-4">
                    <feature.icon className="w-6 h-6" />
                  </div>
                  <CardTitle className="text-xl text-slate-900">{feature.title}</CardTitle>
                </CardHeader>
                <CardContent>
                  <CardDescription className="text-slate-600 text-base">
                    {feature.description}
                  </CardDescription>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </section>

      {/* How It Works */}
      <section className="py-24 bg-white">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="text-center max-w-3xl mx-auto mb-16">
            <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100 mb-4">How It Works</Badge>
            <h2 className="font-heading text-3xl md:text-4xl font-bold text-slate-900 mb-4">
              Get Started in Minutes
            </h2>
          </div>

          <div className="grid md:grid-cols-3 gap-8">
            {[
              {
                step: "01",
                title: "Create Your Account",
                description: "Sign up and connect your WhatsApp Business number. Complete DLT registration for SMS.",
              },
              {
                step: "02",
                title: "Import Contacts & Templates",
                description: "Upload your contact list and create message templates. Get templates approved instantly.",
              },
              {
                step: "03",
                title: "Launch Campaigns",
                description: "Send broadcasts, set up automations, and watch your engagement metrics soar.",
              },
            ].map((item, index) => (
              <div key={index} className="relative">
                <div className="text-6xl font-bold text-orange-100 absolute -top-4 left-0">{item.step}</div>
                <div className="pt-12 relative z-10">
                  <h3 className="font-heading text-xl font-semibold text-slate-900 mb-3">{item.title}</h3>
                  <p className="text-slate-600">{item.description}</p>
                </div>
                {index < 2 && (
                  <ChevronRight className="hidden md:block absolute top-1/2 -right-4 w-8 h-8 text-orange-300" />
                )}
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Team/About Section */}
      <section className="py-24 bg-slate-900 relative overflow-hidden">
        <div className="absolute inset-0 opacity-20">
          <img
            src="https://images.pexels.com/photos/7580644/pexels-photo-7580644.jpeg"
            alt="Indian business team"
            className="w-full h-full object-cover"
          />
        </div>
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative z-10">
          <div className="grid lg:grid-cols-2 gap-16 items-center">
            <div className="space-y-6">
              <Badge className="bg-orange-500/20 text-orange-400 hover:bg-orange-500/20">Built for India</Badge>
              <h2 className="font-heading text-3xl md:text-4xl font-bold text-white">
                Made by Indian Entrepreneurs, for Indian Businesses
              </h2>
              <p className="text-lg text-slate-300 leading-relaxed">
                We understand the unique challenges of business communication in India. From DLT compliance 
                to regional language support, Textzy is built ground-up for the Indian market.
              </p>
              <ul className="space-y-4">
                {[
                  "Full DLT compliance with TRAI regulations",
                  "Support for 10+ Indian languages",
                  "Local payment options including UPI",
                  "India-based support team",
                ].map((item, i) => (
                  <li key={i} className="flex items-center gap-3 text-slate-300">
                    <Check className="w-5 h-5 text-orange-500" />
                    {item}
                  </li>
                ))}
              </ul>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-4">
                <div className="bg-white/10 backdrop-blur-sm rounded-xl p-6">
                  <Globe className="w-8 h-8 text-orange-500 mb-3" />
                  <h4 className="font-semibold text-white mb-2">Pan-India Coverage</h4>
                  <p className="text-sm text-slate-400">Reach customers across all states and territories</p>
                </div>
                <div className="bg-white/10 backdrop-blur-sm rounded-xl p-6">
                  <Shield className="w-8 h-8 text-orange-500 mb-3" />
                  <h4 className="font-semibold text-white mb-2">GDPR Compliant</h4>
                  <p className="text-sm text-slate-400">Enterprise-grade data protection</p>
                </div>
              </div>
              <div className="space-y-4 mt-8">
                <div className="bg-white/10 backdrop-blur-sm rounded-xl p-6">
                  <Zap className="w-8 h-8 text-orange-500 mb-3" />
                  <h4 className="font-semibold text-white mb-2">99.9% Uptime</h4>
                  <p className="text-sm text-slate-400">Reliable infrastructure you can count on</p>
                </div>
                <div className="bg-white/10 backdrop-blur-sm rounded-xl p-6">
                  <Users className="w-8 h-8 text-orange-500 mb-3" />
                  <h4 className="font-semibold text-white mb-2">5000+ Customers</h4>
                  <p className="text-sm text-slate-400">Trusted by leading Indian brands</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Pricing Section */}
      <section id="pricing" className="py-24 bg-white">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="text-center max-w-3xl mx-auto mb-16">
            <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100 mb-4">Pricing</Badge>
            <h2 className="font-heading text-3xl md:text-4xl font-bold text-slate-900 mb-4">
              Simple, Transparent Pricing
            </h2>
            <p className="text-lg text-slate-600">
              Start free, upgrade when you're ready. No hidden fees, no surprises.
            </p>
          </div>

          <div className="grid md:grid-cols-3 gap-8">
            {pricingPlans.map((plan, index) => (
              <Card
                key={index}
                className={`relative bg-white ${
                  plan.popular ? "border-2 border-orange-500 shadow-glow" : "border-slate-200"
                }`}
                data-testid={`pricing-card-${plan.name.toLowerCase()}`}
              >
                {plan.popular && (
                  <div className="absolute -top-3 left-1/2 -translate-x-1/2">
                    <Badge className="bg-orange-500 text-white hover:bg-orange-500">Most Popular</Badge>
                  </div>
                )}
                <CardHeader className="pt-8">
                  <CardTitle className="text-xl text-slate-900">{plan.name}</CardTitle>
                  <div className="mt-4">
                    <span className="text-4xl font-bold text-slate-900">{plan.price}</span>
                    <span className="text-slate-500">{plan.period}</span>
                  </div>
                  <p className="mt-2 text-xs font-medium uppercase tracking-[0.16em] text-slate-500">{plan.taxLabel || "+ GST"}</p>
                  <CardDescription className="mt-2">{plan.description}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <ul className="space-y-3">
                    {plan.features.map((feature, i) => (
                      <li key={i} className="flex items-start gap-3">
                        <Check className="w-5 h-5 text-orange-500 mt-0.5 flex-shrink-0" />
                        <span className="text-slate-600">{feature}</span>
                      </li>
                    ))}
                  </ul>
                  <Button
                    className={`w-full mt-6 ${
                      plan.popular
                        ? "bg-orange-500 hover:bg-orange-600 text-white"
                        : "bg-slate-100 hover:bg-slate-200 text-slate-900"
                    }`}
                    data-testid={`pricing-btn-${plan.name.toLowerCase()}`}
                  >
                    {plan.name === "Enterprise" ? "Contact Sales" : "Get Started"}
                  </Button>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </section>

      {/* Testimonials Section */}
      <section id="testimonials" className="py-24 bg-slate-50">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="text-center max-w-3xl mx-auto mb-16">
            <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100 mb-4">Testimonials</Badge>
            <h2 className="font-heading text-3xl md:text-4xl font-bold text-slate-900 mb-4">
              Loved by Businesses Across India
            </h2>
          </div>

          <div className="grid md:grid-cols-3 gap-8">
            {testimonials.map((testimonial, index) => (
              <Card key={index} className="bg-white border-slate-200" data-testid={`testimonial-${index}`}>
                <CardContent className="pt-6">
                  <div className="flex items-center gap-1 mb-4">
                    {[1, 2, 3, 4, 5].map((i) => (
                      <Star key={i} className="w-4 h-4 fill-yellow-400 text-yellow-400" />
                    ))}
                  </div>
                  <p className="text-slate-600 mb-6 italic">"{testimonial.content}"</p>
                  <div className="flex items-center gap-3">
                    <div className="w-12 h-12 rounded-full bg-gradient-to-br from-orange-400 to-orange-600 flex items-center justify-center text-white font-medium">
                      {testimonial.avatar}
                    </div>
                    <div>
                      <p className="font-medium text-slate-900">{testimonial.name}</p>
                      <p className="text-sm text-slate-500">{testimonial.role}</p>
                    </div>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </section>

      {/* Support Section */}
      <section className="py-24 bg-white relative overflow-hidden">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="grid lg:grid-cols-2 gap-16 items-center">
            <div className="relative">
              <img
                src="https://images.pexels.com/photos/7682350/pexels-photo-7682350.jpeg"
                alt="Customer support agent"
                className="rounded-2xl shadow-2xl"
              />
              <div className="absolute -bottom-6 -right-6 bg-orange-500 text-white p-6 rounded-xl shadow-lg">
                <p className="text-3xl font-bold">24/7</p>
                <p className="text-sm">Support Available</p>
              </div>
            </div>
            <div className="space-y-6">
              <Badge className="bg-orange-100 text-orange-700 hover:bg-orange-100">Support</Badge>
              <h2 className="font-heading text-3xl md:text-4xl font-bold text-slate-900">
                We're Here to Help You Succeed
              </h2>
              <p className="text-lg text-slate-600 leading-relaxed">
                Our dedicated support team is available round the clock to help you with setup, 
                troubleshooting, and optimizing your campaigns for maximum impact.
              </p>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex items-center gap-3 p-4 bg-slate-50 rounded-lg">
                  <MessageCircle className="w-6 h-6 text-orange-500" />
                  <span className="font-medium text-slate-900">Live Chat</span>
                </div>
                <div className="flex items-center gap-3 p-4 bg-slate-50 rounded-lg">
                  <Phone className="w-6 h-6 text-orange-500" />
                  <span className="font-medium text-slate-900">Phone Support</span>
                </div>
                <div className="flex items-center gap-3 p-4 bg-slate-50 rounded-lg">
                  <Mail className="w-6 h-6 text-orange-500" />
                  <span className="font-medium text-slate-900">Email Support</span>
                </div>
                <div className="flex items-center gap-3 p-4 bg-slate-50 rounded-lg">
                  <Globe className="w-6 h-6 text-orange-500" />
                  <span className="font-medium text-slate-900">Knowledge Base</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* CTA Section */}
      <section className="py-24 bg-gradient-to-br from-orange-500 to-orange-600">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center">
          <h2 className="font-heading text-3xl md:text-4xl font-bold text-white mb-6">
            Ready to Transform Your Customer Communication?
          </h2>
          <p className="text-lg text-orange-100 mb-8 max-w-2xl mx-auto">
            Join 5,000+ Indian businesses already using Textzy to engage customers on WhatsApp and SMS.
          </p>
          <div className="flex flex-wrap justify-center gap-4">
            <Link to="/register">
              <Button size="lg" className="bg-white text-orange-600 hover:bg-orange-50 h-12 px-8" data-testid="cta-start-btn">
                Start Free Trial
              </Button>
            </Link>
            <Button size="lg" variant="outline" className="border-white text-white hover:bg-white/10 h-12 px-8" data-testid="cta-demo-btn">
              Schedule Demo
            </Button>
          </div>
        </div>
      </section>

      {/* Footer */}
      <footer id="contact" className="bg-slate-900 pt-16 pb-8">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="grid md:grid-cols-4 gap-12 mb-12">
            <div className="space-y-4">
              <div className="flex items-center gap-2">
                <div className="w-8 h-8 bg-orange-500 rounded-lg flex items-center justify-center">
                  <MessageSquare className="w-5 h-5 text-white" />
                </div>
                <span className="font-heading font-bold text-xl text-white">Textzy</span>
              </div>
              <p className="text-slate-400">
                India's leading WhatsApp Business & SMS platform for modern businesses.
              </p>
              <div className="flex items-center gap-4">
                <a href="#" className="w-10 h-10 bg-slate-800 rounded-lg flex items-center justify-center text-slate-400 hover:text-orange-500 hover:bg-slate-700 transition-colors">
                  <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 24 24"><path d="M24 4.557c-.883.392-1.832.656-2.828.775 1.017-.609 1.798-1.574 2.165-2.724-.951.564-2.005.974-3.127 1.195-.897-.957-2.178-1.555-3.594-1.555-3.179 0-5.515 2.966-4.797 6.045-4.091-.205-7.719-2.165-10.148-5.144-1.29 2.213-.669 5.108 1.523 6.574-.806-.026-1.566-.247-2.229-.616-.054 2.281 1.581 4.415 3.949 4.89-.693.188-1.452.232-2.224.084.626 1.956 2.444 3.379 4.6 3.419-2.07 1.623-4.678 2.348-7.29 2.04 2.179 1.397 4.768 2.212 7.548 2.212 9.142 0 14.307-7.721 13.995-14.646.962-.695 1.797-1.562 2.457-2.549z"/></svg>
                </a>
                <a href="#" className="w-10 h-10 bg-slate-800 rounded-lg flex items-center justify-center text-slate-400 hover:text-orange-500 hover:bg-slate-700 transition-colors">
                  <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 24 24"><path d="M19 0h-14c-2.761 0-5 2.239-5 5v14c0 2.761 2.239 5 5 5h14c2.762 0 5-2.239 5-5v-14c0-2.761-2.238-5-5-5zm-11 19h-3v-11h3v11zm-1.5-12.268c-.966 0-1.75-.79-1.75-1.764s.784-1.764 1.75-1.764 1.75.79 1.75 1.764-.783 1.764-1.75 1.764zm13.5 12.268h-3v-5.604c0-3.368-4-3.113-4 0v5.604h-3v-11h3v1.765c1.396-2.586 7-2.777 7 2.476v6.759z"/></svg>
                </a>
              </div>
            </div>

            <div>
              <h4 className="font-semibold text-white mb-4">Product</h4>
              <ul className="space-y-3">
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">WhatsApp API</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">SMS Gateway</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">Automation</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">Analytics</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">Integrations</a></li>
              </ul>
            </div>

            <div>
              <h4 className="font-semibold text-white mb-4">Company</h4>
              <ul className="space-y-3">
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">About Us</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">Careers</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">Blog</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">Press Kit</a></li>
                <li><a href="#" className="text-slate-400 hover:text-orange-500 transition-colors">Contact</a></li>
              </ul>
            </div>

            <div>
              <h4 className="font-semibold text-white mb-4">Contact</h4>
              <ul className="space-y-3">
                <li className="flex items-center gap-2 text-slate-400">
                  <MapPin className="w-4 h-4" />
                  Mumbai, India
                </li>
                <li className="flex items-center gap-2 text-slate-400">
                  <Phone className="w-4 h-4" />
                  +91 22 1234 5678
                </li>
                <li className="flex items-center gap-2 text-slate-400">
                  <Mail className="w-4 h-4" />
                  hello@textzy.in
                </li>
              </ul>
            </div>
          </div>

          <div className="border-t border-slate-800 pt-8">
            <div className="flex flex-col md:flex-row justify-between items-center gap-4">
              <p className="text-slate-500 text-sm">
                © 2024 Textzy. All rights reserved.
              </p>
              <div className="flex items-center gap-6">
                <a href="#" className="text-slate-500 hover:text-slate-300 text-sm">Privacy Policy</a>
                <a href="#" className="text-slate-500 hover:text-slate-300 text-sm">Terms of Service</a>
                <a href="#" className="text-slate-500 hover:text-slate-300 text-sm">Cookie Policy</a>
              </div>
            </div>
          </div>
        </div>
      </footer>
    </div>
  );
};

export default LandingPage;
