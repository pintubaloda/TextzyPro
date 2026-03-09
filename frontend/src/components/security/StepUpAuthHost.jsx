import { useEffect, useRef, useState } from "react";
import { ShieldCheck, Smartphone } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { registerStepUpUiHandler } from "@/lib/api";

const initialState = {
  open: false,
  title: "Additional verification required",
  message: "Enter the code from your authenticator app.",
  provider: "",
  errorMessage: "",
  code: "",
};

export default function StepUpAuthHost() {
  const [state, setState] = useState(initialState);
  const resolverRef = useRef({ resolve: null, reject: null });

  useEffect(() => {
    const unregister = registerStepUpUiHandler((payload = {}) => new Promise((resolve, reject) => {
      resolverRef.current = { resolve, reject };
      setState({
        open: true,
        title: payload.title || initialState.title,
        message: payload.message || initialState.message,
        provider: payload.provider || "",
        errorMessage: payload.errorMessage || "",
        code: "",
      });
    }));
    return () => unregister();
  }, []);

  const closeWithError = (message = "Verification cancelled.") => {
    const reject = resolverRef.current.reject;
    resolverRef.current = { resolve: null, reject: null };
    setState(initialState);
    if (reject) reject(new Error(message));
  };

  const submit = () => {
    const resolve = resolverRef.current.resolve;
    resolverRef.current = { resolve: null, reject: null };
    const code = state.code.trim();
    setState(initialState);
    if (resolve) resolve(code);
  };

  return (
    <Dialog open={state.open} onOpenChange={(open) => { if (!open) closeWithError(); }}>
      <DialogContent className="max-w-md overflow-hidden border-0 bg-transparent p-0 shadow-none">
        <div className="rounded-3xl border border-orange-100 bg-white shadow-2xl">
          <div className="bg-gradient-to-r from-slate-950 via-slate-900 to-slate-800 px-6 py-5 text-white">
            <div className="flex items-start gap-3">
              <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-orange-500/95 shadow-lg">
                <ShieldCheck className="h-6 w-6" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <DialogTitle className="text-xl font-semibold">{state.title}</DialogTitle>
                  <Badge className="border border-white/15 bg-white/10 text-white hover:bg-white/10">
                    {String(state.provider || "authenticator").replace(/_/g, " ")}
                  </Badge>
                </div>
                <DialogDescription className="mt-1 text-sm text-slate-300">
                  {state.message}
                </DialogDescription>
              </div>
            </div>
          </div>

          <div className="space-y-4 px-6 py-5">
            <div className="rounded-2xl border border-orange-100 bg-orange-50/70 p-4 text-sm text-slate-700">
              Open Google Authenticator or Microsoft Authenticator and enter the current 6-digit code.
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium text-slate-900">Authenticator code</label>
              <div className="relative">
                <Smartphone className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <Input
                  inputMode="numeric"
                  autoFocus
                  maxLength={6}
                  className="h-12 rounded-2xl border-slate-200 pl-10 text-base tracking-[0.24em]"
                  placeholder="000000"
                  value={state.code}
                  onChange={(e) => setState((prev) => ({ ...prev, code: e.target.value }))}
                />
              </div>
              {state.errorMessage ? (
                <p className="text-sm text-red-600">{state.errorMessage}</p>
              ) : null}
            </div>
          </div>

          <DialogFooter className="border-t border-slate-100 px-6 py-4 sm:justify-between">
            <Button variant="outline" className="rounded-xl" onClick={() => closeWithError()}>
              Cancel
            </Button>
            <Button
              className="rounded-xl bg-orange-500 hover:bg-orange-600"
              onClick={submit}
              disabled={!state.code.trim()}
            >
              Verify and continue
            </Button>
          </DialogFooter>
        </div>
      </DialogContent>
    </Dialog>
  );
}
