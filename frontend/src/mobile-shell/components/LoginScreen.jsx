import { useState, useRef, useEffect, useCallback } from "react";
import { C, parsePairingToken } from "../core";
import { Logo, I } from "../uiAssets";
const Scanner = ({ onDone }) => {
  const [pct, setPct]   = useState(0);
  const [done, setDone] = useState(false);
  const [camErr, setCamErr] = useState("");
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  const rafRef = useRef(0);

  useEffect(() => {
    let active = true;
    let stream = null;
    let detector = null;
    let useJsQrFallback = false;
    const w = window;

    const stop = () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
      if (stream) stream.getTracks().forEach((t) => t.stop());
    };

    const scanLoop = async () => {
      if (!active) return;
      setPct((p) => (p >= 99 ? 8 : p + 1.1));
      try {
        const video = videoRef.current;
        if (detector && video && video.readyState >= 2) {
          const found = await detector.detect(video);
          if (found?.length) {
            const rawValue = found[0].rawValue || "";
            setDone(true);
            stop();
            setTimeout(() => onDone?.(rawValue), 700);
            return;
          }
        } else if (useJsQrFallback && video && video.readyState >= 2 && w.jsQR) {
          const canvas = canvasRef.current;
          if (canvas) {
            const size = 360;
            canvas.width = size;
            canvas.height = size;
            const ctx = canvas.getContext("2d", { willReadFrequently: true });
            if (ctx) {
              ctx.drawImage(video, 0, 0, size, size);
              const image = ctx.getImageData(0, 0, size, size);
              const code = w.jsQR(image.data, image.width, image.height, {
                inversionAttempts: "dontInvert",
              });
              if (code?.data) {
                setDone(true);
                stop();
                setTimeout(() => onDone?.(code.data), 700);
                return;
              }
            }
          }
        }
      } catch {
        // keep scanning
      }
      rafRef.current = requestAnimationFrame(scanLoop);
    };

    const ensureJsQr = () =>
      new Promise((resolve) => {
        if (w.jsQR) {
          resolve(true);
          return;
        }
        const id = "jsqr-lib-script";
        const existing = document.getElementById(id);
        if (existing) {
          existing.addEventListener("load", () => resolve(true), { once: true });
          existing.addEventListener("error", () => resolve(false), { once: true });
          return;
        }
        const script = document.createElement("script");
        script.id = id;
        script.src = "https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js";
        script.async = true;
        script.onload = () => resolve(true);
        script.onerror = () => resolve(false);
        document.head.appendChild(script);
      });

    const start = async () => {
      try {
        if (!navigator.mediaDevices?.getUserMedia) {
          setCamErr("Camera not available on this device.");
          return;
        }
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: "environment" } },
          audio: false,
        });
        const video = videoRef.current;
        if (!video) return;
        video.srcObject = stream;
        await video.play();

        if ("BarcodeDetector" in w) {
          detector = new w.BarcodeDetector({ formats: ["qr_code"] });
        } else {
          const ok = await ensureJsQr();
          if (ok && w.jsQR) {
            useJsQrFallback = true;
          } else {
            setCamErr("QR scanner not supported here. Use manual token.");
          }
        }
        rafRef.current = requestAnimationFrame(scanLoop);
      } catch {
        setCamErr("Camera permission denied or unavailable.");
      }
    };

    start();
    return () => {
      active = false;
      stop();
    };
  }, [onDone]);

  return (
    <div style={{
      width:"100%", aspectRatio:"1/1", borderRadius:20,
      overflow:"hidden", position:"relative",
      background: done
        ? `linear-gradient(135deg,#052e16,#14532d)`
        : `linear-gradient(160deg,#0c2340 0%,#1E3A5F 50%,#0d1f38 100%)`,
      transition:"background 0.5s",
    }}>
      <video
        ref={videoRef}
        muted
        playsInline
        autoPlay
        style={{
          position: "absolute",
          inset: 0,
          width: "100%",
          height: "100%",
          objectFit: "cover",
          opacity: done ? 0 : 0.55,
        }}
      />
      <canvas ref={canvasRef} style={{ display: "none" }} />

      {/* animated scan line */}
      {!done && (
        <div style={{
          position:"absolute", left:"12%", right:"12%", height:2.5,
          background:`linear-gradient(90deg,transparent,${C.orange},transparent)`,
          top:`${12+(pct*0.76)}%`,
          boxShadow:`0 0 14px ${C.orange}BB`,
          transition:"top 0.05s linear",
        }}/>
      )}

      {/* corner brackets */}
      {!done && ["tl","tr","bl","br"].map(pos=>(
        <div key={pos} style={{
          position:"absolute",
          ...(pos.includes("t")?{top:"11%"}:{bottom:"11%"}),
          ...(pos.includes("l")?{left:"11%"}:{right:"11%"}),
          width:34, height:34,
          borderTop:   pos.includes("t")?`3px solid ${C.orange}`:"none",
          borderBottom:pos.includes("b")?`3px solid ${C.orange}`:"none",
          borderLeft:  pos.includes("l")?`3px solid ${C.orange}`:"none",
          borderRight: pos.includes("r")?`3px solid ${C.orange}`:"none",
          borderRadius:pos==="tl"?"6px 0 0 0":pos==="tr"?"0 6px 0 0":pos==="bl"?"0 0 0 6px":"0 0 6px 0",
        }}/>
      ))}

      {/* dashed frame */}
      {!done && (
        <div style={{
          position:"absolute", top:"11%", left:"11%", right:"11%", bottom:"11%",
          border:`1.5px dashed ${C.orange}66`, borderRadius:14,
        }}/>
      )}

      {/* success */}
      {done && (
        <div style={{
          position:"absolute", inset:0,
          display:"flex", flexDirection:"column", alignItems:"center", justifyContent:"center", gap:10,
          animation:"fadeUp 0.3s ease-out",
        }}>
          <div style={{
            width:68, height:68, borderRadius:"50%", background:C.online,
            display:"flex", alignItems:"center", justifyContent:"center",
            boxShadow:`0 0 28px ${C.online}88`,
          }}>
            <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="20 6 9 17 4 12"/>
            </svg>
          </div>
          <p style={{ color:"#fff", fontWeight:700, fontSize:16, margin:0 }}>QR Detected!</p>
          <p style={{ color:"rgba(255,255,255,0.7)", fontSize:13, margin:0 }}>Logging you in...</p>
        </div>
      )}

      {/* label */}
      {!done && (
        <div style={{ position:"absolute", bottom:16, left:0, right:0, textAlign:"center" }}>
          <p style={{ color:"rgba(255,255,255,0.9)", fontSize:13, fontWeight:500, margin:0 }}>
            Point camera at the QR on your computer
          </p>
          <div style={{
            display:"inline-flex", alignItems:"center", gap:5,
            marginTop:5, padding:"3px 12px",
            background:"rgba(249,115,22,0.2)", borderRadius:10,
          }}>
            <div style={{ width:6,height:6,borderRadius:"50%",background:C.orange,animation:"pulse 1s ease-in-out infinite" }}/>
            <span style={{ color:C.orange, fontSize:11, fontWeight:600 }}>Scanning {Math.round(pct)}%</span>
          </div>
          {camErr ? (
            <p style={{ color:"rgba(255,255,255,0.88)", fontSize:11, margin:"6px 0 0" }}>{camErr}</p>
          ) : null}
        </div>
      )}
    </div>
  );
};

