import type { CapacitorConfig } from "@capacitor/cli";

const config: CapacitorConfig = {
  appId: "com.textzy.ios.shell",
  appName: "Textzy iOS",
  webDir: "www",
  server: {
    url: "https://textzy-frontend-production.up.railway.app/?mobileShell=1&platform=ios",
    cleartext: false
  },
  ios: {
    contentInset: "automatic",
    allowsLinkPreview: false,
    scheme: "Textzy"
  }
};

export default config;
