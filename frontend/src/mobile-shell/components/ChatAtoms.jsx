import { C } from "../core";

export const Avatar = ({ name, color, size = 46, online = false }) => (
  <div style={{ position: "relative", flexShrink: 0 }}>
    <div
      style={{
        width: size,
        height: size,
        borderRadius: "50%",
        background: `linear-gradient(135deg,${color}EE,${color}88)`,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: size * 0.34,
        fontWeight: 700,
        color: "#fff",
        boxShadow: `0 2px 8px ${color}44`,
      }}
    >
      {name
        .replace(/[^\w\s]/gi, "")
        .trim()
        .split(" ")
        .map((w) => w[0])
        .join("")
        .slice(0, 2)
        .toUpperCase() || "?"}
    </div>
    {online ? (
      <div
        style={{
          position: "absolute",
          bottom: 1,
          right: 1,
          width: size * 0.27,
          height: size * 0.27,
          borderRadius: "50%",
          background: C.online,
          border: "2.5px solid #fff",
          boxShadow: `0 0 0 1px ${C.online}44`,
        }}
      />
    ) : null}
  </div>
);

export const Typing = () => (
  <div style={{ display: "flex", gap: 5, alignItems: "center", padding: "11px 15px" }}>
    {[0, 1, 2].map((i) => (
      <div
        key={i}
        style={{
          width: 7,
          height: 7,
          borderRadius: "50%",
          background: C.orange,
          opacity: 0.7,
          animation: `tdot 1.2s ease-in-out ${i * 0.2}s infinite`,
        }}
      />
    ))}
  </div>
);
