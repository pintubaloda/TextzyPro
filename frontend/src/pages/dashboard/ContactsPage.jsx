import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
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
import {
  Search,
  Plus,
  Upload,
  Download,
  Filter,
  MoreVertical,
  Users,
  UserPlus,
  Tag,
  Trash2,
  Edit,
  Phone,
  MessageSquare,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { apiDelete, apiGet, apiPost, apiPut, buildIdempotencyKey, getBillingUsage, getCurrentBillingPlan } from "@/lib/api";
import { toast } from "sonner";

const ContactsPage = () => {
  const [selectedContacts, setSelectedContacts] = useState([]);
  const [showImportDialog, setShowImportDialog] = useState(false);
  const [showAddDialog, setShowAddDialog] = useState(false);
  const [showEditDialog, setShowEditDialog] = useState(false);
  const [showSendDialog, setShowSendDialog] = useState(false);
  const [contacts, setContacts] = useState([]);
  const [newContact, setNewContact] = useState({ name: "", phone: "", email: "", segmentId: "" });
  const [editContact, setEditContact] = useState(null);
  const [sendContact, setSendContact] = useState(null);
  const [messageBody, setMessageBody] = useState("");
  const [messageChannel, setMessageChannel] = useState("whatsapp");
  const [segmentsApi, setSegmentsApi] = useState([]);
  const [newSegmentName, setNewSegmentName] = useState("");
  const [billingUsage, setBillingUsage] = useState({});
  const [billingLimits, setBillingLimits] = useState({});

  const load = async () => {
    try {
      const [c, s, usageRes, planRes] = await Promise.all([
        apiGet("/api/contacts"),
        apiGet("/api/contact-data/segments"),
        getBillingUsage().catch(() => ({ values: {} })),
        getCurrentBillingPlan().catch(() => ({ plan: { limits: {} } })),
      ]);
      setContacts(Array.isArray(c) ? c : []);
      setSegmentsApi(Array.isArray(s) ? s : []);
      setBillingUsage(usageRes?.values || {});
      setBillingLimits(planRes?.plan?.limits || {});
    } catch {
      setContacts([]);
      setSegmentsApi([]);
      setBillingUsage({});
      setBillingLimits({});
    }
  };

  useEffect(() => {
    load();
  }, []);

  const segments = [
    { name: "All Contacts", count: contacts.length, id: "all" },
    ...segmentsApi.map((s) => ({
      id: s.id,
      name: s.name,
      count: contacts.filter((c) => c.segmentId === s.id).length,
    })),
  ];

  const handleSelectAll = (checked) => {
    if (checked) {
      setSelectedContacts(contacts.map((c) => c.id));
    } else {
      setSelectedContacts([]);
    }
  };

  const handleSelectContact = (id, checked) => {
    if (checked) {
      setSelectedContacts([...selectedContacts, id]);
    } else {
      setSelectedContacts(selectedContacts.filter((c) => c !== id));
    }
  };

  const handleAddContact = async () => {
    if (!newContact.name || !newContact.phone) return;
    if (!canCreateContact) {
      toast.error("Contact limit reached. Upgrade your plan.");
      return;
    }
    try {
      await apiPost("/api/contacts", {
        name: newContact.name,
        phone: newContact.phone,
        groupId: null,
        segmentId: newContact.segmentId || null,
      });
      await load();
      setShowAddDialog(false);
      setNewContact({ name: "", phone: "", email: "", segmentId: "" });
      toast.success("Contact added");
    } catch {
      toast.error("Failed to add contact");
    }
  };

  const contactsUsed = Number(billingUsage.contacts ?? contacts.length ?? 0);
  const contactsLimit = Number(billingLimits.contacts ?? 0);
  const canCreateContact = contactsLimit <= 0 || contactsUsed < contactsLimit;

  const handleDeleteContact = async (id) => {
    try {
      await apiDelete(`/api/contacts/${id}`);
      setContacts((prev) => prev.filter((x) => x.id !== id));
      toast.success("Contact deleted");
    } catch {
      toast.error("Failed to delete contact");
    }
  };

  const handleEditContact = (contact) => {
    setEditContact({
      id: contact.id,
      name: contact.name || "",
      phone: contact.phone || "",
      email: contact.email || "",
      segmentId: contact.segmentId || "",
      tagsCsv: contact.tagsCsv || (Array.isArray(contact.tags) ? contact.tags.join(",") : ""),
    });
    setShowEditDialog(true);
  };

  const handleUpdateContact = async () => {
    if (!editContact?.id) return;
    try {
      await apiPut(`/api/contacts/${editContact.id}`, {
        name: editContact.name,
        phone: editContact.phone,
        email: editContact.email || "",
        tagsCsv: editContact.tagsCsv || "",
        groupId: null,
        segmentId: editContact.segmentId || null,
      });
      setShowEditDialog(false);
      setEditContact(null);
      await load();
      toast.success("Contact updated");
    } catch {
      toast.error("Failed to update contact");
    }
  };

  const handleAddTag = async (contact) => {
    const value = window.prompt("Enter tag(s), comma separated", "");
    if (value === null) return;
    const existing = Array.isArray(contact.tags) ? contact.tags : [];
    const next = [...new Set([...existing, ...value.split(",").map((x) => x.trim()).filter(Boolean)])];
    try {
      await apiPut(`/api/contacts/${contact.id}`, {
        name: contact.name || "",
        phone: contact.phone || "",
        email: contact.email || "",
        tagsCsv: next.join(","),
        groupId: contact.groupId || null,
        segmentId: contact.segmentId || null,
      });
      await load();
      toast.success("Tag updated");
    } catch {
      toast.error("Failed to update tag");
    }
  };

  const handleBulkAddTag = async () => {
    if (!selectedContacts.length) return;
    const value = window.prompt("Enter tag(s), comma separated", "");
    if (value === null) return;
    const tagParts = value.split(",").map((x) => x.trim()).filter(Boolean);
    if (!tagParts.length) return;
    try {
      const selectedRows = contacts.filter((c) => selectedContacts.includes(c.id));
      await Promise.all(selectedRows.map((c) => {
        const existing = Array.isArray(c.tags) ? c.tags : [];
        const next = [...new Set([...existing, ...tagParts])];
        return apiPut(`/api/contacts/${c.id}`, {
          name: c.name || "",
          phone: c.phone || "",
          email: c.email || "",
          tagsCsv: next.join(","),
          groupId: c.groupId || null,
          segmentId: c.segmentId || null,
        });
      }));
      await load();
      toast.success("Tags updated");
    } catch {
      toast.error("Bulk tag update failed");
    }
  };

  const handleCall = (contact) => {
    window.open(`tel:${contact.phone}`, "_self");
  };

  const handleOpenSendDialog = (contact) => {
    setSendContact(contact);
    setMessageBody("");
    setMessageChannel("whatsapp");
    setShowSendDialog(true);
  };

  const handleSendMessage = async () => {
    if (!sendContact || !messageBody.trim()) return;
    try {
      await apiPost("/api/messages/send", {
        channel: messageChannel === "sms" ? 1 : 2,
        recipient: sendContact.phone,
        body: messageBody.trim(),
        idempotencyKey: buildIdempotencyKey("contact"),
      });
      toast.success("Message sent");
      setShowSendDialog(false);
      setSendContact(null);
    } catch {
      toast.error("Send message failed");
    }
  };

  const handleCreateSegment = async () => {
    const name = newSegmentName.trim();
    if (!name) return;
    try {
      await apiPost("/api/contact-data/segments", { name, ruleJson: "{}" });
      setNewSegmentName("");
      await load();
      toast.success("Segment created");
    } catch {
      toast.error("Failed to create segment");
    }
  };

  return (
    <div className="space-y-6" data-testid="contacts-page">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-heading font-bold text-slate-900">Contacts</h1>
          <p className="text-slate-600">Manage your contacts and audience segments</p>
        </div>
        <div className="flex items-center gap-3">
          <Dialog open={showImportDialog} onOpenChange={setShowImportDialog}>
            <DialogTrigger asChild>
              <Button variant="outline" className="gap-2" data-testid="import-contacts-btn">
                <Upload className="w-4 h-4" />
                Import
              </Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Import Contacts</DialogTitle>
                <DialogDescription>
                  Upload a CSV file to import contacts in bulk
                </DialogDescription>
              </DialogHeader>
              <div className="space-y-4 py-4">
                <div className="border-2 border-dashed border-slate-200 rounded-lg p-8 text-center">
                  <Upload className="w-12 h-12 text-slate-400 mx-auto mb-4" />
                  <p className="text-sm text-slate-600 mb-2">
                    Drag and drop your CSV file here, or click to browse
                  </p>
                  <Button variant="outline" size="sm">Browse Files</Button>
                </div>
                <div className="text-sm text-slate-500">
                  <p className="font-medium mb-2">Required columns:</p>
                  <ul className="list-disc list-inside space-y-1">
                    <li>name - Contact name</li>
                    <li>phone - Phone number with country code</li>
                    <li>email (optional) - Email address</li>
                  </ul>
                </div>
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => setShowImportDialog(false)}>Cancel</Button>
                <Button className="bg-orange-500 hover:bg-orange-600">Upload & Import</Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>

          <Button variant="outline" className="gap-2" data-testid="export-contacts-btn">
            <Download className="w-4 h-4" />
            Export
          </Button>

          <Dialog open={showAddDialog} onOpenChange={setShowAddDialog}>
            <DialogTrigger asChild>
              <Button
                className="bg-orange-500 hover:bg-orange-600 text-white gap-2"
                data-testid="add-contact-btn"
                disabled={!canCreateContact}
                title={!canCreateContact ? "Contact limit reached" : ""}
              >
                <Plus className="w-4 h-4" />
                Add Contact
              </Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Add New Contact</DialogTitle>
                <DialogDescription>
                  Create a new contact in your address book
                </DialogDescription>
              </DialogHeader>
              <div className="space-y-4 py-4">
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label>First Name</Label>
                    <Input placeholder="John" value={newContact.name} onChange={(e) => setNewContact((p) => ({ ...p, name: e.target.value }))} />
                  </div>
                  <div className="space-y-2">
                    <Label>Last Name</Label>
                    <Input placeholder="Doe" />
                  </div>
                </div>
                <div className="space-y-2">
                  <Label>Phone Number</Label>
                  <div className="flex gap-2">
                    <div className="w-20 flex items-center justify-center bg-slate-50 border border-slate-200 rounded-md text-sm">
                      +91
                    </div>
                    <Input placeholder="9876543210" className="flex-1" value={newContact.phone} onChange={(e) => setNewContact((p) => ({ ...p, phone: e.target.value }))} />
                  </div>
                </div>
                <div className="space-y-2">
                  <Label>Email</Label>
                  <Input type="email" placeholder="john@example.com" value={newContact.email} onChange={(e) => setNewContact((p) => ({ ...p, email: e.target.value }))} />
                </div>
                <div className="space-y-2">
                  <Label>Segment</Label>
                  <Select value={newContact.segmentId} onValueChange={(v) => setNewContact((p) => ({ ...p, segmentId: v }))}>
                    <SelectTrigger>
                      <SelectValue placeholder="Select segment" />
                    </SelectTrigger>
                    <SelectContent>
                      {segmentsApi.map((segment) => (
                        <SelectItem key={segment.id} value={segment.id}>
                          {segment.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label>Notes</Label>
                  <Textarea placeholder="Additional notes about this contact" />
                </div>
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => setShowAddDialog(false)}>Cancel</Button>
                <Button className="bg-orange-500 hover:bg-orange-600" onClick={handleAddContact} disabled={!canCreateContact}>Add Contact</Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <Card className="border-slate-200">
          <CardContent className="pt-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-slate-600">Total Contacts</p>
                <p className="text-2xl font-bold text-slate-900">{contacts.length.toLocaleString()}</p>
                <p className="text-xs text-slate-500 mt-1">Plan: {contactsUsed.toLocaleString()} / {contactsLimit > 0 ? contactsLimit.toLocaleString() : "Unlimited"}</p>
              </div>
              <div className="w-10 h-10 bg-orange-100 rounded-lg flex items-center justify-center">
                <Users className="w-5 h-5 text-orange-600" />
              </div>
            </div>
          </CardContent>
        </Card>
        <Card className="border-slate-200">
          <CardContent className="pt-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-slate-600">Opted In</p>
                <p className="text-2xl font-bold text-slate-900">{contacts.filter((c) => c.optInStatus === "opted_in").length.toLocaleString()}</p>
              </div>
              <div className="w-10 h-10 bg-green-100 rounded-lg flex items-center justify-center">
                <MessageSquare className="w-5 h-5 text-green-600" />
              </div>
            </div>
          </CardContent>
        </Card>
        <Card className="border-slate-200">
          <CardContent className="pt-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-slate-600">New This Month</p>
                <p className="text-2xl font-bold text-slate-900">{contacts.length.toLocaleString()}</p>
              </div>
              <div className="w-10 h-10 bg-blue-100 rounded-lg flex items-center justify-center">
                <UserPlus className="w-5 h-5 text-blue-600" />
              </div>
            </div>
          </CardContent>
        </Card>
        <Card className="border-slate-200">
          <CardContent className="pt-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm text-slate-600">Segments</p>
                <p className="text-2xl font-bold text-slate-900">{segments.length}</p>
              </div>
              <div className="w-10 h-10 bg-purple-100 rounded-lg flex items-center justify-center">
                <Tag className="w-5 h-5 text-purple-600" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <div className="grid lg:grid-cols-4 gap-6">
        {/* Segments Sidebar */}
        <div className="lg:col-span-1">
          <Card className="border-slate-200">
            <CardHeader>
              <CardTitle className="text-base">Segments</CardTitle>
            </CardHeader>
            <CardContent className="p-0">
              <div className="space-y-1">
                {segments.map((segment, index) => (
                  <div
                    key={segment.id || segment.name}
                    className={`flex items-center justify-between px-4 py-3 cursor-pointer transition-colors ${
                      index === 0 ? "bg-orange-50 border-l-4 border-l-orange-500" : "hover:bg-slate-50"
                    }`}
                    data-testid={`segment-${index}`}
                  >
                    <span className={`text-sm ${index === 0 ? "text-orange-600 font-medium" : "text-slate-700"}`}>
                      {segment.name}
                    </span>
                    <Badge variant="outline" className="text-xs">
                      {segment.count.toLocaleString()}
                    </Badge>
                  </div>
                ))}
              </div>
              <div className="p-4 border-t border-slate-200">
                <div className="space-y-2">
                  <Input
                    placeholder="New segment name"
                    value={newSegmentName}
                    onChange={(e) => setNewSegmentName(e.target.value)}
                  />
                  <Button variant="outline" size="sm" className="w-full gap-2" onClick={handleCreateSegment}>
                    <Plus className="w-4 h-4" />
                    Create Segment
                  </Button>
                </div>
                <Button variant="outline" size="sm" className="w-full gap-2 mt-2" onClick={load}>
                  <Plus className="w-4 h-4" />
                  Refresh
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Contacts Table */}
        <div className="lg:col-span-3">
          <Card className="border-slate-200">
            <CardHeader className="pb-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                  <div className="relative">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                    <Input
                      placeholder="Search contacts..."
                      className="pl-10 w-64"
                      data-testid="search-contacts"
                    />
                  </div>
                  <Button variant="outline" size="icon" data-testid="filter-contacts-btn">
                    <Filter className="w-4 h-4" />
                  </Button>
                </div>
                {selectedContacts.length > 0 && (
                  <div className="flex items-center gap-2">
                    <span className="text-sm text-slate-600">{selectedContacts.length} selected</span>
                    <Button variant="outline" size="sm" className="gap-2" onClick={handleBulkAddTag}>
                      <Tag className="w-4 h-4" />
                      Add Tag
                    </Button>
                    <Button variant="outline" size="sm" className="gap-2 text-red-600 hover:text-red-700">
                      <Trash2 className="w-4 h-4" />
                      Delete
                    </Button>
                  </div>
                )}
              </div>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-12">
                      <Checkbox
                        checked={selectedContacts.length === contacts.length}
                        onCheckedChange={handleSelectAll}
                        data-testid="select-all-checkbox"
                      />
                    </TableHead>
                    <TableHead>Contact</TableHead>
                    <TableHead>Phone</TableHead>
                    <TableHead>Tags</TableHead>
                    <TableHead>Segment</TableHead>
                    <TableHead>Opt-in</TableHead>
                    <TableHead>Last Activity</TableHead>
                    <TableHead className="w-12"></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {contacts.map((contact) => (
                    <TableRow key={contact.id} className="table-row-hover">
                      <TableCell>
                        <Checkbox
                          checked={selectedContacts.includes(contact.id)}
                          onCheckedChange={(checked) => handleSelectContact(contact.id, checked)}
                          data-testid={`select-contact-${contact.id}`}
                        />
                      </TableCell>
                      <TableCell>
                        <div className="flex items-center gap-3">
                          <Avatar className="w-9 h-9">
                            <AvatarFallback className="bg-gradient-to-br from-orange-400 to-orange-600 text-white text-sm">
                              {contact.name.split(" ").map((n) => n[0]).join("")}
                            </AvatarFallback>
                          </Avatar>
                          <div>
                            <p className="font-medium text-slate-900">{contact.name}</p>
                            <p className="text-sm text-slate-500">{contact.email}</p>
                          </div>
                        </div>
                      </TableCell>
                      <TableCell className="text-slate-600">{contact.phone}</TableCell>
                      <TableCell>
                        <div className="flex gap-1 flex-wrap">
                          {(contact.tags || []).map((tag) => (
                            <Badge key={tag} variant="outline" className="text-xs">
                              {tag}
                            </Badge>
                          ))}
                        </div>
                      </TableCell>
                      <TableCell className="text-slate-600">{contact.segment || "-"}</TableCell>
                      <TableCell>
                        <Badge className={contact.optInStatus === "opted_in" ? "bg-green-100 text-green-700 hover:bg-green-100" : "bg-red-100 text-red-700 hover:bg-red-100"}>
                          {contact.optInStatus === "opted_in" ? "Yes" : "No"}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-slate-500 text-sm">{contact.lastActivity || "-"}</TableCell>
                      <TableCell>
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon" data-testid={`contact-menu-${contact.id}`}>
                              <MoreVertical className="w-4 h-4" />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            <DropdownMenuItem onClick={() => handleEditContact(contact)}>
                              <Edit className="w-4 h-4 mr-2" />
                              Edit
                            </DropdownMenuItem>
                            <DropdownMenuItem onClick={() => handleOpenSendDialog(contact)}>
                              <MessageSquare className="w-4 h-4 mr-2" />
                              Send Message
                            </DropdownMenuItem>
                            <DropdownMenuItem onClick={() => handleCall(contact)}>
                              <Phone className="w-4 h-4 mr-2" />
                              Call
                            </DropdownMenuItem>
                            <DropdownMenuItem onClick={() => handleAddTag(contact)}>
                              <Tag className="w-4 h-4 mr-2" />
                              Add Tag
                            </DropdownMenuItem>
                            <DropdownMenuSeparator />
                            <DropdownMenuItem className="text-red-600" onClick={() => handleDeleteContact(contact.id)}>
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

              {/* Pagination */}
              <div className="flex items-center justify-between mt-4 pt-4 border-t border-slate-200">
                <p className="text-sm text-slate-600">
                  Showing 1-5 of 15,832 contacts
                </p>
                <div className="flex items-center gap-2">
                  <Button variant="outline" size="sm" disabled>
                    <ChevronLeft className="w-4 h-4" />
                  </Button>
                  <Button variant="outline" size="sm" className="bg-orange-500 text-white hover:bg-orange-600">1</Button>
                  <Button variant="outline" size="sm">2</Button>
                  <Button variant="outline" size="sm">3</Button>
                  <span className="text-slate-400">...</span>
                  <Button variant="outline" size="sm">100</Button>
                  <Button variant="outline" size="sm">
                    <ChevronRight className="w-4 h-4" />
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>

      <Dialog open={showEditDialog} onOpenChange={setShowEditDialog}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Edit Contact</DialogTitle>
          </DialogHeader>
          {editContact && (
            <div className="space-y-3">
              <div><Label>Name</Label><Input value={editContact.name} onChange={(e) => setEditContact((p) => ({ ...p, name: e.target.value }))} /></div>
              <div><Label>Phone</Label><Input value={editContact.phone} onChange={(e) => setEditContact((p) => ({ ...p, phone: e.target.value }))} /></div>
              <div><Label>Email</Label><Input value={editContact.email} onChange={(e) => setEditContact((p) => ({ ...p, email: e.target.value }))} /></div>
              <div>
                <Label>Segment</Label>
                <Select value={editContact.segmentId || ""} onValueChange={(v) => setEditContact((p) => ({ ...p, segmentId: v }))}>
                  <SelectTrigger><SelectValue placeholder="Select segment" /></SelectTrigger>
                  <SelectContent>
                    {segmentsApi.map((segment) => <SelectItem key={segment.id} value={segment.id}>{segment.name}</SelectItem>)}
                  </SelectContent>
                </Select>
              </div>
              <div><Label>Tags (comma separated)</Label><Input value={editContact.tagsCsv || ""} onChange={(e) => setEditContact((p) => ({ ...p, tagsCsv: e.target.value }))} /></div>
            </div>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowEditDialog(false)}>Cancel</Button>
            <Button className="bg-orange-500 hover:bg-orange-600" onClick={handleUpdateContact}>Save</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={showSendDialog} onOpenChange={setShowSendDialog}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Send Message</DialogTitle>
            <DialogDescription>{sendContact?.name} ({sendContact?.phone})</DialogDescription>
          </DialogHeader>
          <div className="space-y-3">
            <div>
              <Label>Channel</Label>
              <Select value={messageChannel} onValueChange={setMessageChannel}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="whatsapp">WhatsApp</SelectItem>
                  <SelectItem value="sms">SMS</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label>Message</Label>
              <Textarea rows={4} value={messageBody} onChange={(e) => setMessageBody(e.target.value)} />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowSendDialog(false)}>Cancel</Button>
            <Button className="bg-orange-500 hover:bg-orange-600" onClick={handleSendMessage}>Send</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
};

export default ContactsPage;
