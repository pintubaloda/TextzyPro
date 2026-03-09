import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Calendar } from "@/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import {
  Search,
  Plus,
  Filter,
  MoreVertical,
  Megaphone,
  Send,
  Clock,
  CheckCircle,
  XCircle,
  Calendar as CalendarIcon,
  Users,
  TrendingUp,
  Eye,
  Copy,
  Pause,
  Play,
  Trash2,
  Edit,
  BarChart3,
  MessageSquare,
} from "lucide-react";
import { format } from "date-fns";
import { apiDelete, apiGet, apiPost } from "@/lib/api";

const CampaignsPage = () => {
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [date, setDate] = useState(new Date());
  const [campaigns, setCampaigns] = useState([]);
  const [draft, setDraft] = useState({ name: "", channel: "whatsapp", templateText: "" });

  useEffect(() => {
    apiGet("/api/campaigns").then(setCampaigns).catch(() => setCampaigns([]));
  }, []);

  const stats = [
    { title: "Total Campaigns", value: String(campaigns.length), icon: Megaphone, color: "orange" },
    { title: "Active", value: String(campaigns.length), icon: Play, color: "green" },
    { title: "Scheduled", value: "5", icon: Clock, color: "blue" },
    { title: "Avg. Open Rate", value: "72%", icon: Eye, color: "purple" },
  ];

  const getStatusBadge = (status) => {
    switch (status) {
      case "completed":
        return <Badge className="bg-green-100 text-green-700 hover:bg-green-100">Completed</Badge>;
      case "active":
        return <Badge className="bg-blue-100 text-blue-700 hover:bg-blue-100">Active</Badge>;
      case "scheduled":
        return <Badge className="bg-yellow-100 text-yellow-700 hover:bg-yellow-100">Scheduled</Badge>;
      case "paused":
        return <Badge className="bg-slate-100 text-slate-700 hover:bg-slate-100">Paused</Badge>;
      case "failed":
        return <Badge className="bg-red-100 text-red-700 hover:bg-red-100">Failed</Badge>;
      default:
        return null;
    }
  };

  const createCampaign = async () => {
    if (!draft.name.trim()) return;
    await apiPost("/api/campaigns", {
      name: draft.name,
      channel: draft.channel === "sms" ? 1 : 2,
      templateText: draft.templateText || "",
    });
    setCampaigns(await apiGet("/api/campaigns"));
    setShowCreateDialog(false);
    setDraft({ name: "", channel: "whatsapp", templateText: "" });
  };

  const removeCampaign = async (id) => {
    await apiDelete(`/api/campaigns/${id}`);
    setCampaigns((prev) => prev.filter((x) => x.id !== id));
  };

  const getChannelBadge = (channel) => {
    if (channel === "whatsapp") {
      return (
        <Badge variant="outline" className="bg-green-50 text-green-700 border-green-200">
          <MessageSquare className="w-3 h-3 mr-1" />
          WhatsApp
        </Badge>
      );
    }
    return (
      <Badge variant="outline" className="bg-orange-50 text-orange-700 border-orange-200">
        <Send className="w-3 h-3 mr-1" />
        SMS
      </Badge>
    );
  };

  return (
    <div className="space-y-6" data-testid="campaigns-page">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Campaigns</h1>
          <p className="text-slate-600">Create and manage your broadcast campaigns</p>
        </div>
        <Dialog open={showCreateDialog} onOpenChange={setShowCreateDialog}>
          <DialogTrigger asChild>
            <Button className="bg-orange-500 hover:bg-orange-600 text-white gap-2" data-testid="create-campaign-btn">
              <Plus className="w-4 h-4" />
              Create Campaign
            </Button>
          </DialogTrigger>
          <DialogContent className="max-w-2xl">
            <DialogHeader>
              <DialogTitle>Create New Campaign</DialogTitle>
              <DialogDescription>
                Set up a new broadcast campaign for your audience
              </DialogDescription>
            </DialogHeader>
            <div className="space-y-4 py-4">
              <div className="space-y-2">
                <Label>Campaign Name</Label>
                <Input placeholder="e.g., Summer Sale Announcement" value={draft.name} onChange={(e) => setDraft((p) => ({ ...p, name: e.target.value }))} />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label>Channel</Label>
                  <Select value={draft.channel} onValueChange={(v) => setDraft((p) => ({ ...p, channel: v }))}>
                    <SelectTrigger>
                      <SelectValue placeholder="Select channel" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="whatsapp">WhatsApp</SelectItem>
                      <SelectItem value="sms">SMS</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label>Template</Label>
                  <Select>
                    <SelectTrigger>
                      <SelectValue placeholder="Select template" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="promo1">Promotional - Summer Sale</SelectItem>
                      <SelectItem value="promo2">Promotional - New Arrivals</SelectItem>
                      <SelectItem value="utility1">Utility - Order Update</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>

              <div className="space-y-2">
                <Label>Audience Segment</Label>
                <Select>
                  <SelectTrigger>
                    <SelectValue placeholder="Select audience" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All Contacts (15,832)</SelectItem>
                    <SelectItem value="active">Active Buyers (5,234)</SelectItem>
                    <SelectItem value="new">New Users (3,421)</SelectItem>
                    <SelectItem value="newsletter">Newsletter Subscribers (8,765)</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="space-y-2">
                <Label>Template Text</Label>
                <Textarea value={draft.templateText} onChange={(e) => setDraft((p) => ({ ...p, templateText: e.target.value }))} placeholder="Message body" />
              </div>

              <div className="space-y-2">
                <Label>Schedule</Label>
                <div className="flex items-center gap-4">
                  <Popover>
                    <PopoverTrigger asChild>
                      <Button variant="outline" className="w-48 justify-start text-left font-normal">
                        <CalendarIcon className="mr-2 h-4 w-4" />
                        {date ? format(date, "PPP") : "Pick a date"}
                      </Button>
                    </PopoverTrigger>
                    <PopoverContent className="w-auto p-0">
                      <Calendar
                        mode="single"
                        selected={date}
                        onSelect={setDate}
                        initialFocus
                      />
                    </PopoverContent>
                  </Popover>
                  <Select>
                    <SelectTrigger className="w-32">
                      <SelectValue placeholder="Time" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="09:00">09:00 AM</SelectItem>
                      <SelectItem value="10:00">10:00 AM</SelectItem>
                      <SelectItem value="11:00">11:00 AM</SelectItem>
                      <SelectItem value="12:00">12:00 PM</SelectItem>
                      <SelectItem value="14:00">02:00 PM</SelectItem>
                      <SelectItem value="16:00">04:00 PM</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>

              <div className="p-4 bg-slate-50 rounded-lg">
                <p className="text-sm font-medium text-slate-700 mb-2">Campaign Summary</p>
                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div className="flex justify-between">
                    <span className="text-slate-500">Recipients:</span>
                    <span className="font-medium">15,832</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-500">Est. Cost:</span>
                    <span className="font-medium">₹4,749</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-500">Est. Delivery:</span>
                    <span className="font-medium">~2 hours</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-500">Schedule:</span>
                    <span className="font-medium">Jan 25, 10:00 AM</span>
                  </div>
                </div>
              </div>
            </div>
            <DialogFooter>
              <Button variant="outline" onClick={() => setShowCreateDialog(false)}>Cancel</Button>
              <Button variant="outline">Save as Draft</Button>
              <Button className="bg-orange-500 hover:bg-orange-600" onClick={createCampaign}>Schedule Campaign</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {stats.map((stat, index) => (
          <Card key={index} className="border-slate-200">
            <CardContent className="pt-6">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm text-slate-600">{stat.title}</p>
                  <p className="text-2xl font-bold text-slate-900">{stat.value}</p>
                </div>
                <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${
                  stat.color === "orange" ? "bg-orange-100" :
                  stat.color === "green" ? "bg-green-100" :
                  stat.color === "blue" ? "bg-blue-100" : "bg-purple-100"
                }`}>
                  <stat.icon className={`w-5 h-5 ${
                    stat.color === "orange" ? "text-orange-600" :
                    stat.color === "green" ? "text-green-600" :
                    stat.color === "blue" ? "text-blue-600" : "text-purple-600"
                  }`} />
                </div>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      {/* Campaigns Table */}
      <Card className="border-slate-200">
        <CardHeader>
          <Tabs defaultValue="all">
            <div className="flex items-center justify-between">
              <TabsList>
                <TabsTrigger value="all">All Campaigns</TabsTrigger>
                <TabsTrigger value="active">Active</TabsTrigger>
                <TabsTrigger value="scheduled">Scheduled</TabsTrigger>
                <TabsTrigger value="completed">Completed</TabsTrigger>
              </TabsList>
              <div className="flex items-center gap-2">
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                  <Input placeholder="Search campaigns..." className="pl-10 w-64" data-testid="search-campaigns" />
                </div>
                <Button variant="outline" size="icon" data-testid="filter-campaigns-btn">
                  <Filter className="w-4 h-4" />
                </Button>
              </div>
            </div>
          </Tabs>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Campaign</TableHead>
                <TableHead>Channel</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Sent</TableHead>
                <TableHead>Delivered</TableHead>
                <TableHead>Read</TableHead>
                <TableHead>Clicked</TableHead>
                <TableHead>Schedule</TableHead>
                <TableHead className="w-12"></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {campaigns.map((campaign) => (
                <TableRow key={campaign.id} className="table-row-hover">
                  <TableCell>
                    <div>
                      <p className="font-medium text-slate-900">{campaign.name}</p>
                      <p className="text-sm text-slate-500">Created {campaign.createdAt || "-"}</p>
                    </div>
                  </TableCell>
                  <TableCell>{getChannelBadge(campaign.channel === 1 ? "sms" : "whatsapp")}</TableCell>
                  <TableCell>{getStatusBadge(campaign.status || "active")}</TableCell>
                  <TableCell className="text-slate-600">{(campaign.sent || 0).toLocaleString()}</TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <span className="text-slate-600">{(campaign.delivered || 0).toLocaleString()}</span>
                      {(campaign.sent || 0) > 0 && (
                        <span className="text-xs text-green-600">
                          ({Math.round(((campaign.delivered || 0) / (campaign.sent || 1)) * 100)}%)
                        </span>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <span className="text-slate-600">{(campaign.read || 0).toLocaleString()}</span>
                      {(campaign.delivered || 0) > 0 && (
                        <span className="text-xs text-blue-600">
                          ({Math.round(((campaign.read || 0) / (campaign.delivered || 1)) * 100)}%)
                        </span>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <span className="text-slate-600">{(campaign.clicked || 0).toLocaleString()}</span>
                      {(campaign.read || 0) > 0 && (
                        <span className="text-xs text-purple-600">
                          ({Math.round(((campaign.clicked || 0) / (campaign.read || 1)) * 100)}%)
                        </span>
                      )}
                    </div>
                  </TableCell>
                  <TableCell className="text-slate-500 text-sm">{campaign.scheduledAt || "-"}</TableCell>
                  <TableCell>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" data-testid={`campaign-menu-${campaign.id}`}>
                          <MoreVertical className="w-4 h-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem>
                          <BarChart3 className="w-4 h-4 mr-2" />
                          View Analytics
                        </DropdownMenuItem>
                        <DropdownMenuItem>
                          <Copy className="w-4 h-4 mr-2" />
                          Duplicate
                        </DropdownMenuItem>
                        <DropdownMenuItem>
                          <Edit className="w-4 h-4 mr-2" />
                          Edit
                        </DropdownMenuItem>
                        {campaign.status === "active" && (
                          <DropdownMenuItem>
                            <Pause className="w-4 h-4 mr-2" />
                            Pause
                          </DropdownMenuItem>
                        )}
                        {campaign.status === "paused" && (
                          <DropdownMenuItem>
                            <Play className="w-4 h-4 mr-2" />
                            Resume
                          </DropdownMenuItem>
                        )}
                        <DropdownMenuSeparator />
                        <DropdownMenuItem className="text-red-600" onClick={() => removeCampaign(campaign.id)}>
                          <Trash2 className="w-4 h-4 mr-2" />
                          Delete
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
};

export default CampaignsPage;
