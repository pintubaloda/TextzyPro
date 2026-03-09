export default function ProfileView(props) {
  const {
    C,
    I,
    uname,
    user,
    project,
    projectBadge,
    contacts,
    setView,
    setTab,
    setAId,
    handleSetLabels,
    openDevicesModal,
    setShowSettingsModal,
    setShowNotificationsModal,
    openProjectSwitch,
    onLogout,
    Avatar,
    sharedDialogsNode,
  } = props;

  return (
    <div style={{ minHeight: "100vh", fontFamily: "'Segoe UI',system-ui,sans-serif", background: "#fff" }}>
      <div
        style={{
          background: `linear-gradient(135deg,${C.orange},${C.orangeLight})`,
          padding: "52px 20px 28px",
          display: "flex",
          alignItems: "flex-end",
          gap: 14,
        }}
      >
        <button
          onClick={() => setView("list")}
          style={{
            position: "absolute",
            top: 16,
            left: 16,
            background: "rgba(255,255,255,0.2)",
            border: "none",
            color: "#fff",
            padding: "8px",
            borderRadius: 10,
            cursor: "pointer",
            display: "flex",
            backdropFilter: "blur(4px)",
          }}
        >
          <I.Back />
        </button>
        <Avatar name={uname} color={C.headerBg} size={64} online />
        <div>
          <div style={{ color: "#fff", fontWeight: 800, fontSize: 20 }}>{uname}</div>
          <div style={{ color: "rgba(255,255,255,0.8)", fontSize: 13 }}>{user?.email}</div>
          <div style={{ color: "rgba(255,255,255,0.7)", fontSize: 12, marginTop: 3 }}>
            {projectBadge} {project?.name} | {project?.role}
          </div>
        </div>
      </div>

      <div style={{ padding: "8px 0" }}>
        {[
          {
            ic: <I.Star />,
            label: "Starred Messages",
            onClick: () => {
              setView("list");
              setTab("All");
              const firstStarred = contacts.find((c) =>
                (c.labels || []).some((l) => String(l).toLowerCase() === "starred"),
              );
              if (firstStarred) setAId(firstStarred.id);
            },
          },
          { ic: <I.Tag />, label: "Labels", onClick: handleSetLabels },
          { ic: <I.Device />, label: "Linked Devices", onClick: openDevicesModal },
          { ic: <I.Cog />, label: "Settings", onClick: () => setShowSettingsModal(true) },
          { ic: <I.Bell />, label: "Notifications", onClick: () => setShowNotificationsModal(true) },
          { ic: <I.ArrowRight />, label: "Switch Project", onClick: openProjectSwitch },
        ].map((item, i) => (
          <div
            key={i}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 14,
              padding: "16px 20px",
              borderBottom: `1px solid ${C.divider}`,
              cursor: "pointer",
            }}
            onClick={item.onClick}
            onMouseEnter={(e) => (e.currentTarget.style.background = C.orangePale)}
            onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
          >
            <div
              style={{
                width: 40,
                height: 40,
                borderRadius: 12,
                background: C.orangePale,
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                fontSize: 20,
                color: C.orange,
              }}
            >
              {item.ic}
            </div>
            <span style={{ fontWeight: 500, fontSize: 15, color: C.textMain }}>{item.label}</span>
            <svg
              style={{ marginLeft: "auto" }}
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke={C.textMuted}
              strokeWidth="2.5"
              strokeLinecap="round"
            >
              <polyline points="9 18 15 12 9 6" />
            </svg>
          </div>
        ))}
        <div
          onClick={onLogout}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 14,
            padding: "16px 20px",
            cursor: "pointer",
          }}
          onMouseEnter={(e) => (e.currentTarget.style.background = "#FFF1F1")}
          onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
        >
          <div
            style={{
              width: 40,
              height: 40,
              borderRadius: 12,
              background: "#FEE2E2",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              color: C.danger,
            }}
          >
            <I.Logout />
          </div>
          <span style={{ fontWeight: 600, fontSize: 15, color: C.danger }}>Log Out</span>
        </div>
      </div>
      {sharedDialogsNode}
    </div>
  );
}