/* ══════════════════════════════════════
   SCREEN 1 — MOBILE LOGIN
   Orange gradient bg · no black
══════════════════════════════════════ */
const LoginScreen = ({ onLogin }) => {
  const [tab,setTab]     = useState("password");
  const [email,setEmail] = useState("admin@textzy.io");
  const [pass,setPass]   = useState("password123");
  const [otp,setOtp]     = useState("");
  const [verificationId,setVerificationId] = useState("");
  const [verificationState,setVerificationState] = useState("");
  const [otpSent,setOtpSent] = useState(false);
  const [otpReady,setOtpReady] = useState(false);
  const [otpVerified,setOtpVerified] = useState(false);
  const [otpBusy,setOtpBusy] = useState(false);
  const [otpStatusBusy,setOtpStatusBusy] = useState(false);
  const [verifyBusy,setVerifyBusy] = useState(false);
  const [twoFactor,setTwoFactor] = useState({ challengeToken:"", provider:"", expiresAtUtc:"", code:"", busy:false });
  const [showPass,setShowPass] = useState(false);
  const [loading,setLoad]= useState(false);
  const [err,setErr]     = useState("");

  const submit = async () => {
    if (!email||!pass) { setErr("Please fill all fields."); return; }
    if (!otpVerified) { setErr("Please verify email OTP first."); return; }
    setLoad(true);
    setErr("");
    try {
      const result = await onLogin({ mode: "password", email, password: pass, emailVerificationId: verificationId });
      if (result?.requiresTwoFactor) {
        setTwoFactor({
          challengeToken: result.challengeToken || "",
          provider: result.provider || "authenticator",
          expiresAtUtc: result.expiresAtUtc || "",
          code: "",
          busy: false,
        });
      }
    } catch (e) {
      setErr(e?.message || "Login failed.");
    } finally {
      setLoad(false);
    }
  };

  const requestOtp = async () => {
    if (!email) { setErr("Enter email first."); return; }
    setErr("");
    setOtpBusy(true);
    try {
      const data = await onLogin({ mode: "request-otp", email });
      setVerificationId(data?.verificationId || "");
      setVerificationState(data?.state || "waiting_user_action");
      setOtp("");
      setOtpSent(true);
      setOtpReady(false);
      setOtpVerified(false);
    } catch (e) {
      setErr(e?.message || "Failed to send OTP.");
    } finally {
      setOtpBusy(false);
    }
  };

  const refreshOtpStatus = useCallback(async () => {
    if (!verificationId) return;
    setOtpStatusBusy(true);
    try {
      const status = await onLogin({ mode: "otp-status", verificationId, email });
      const state = status?.state || "";
      setVerificationState(state);
      const ready = state === "otp_ready" || state === "verified";
      setOtpReady(ready);
      if (state === "verified") setOtpVerified(true);
    } catch (e) {
      setErr(e?.message || "Failed to check verification status.");
    } finally {
      setOtpStatusBusy(false);
    }
  }, [verificationId, onLogin, email]);

  useEffect(() => {
    if (!otpSent || !verificationId || otpReady || otpVerified) return;
    const timer = setInterval(() => {
      refreshOtpStatus();
    }, 2500);
    return () => clearInterval(timer);
  }, [otpSent, verificationId, otpReady, otpVerified, refreshOtpStatus]);

  const verifyOtp = async () => {
    if (!verificationId || !otp) { setErr("Enter OTP first."); return; }
    setErr("");
    setVerifyBusy(true);
    try {
      await onLogin({ mode: "verify-otp", email, verificationId, otp });
      setOtpVerified(true);
    } catch (e) {
      setErr(e?.message || "Invalid OTP.");
    } finally {
      setVerifyBusy(false);
    }
  };

  const verifyAuthenticator = async () => {
    if (!twoFactor.challengeToken || !twoFactor.code.trim()) { setErr("Enter authenticator code first."); return; }
    setErr("");
    setTwoFactor((prev) => ({ ...prev, busy: true }));
    try {
      await onLogin({
        mode: "verify-authenticator",
        email,
        challengeToken: twoFactor.challengeToken,
        code: twoFactor.code.trim(),
      });
    } catch (e) {
      setErr(e?.message || "Authenticator verification failed.");
      setTwoFactor((prev) => ({ ...prev, busy: false }));
      return;
    }
    setTwoFactor({ challengeToken:"", provider:"", expiresAtUtc:"", code:"", busy:false });
  };

  return (
    <div style={{
      minHeight:"100vh", display:"flex", flexDirection:"column",
      background:`radial-gradient(1200px 460px at 50% -10%, rgba(255,255,255,0.2), transparent 60%), linear-gradient(165deg, ${C.orange} 0%, #EA6C0A 35%, #C2560A 75%, #1E3A5F 100%)`,
      fontFamily:"'Segoe UI',system-ui,sans-serif",
    }}>
      {/* top hero */}
      <div style={{
        flex:"0 0 22vh", display:"flex", flexDirection:"column",
        alignItems:"center", justifyContent:"center",
        padding:"8px 20px 2px",
      }}>
        <div style={{
          width:52, height:52, borderRadius:15,
          background:"rgba(255,255,255,0.18)",
          backdropFilter:"blur(8px)",
          display:"flex", alignItems:"center", justifyContent:"center",
          marginBottom:6,
          boxShadow:"0 8px 32px rgba(0,0,0,0.12)",
        }}>
          <Logo size={30}/>
        </div>
        <h1 style={{ margin:0, fontSize:22, fontWeight:800, color:"#fff", letterSpacing:"-0.5px" }}>Textzy</h1>
        <p style={{ margin:"1px 0 0", color:"rgba(255,255,255,0.75)", fontSize:12.5 }}>Business Inbox</p>
      </div>

      {/* card */}
      <div style={{
        flex:1,
        minHeight:"78vh",
        background:"#fff", borderRadius:"28px 28px 0 0",
        padding:"20px 22px 38px",
        boxShadow:"0 -14px 46px rgba(0,0,0,0.16)",
        borderTop:"1px solid rgba(255,255,255,0.45)",
      }}>
        {/* tab switcher */}
        <div style={{ display:"flex", background:C.panelBg, borderRadius:12, padding:4, marginBottom:22 }}>
          {[{ mode: "password", label: "Password", icon: <I.Key/> }, { mode: "qr", label: "Scan QR", icon: <I.Camera/> }].map((item)=>(
            <button key={item.mode} onClick={()=>{setTab(item.mode);setErr("");}} style={{
              flex:1, padding:"10px 0", border:"none", borderRadius:9,
              background:tab===item.mode?"#fff":"transparent",
              color:tab===item.mode?C.orange:C.textSub,
              fontWeight:tab===item.mode?700:500, fontSize:14,
              cursor:"pointer", fontFamily:"inherit",
              boxShadow:tab===item.mode?"0 1px 6px rgba(0,0,0,0.10)":"none",
              transition:"all 0.2s",
              display:"flex", alignItems:"center", justifyContent:"center", gap:8,
            }}>
              {item.icon}
              <span>{item.label}</span>
            </button>
          ))}
        </div>

        {tab==="password" ? (
          <>
            <div style={{ marginBottom:14 }}>
              <label style={{ display:"block",fontSize:11,fontWeight:700,color:C.textSub,marginBottom:5,textTransform:"uppercase",letterSpacing:"0.7px" }}>Email</label>
              <input
                type="email"
                value={email}
                placeholder="you@company.com"
                onChange={e=>{
                  setEmail(e.target.value);
                  setErr("");
                  setOtp("");
                  setOtpSent(false);
                  setOtpReady(false);
                  setOtpVerified(false);
                  setVerificationId("");
                  setVerificationState("");
                  setTwoFactor({ challengeToken:"", provider:"", expiresAtUtc:"", code:"", busy:false });
                }}
                onKeyDown={e=>e.key==="Enter"&&submit()}
                style={{
                  width:"100%", padding:"13px 15px", borderRadius:12, boxSizing:"border-box",
                  border:`1.5px solid ${C.divider}`, fontSize:15, color:C.textMain,
                  outline:"none", fontFamily:"inherit", transition:"border-color 0.2s",
                  background:"#fff",
                }}
                onFocus={e=>e.target.style.borderColor=C.orange}
                onBlur={e=>e.target.style.borderColor=C.divider}
              />
            </div>
            <div style={{ marginBottom:14 }}>
              <label style={{ display:"block",fontSize:11,fontWeight:700,color:C.textSub,marginBottom:5,textTransform:"uppercase",letterSpacing:"0.7px" }}>Password</label>
              <div style={{ position:"relative" }}>
                <input
                  type={showPass ? "text" : "password"}
                  value={pass}
                  placeholder="********"
                  onChange={e=>{setPass(e.target.value);setErr("");}}
                  onKeyDown={e=>e.key==="Enter"&&submit()}
                  style={{
                    width:"100%", padding:"13px 72px 13px 15px", borderRadius:12, boxSizing:"border-box",
                    border:`1.5px solid ${C.divider}`, fontSize:15, color:C.textMain,
                    outline:"none", fontFamily:"inherit", transition:"border-color 0.2s",
                    background:"#fff",
                  }}
                  onFocus={e=>e.target.style.borderColor=C.orange}
                  onBlur={e=>e.target.style.borderColor=C.divider}
                />
                <button
                  type="button"
                  onClick={()=>setShowPass(v=>!v)}
                  style={{
                    position:"absolute", right:10, top:"50%", transform:"translateY(-50%)",
                    border:"none", background:"transparent", color:C.textSub, cursor:"pointer",
                    padding:"4px 6px", display:"flex", alignItems:"center", justifyContent:"center",
                  }}
                >
                  {showPass ? <I.EyeOff/> : <I.Eye/>}
                </button>
              </div>
            </div>
            {!twoFactor.challengeToken ? (
              <>
                <div style={{ display:"grid", gridTemplateColumns: otpReady ? "1fr 92px 92px" : "1fr", gap:8, marginBottom:10 }}>
                  <button onClick={requestOtp} disabled={otpBusy} style={{ padding:"10px 12px",borderRadius:10,border:`1px solid ${C.divider}`,background:"#fff",fontWeight:700,color:C.textMain,cursor:otpBusy?"not-allowed":"pointer" }}>
                    {otpBusy ? "Sending..." : "Verify Email"}
                  </button>
                  {otpReady ? (
                    <>
                      <input
                        value={otp}
                        onChange={(e)=>setOtp(e.target.value)}
                        placeholder="OTP"
                        style={{ padding:"10px 10px",borderRadius:10,border:`1px solid ${C.divider}`,fontSize:13 }}
                      />
                      <button onClick={verifyOtp} disabled={verifyBusy || !otpSent} style={{ padding:"10px 12px",borderRadius:10,border:"none",background:C.orange,color:"#fff",fontWeight:700,cursor:(verifyBusy || !otpSent)?"not-allowed":"pointer",opacity:(verifyBusy || !otpSent)?0.8:1 }}>
                        {verifyBusy ? "..." : "Verify"}
                      </button>
                    </>
                  ) : null}
                </div>
                {otpSent && (
                  <p style={{ fontSize:12, color: otpVerified ? C.online : C.textSub, margin:"0 0 8px" }}>
                    {otpVerified
                      ? "Email verified successfully."
                      : otpReady
                        ? "Verification link confirmed. Enter OTP from email tab."
                        : "Waiting for user action. Check your email and click Verify Now."}
                  </p>
                )}
              </>
            ) : (
              <div style={{ marginBottom:14, padding:"14px", borderRadius:16, border:`1px solid ${C.orangeLight2}`, background:"linear-gradient(180deg,#fff7ed 0%,#ffffff 100%)", boxShadow:"0 8px 22px rgba(249,115,22,0.08)" }}>
                <div style={{ display:"flex", alignItems:"center", gap:10, marginBottom:10 }}>
                  <div style={{ width:34, height:34, borderRadius:12, background:C.orangePale, color:C.orange, display:"flex", alignItems:"center", justifyContent:"center" }}>
                    <I.Shield/>
                  </div>
                  <div>
                    <div style={{ fontWeight:800, fontSize:14, color:C.textMain }}>Two-factor authentication</div>
                    <div style={{ fontSize:12, color:C.textSub }}>Enter the 6-digit code from your {String(twoFactor.provider || "authenticator").replace(/_/g, " ")} app.</div>
                  </div>
                </div>
                {twoFactor.expiresAtUtc ? (
                  <p style={{ margin:"0 0 10px", fontSize:12, color:C.textSub }}>
                    Challenge expires at {new Date(twoFactor.expiresAtUtc).toLocaleTimeString()}
                  </p>
                ) : null}
                <div style={{ display:"grid", gridTemplateColumns:"1fr 120px", gap:8 }}>
                  <input
                    value={twoFactor.code}
                    onChange={(e)=>setTwoFactor((prev) => ({ ...prev, code: e.target.value }))}
                    placeholder="Authenticator code"
                    inputMode="numeric"
                    style={{ padding:"11px 12px",borderRadius:10,border:`1px solid ${C.divider}`,fontSize:14 }}
                  />
                  <button onClick={verifyAuthenticator} disabled={twoFactor.busy} style={{ padding:"10px 12px",borderRadius:10,border:"none",background:C.orange,color:"#fff",fontWeight:700,cursor:twoFactor.busy?"not-allowed":"pointer",opacity:twoFactor.busy?0.8:1 }}>
                    {twoFactor.busy ? "Checking..." : "Verify"}
                  </button>
                </div>
              </div>
            )}
            {err&&<p style={{ color:C.danger,fontSize:13,marginBottom:10,textAlign:"center" }}>{err}</p>}
            <button onClick={submit} disabled={loading || !!twoFactor.challengeToken} style={{
              width:"100%", padding:15, borderRadius:14, border:"none",
              background:`linear-gradient(135deg,${C.orange},${C.orangeLight})`,
              color:"#fff", fontWeight:700, fontSize:16,
              cursor:(loading || twoFactor.challengeToken)?"not-allowed":"pointer", fontFamily:"inherit",
              display:"flex", alignItems:"center", justifyContent:"center", gap:8,
              boxShadow:`0 6px 24px ${C.orange}55`,
              opacity:(loading || twoFactor.challengeToken)?0.85:1, transition:"opacity 0.2s",
            }}>
              {loading
                ? <><div style={{ width:20,height:20,border:"2.5px solid rgba(255,255,255,0.3)",borderTopColor:"#fff",borderRadius:"50%",animation:"spin 0.7s linear infinite" }}/>Signing in...</>
                : twoFactor.challengeToken
                  ? <><span>Awaiting authenticator verification</span><I.Shield/></>
                  : <><span>Sign In</span><I.ArrowRight/></>}
            </button>
            <p style={{ textAlign:"center",marginTop:16,fontSize:12,color:C.textMuted }}>
              <span style={{ display:"inline-flex",alignItems:"center",gap:6 }}><I.Shield/>Secure session | HTTPS only</span>
            </p>
            <p style={{ textAlign:"center",marginTop:8,fontSize:12,color:C.textMuted }}>
              Powered By - Moneyart Private Limited
            </p>
          </>
        ) : (
          /* QR SCANNER */
          <div>
            <div style={{
              background:C.orangePale, borderRadius:12, padding:"11px 14px",
              marginBottom:16, display:"flex", alignItems:"center", gap:10,
            }}>
              <span style={{ display:"inline-flex", color:C.orange }}><I.Camera/></span>
              <div>
                <div style={{ fontWeight:700,fontSize:13,color:C.textMain }}>Scan from your computer</div>
                <div style={{ fontSize:11,color:C.textSub,marginTop:1 }}>Textzy web -> Link Mobile -> QR appears</div>
              </div>
            </div>
            <Scanner onDone={async (raw)=>{
              try {
                const pairingToken = parsePairingToken(raw);
                if (!pairingToken) throw new Error("Could not read pairing token from QR. Please regenerate QR and scan again.");
                await onLogin({ mode: "qr", pairingToken });
              } catch (e) {
                setErr(e?.message || "QR login failed.");
              }
            }}/>
            <p style={{ textAlign:"center",marginTop:12,fontSize:12,color:C.textMuted }}>
              One-time token | auto-expires in 3 min
            </p>
          </div>
        )}
      </div>
      <style>{`@keyframes spin{to{transform:rotate(360deg)}}@keyframes fadeUp{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:translateY(0)}}@keyframes pulse{0%,100%{opacity:1}50%{opacity:0.4}}`}</style>
    </div>
  );
};

/* ══════════════════════════════════════
   SCREEN 2 — PROJECT PICKER
══════════════════════════════════════ */

export { LoginScreen };

