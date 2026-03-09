import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { createPairingQr, getApiBase, getConnectedDevices, getSession, removeConnectedDevice } from "@/lib/api";

const MobileDevicesPage = () => {
  const [loading, setLoading] = useState(false);
  const [devices, setDevices] = useState([]);
  const [pairing, setPairing] = useState(null);
  const [qrImageUrl, setQrImageUrl] = useState("");

  const currentTenantSlug = useMemo(() => getSession().tenantSlug || "", []);

  const loadDevices = async () => {
    try {
      const rows = await getConnectedDevices();
      setDevices(Array.isArray(rows) ? rows : []);
    } catch (e) {
      toast.error(e?.message || "Failed to load connected devices");
    }
  };

  useEffect(() => {
    loadDevices();
  }, []);

  const handleGenerateQr = async () => {
    try {
      setLoading(true);
      const data = await createPairingQr({ buildHint: "web" });
      setPairing(data || null);
      const token = String(data?.pairingToken || "").trim();
      if (!token) throw new Error("Pairing token missing in response.");
      const apiBase = getApiBase();
      const cacheBust = Date.now();
      setQrImageUrl(`${apiBase}/api/auth/devices/pair-qr-image?pairingToken=${encodeURIComponent(token)}&_=${cacheBust}`);
      toast.success("Pairing QR generated");
    } catch (e) {
      setPairing(null);
      setQrImageUrl("");
      toast.error(e?.message || "Failed to generate pairing QR");
    } finally {
      setLoading(false);
    }
  };

  const handleRemove = async (deviceId) => {
    try {
      await removeConnectedDevice(deviceId);
      await loadDevices();
      toast.success("Device removed");
    } catch (e) {
      toast.error(e?.message || "Failed to remove device");
    }
  };

  return (
    <div className="space-y-4" data-testid="mobile-devices-page">
      <div>
        <h1 className="text-2xl font-semibold text-slate-900">Connect Mobile Device</h1>
        <p className="text-sm text-slate-500">
          Generate a one-time QR, scan in app, and manage connected devices for tenant: <strong>{currentTenantSlug || "n/a"}</strong>.
        </p>
      </div>

      <Card className="border-slate-200">
        <CardHeader>
          <CardTitle>Pair Using QR</CardTitle>
          <CardDescription>QR is one-time and expires automatically.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <Button className="bg-orange-500 hover:bg-orange-600" disabled={loading} onClick={handleGenerateQr}>
            {loading ? "Generating..." : "Generate Connect QR"}
          </Button>

          {pairing && qrImageUrl ? (
            <div className="grid gap-3 md:grid-cols-[340px_1fr]">
              <div className="rounded-lg border border-slate-200 p-3 bg-white">
                <img
                  src={qrImageUrl}
                  alt="Mobile pairing QR"
                  className="h-[320px] w-[320px]"
                  loading="eager"
                  referrerPolicy="no-referrer"
                  onError={() => toast.error("QR image failed to load. Generate a new code.")}
                />
              </div>
              <div className="space-y-2 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm">
                <p><strong>Expires:</strong> {pairing?.expiresAtUtc ? new Date(pairing.expiresAtUtc).toLocaleString() : "-"}</p>
                <p><strong>Device Limit:</strong> {pairing?.maxDevicesPerUser ?? "-"} (active: {pairing?.activeDeviceCount ?? "-"})</p>
                <p><strong>Pairing Token:</strong> <code className="break-all">{pairing?.pairingToken || "-"}</code></p>
              </div>
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card className="border-slate-200">
        <CardHeader>
          <CardTitle>Connected Devices</CardTitle>
          <CardDescription>Only active devices for the current project are shown.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 text-left text-slate-500">
                  <th className="px-2 py-2">Device</th>
                  <th className="px-2 py-2">Platform</th>
                  <th className="px-2 py-2">App Version</th>
                  <th className="px-2 py-2">Last Seen</th>
                  <th className="px-2 py-2">Action</th>
                </tr>
              </thead>
              <tbody>
                {devices.map((d) => (
                  <tr key={d.id} className="border-b border-slate-100">
                    <td className="px-2 py-2">{d.deviceName || "-"}</td>
                    <td className="px-2 py-2">{d.platform || "-"}</td>
                    <td className="px-2 py-2">{d.appVersion || "-"}</td>
                    <td className="px-2 py-2">{d.lastSeenAtUtc ? new Date(d.lastSeenAtUtc).toLocaleString() : "-"}</td>
                    <td className="px-2 py-2">
                      <Button variant="outline" onClick={() => handleRemove(d.id)}>Remove</Button>
                    </td>
                  </tr>
                ))}
                {devices.length === 0 ? (
                  <tr>
                    <td className="px-2 py-3 text-slate-500" colSpan={5}>No connected devices yet.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};

export default MobileDevicesPage;
