import { C } from './core';

export const CONTACTS = [
  { id:1, name:"Alice Johnson",    avatar:"AJ", color:"#7C3AED",
    online:true, unread:2, time:"10:42 AM", lastMsg:"Sure, I'll send the report by EOD.", typing:false,
    messages:[
      {id:1,text:"Hey! Did you review the Q3 report?",sent:false,time:"10:30 AM",status:"read"},
      {id:2,text:"Yes looks great! Just a few numbers to double-check.",sent:true,time:"10:35 AM",status:"read"},
      {id:3,text:"Which ones? I can fix right now.",sent:false,time:"10:38 AM",status:"read"},
      {id:4,text:"Pages 4 and 7 - the revenue projections look off.",sent:true,time:"10:40 AM",status:"read"},
      {id:5,text:"Sure, I'll send the report by EOD.",sent:false,time:"10:42 AM",status:"read"},
    ]},
  { id:2, name:"Bob Martinez",     avatar:"BM", color:"#DC2626",
    online:false, unread:0, time:"9:15 AM", lastMsg:"Meeting rescheduled to 3 PM", typing:false,
    messages:[
      {id:1,text:"Are we still on for the 2 PM sync?",sent:false,time:"8:50 AM",status:"read"},
      {id:2,text:"Let me check with the team.",sent:true,time:"8:55 AM",status:"read"},
      {id:3,text:"Meeting rescheduled to 3 PM",sent:false,time:"9:15 AM",status:"read"},
    ]},
  { id:3, name:"Customer Support", avatar:"CS", color:"#0891B2",
    online:true, unread:1, time:"Yesterday", lastMsg:"Ticket #4821 has been resolved.", typing:true,
    messages:[
      {id:1,text:"Hello, I need help with my subscription.",sent:true,time:"Yesterday",status:"read"},
      {id:2,text:"Hi! Happy to help. Can you share your account email?",sent:false,time:"Yesterday",status:"read"},
      {id:3,text:"It's user@example.com",sent:true,time:"Yesterday",status:"read"},
      {id:4,text:"Ticket #4821 has been resolved.",sent:false,time:"Yesterday",status:"read"},
    ]},
  { id:4, name:"Dev Team",      avatar:"DT", color:"#059669",
    online:true, unread:0, time:"Yesterday", lastMsg:"Deployment to prod done.", typing:false,
    messages:[
      {id:1,text:"Starting prod deployment...",sent:false,time:"Yesterday",status:"read"},
      {id:2,text:"Pipeline passed all checks.",sent:false,time:"Yesterday",status:"read"},
      {id:3,text:"Deployment to prod done.",sent:false,time:"Yesterday",status:"read"},
    ]},
  { id:5, name:"Sarah Patel",      avatar:"SP", color:"#9333EA",
    online:false, unread:0, time:"Mon", lastMsg:"Can you review my PR?", typing:false,
    messages:[
      {id:1,text:"Just pushed my feature branch.",sent:false,time:"Mon",status:"read"},
      {id:2,text:"Looks good from the summary!",sent:true,time:"Mon",status:"read"},
      {id:3,text:"Can you review my PR when free?",sent:false,time:"Mon",status:"read"},
    ]},
  { id:6, name:"Marketing Hub",    avatar:"MH", color:"#D97706",
    online:false, unread:3, time:"Sun", lastMsg:"New campaign brief ready.", typing:false,
    messages:[
      {id:1,text:"Q4 campaign planning started!",sent:false,time:"Sun",status:"read"},
      {id:2,text:"New campaign brief ready.",sent:false,time:"Sun",status:"read"},
    ]},
];

export const PROJECTS = [
  {slug:"moneyart",  name:"MoneyArt",  icon:"MA", role:"Agent"},
  {slug:"techcorp",  name:"TechCorp",  icon:"TC", role:"Admin"},
  {slug:"retailhub", name:"RetailHub", icon:"RH", role:"Agent"},
];

export const REPLIES = [
  "Got it!", "Sure thing!", "I'll check and get back to you.",
  "Sounds good!", "On it.", "Thanks!", "Will do.", "Let me look into this.", "Perfect!",
];

/* ════════════════════════════
   TEXTZY LOGO
════════════════════════════ */
export const Logo = ({ size=32 }) => (
  <svg width={size} height={size} viewBox="0 0 40 40" fill="none">
    <rect width="40" height="40" rx="10" fill="#F97316"/>
    <path d="M8 12C8 10.343 9.343 9 11 9H29C30.657 9 32 10.343 32 12V22C32 23.657 30.657 25 29 25H22L16 31V25H11C9.343 25 8 23.657 8 22V12Z" fill="white"/>
  </svg>
);

/* ════════════════════════════
   ICONS
════════════════════════════ */
export const I = {
  Send:    ()=><svg width="19" height="19" viewBox="0 0 24 24" fill="currentColor"><path d="M2 21l21-9L2 3v7l15 2-15 2z"/></svg>,
  Key:     ()=><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="7.5" cy="15.5" r="3.5"/><path d="M11 13l9-9"/><path d="M16 4l4 4"/><path d="M14 6l4 4"/></svg>,
  Mic:     ()=><svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 1a3 3 0 00-3 3v8a3 3 0 006 0V4a3 3 0 00-3-3z"/><path d="M19 10v2a7 7 0 01-14 0v-2"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/></svg>,
  Attach:  ()=><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21.44 11.05l-9.19 9.19a6 6 0 01-8.49-8.49l9.19-9.19a4 4 0 015.66 5.66l-9.2 9.19a2 2 0 01-2.83-2.83l8.49-8.48"/></svg>,
  Emoji:   ()=><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10"/><path d="M8 14s1.5 2 4 2 4-2 4-2"/><line x1="9" y1="9" x2="9.01" y2="9"/><line x1="15" y1="9" x2="15.01" y2="9"/></svg>,
  Back:    ()=><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>,
  More:    ()=><svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="5" r="1.8"/><circle cx="12" cy="12" r="1.8"/><circle cx="12" cy="19" r="1.8"/></svg>,
  Phone:   ()=><svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07A19.5 19.5 0 013.07 10.8 19.79 19.79 0 01.22 2.18 2 2 0 012.18 0h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L6.91 7.91a16 16 0 006.07 6.07l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7A2 2 0 0122 16.92z"/></svg>,
  Video:   ()=><svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polygon points="23 7 16 12 23 17 23 7"/><rect x="1" y="5" width="15" height="14" rx="2" ry="2"/></svg>,
  Search:  ()=><svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>,
  Eye:     ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7-11-7-11-7z"/><circle cx="12" cy="12" r="3"/></svg>,
  EyeOff:  ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17.94 17.94A10.94 10.94 0 0112 19c-7 0-11-7-11-7a21.77 21.77 0 015.06-5.94"/><path d="M9.9 4.24A10.94 10.94 0 0112 5c7 0 11 7 11 7a21.8 21.8 0 01-3.22 4.38"/><line x1="1" y1="1" x2="23" y2="23"/></svg>,
  ArrowRight: ()=><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>,
  Shield:  ()=><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 2l8 4v6c0 5-3.4 9.7-8 10-4.6-.3-8-5-8-10V6z"/></svg>,
  Close:   ()=><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>,
  Logout:  ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>,
  Camera:  ()=><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M23 19a2 2 0 01-2 2H3a2 2 0 01-2-2V8a2 2 0 012-2h4l2-3h6l2 3h4a2 2 0 012 2z"/><circle cx="12" cy="13" r="4"/></svg>,
  Check:   ()=><svg width="13" height="10" viewBox="0 0 13 10" fill="none"><path d="M1 5L4.5 8.5L12 1" stroke={C.textMuted} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/></svg>,
  DblChk:  ()=><svg width="18" height="11" viewBox="0 0 18 11" fill="none"><path d="M1 5.5L4.5 9L11 1.5" stroke={C.orange} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/><path d="M7 5.5L10.5 9L17 1.5" stroke={C.orange} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/></svg>,
  Star:    ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>,
  Device:  ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="5" y="2" width="14" height="20" rx="2" ry="2"/><line x1="12" y1="18" x2="12.01" y2="18"/></svg>,
  Bell:    ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M18 8a6 6 0 10-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9"/><path d="M13.73 21a2 2 0 01-3.46 0"/></svg>,
  Cog:     ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09a1.65 1.65 0 00-1-1.51 1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83 0 2 2 0 010-2.83l.06-.06a1.65 1.65 0 00.33-1.82 1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09a1.65 1.65 0 001.51-1 1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 010-2.83 2 2 0 012.83 0l.06.06a1.65 1.65 0 001.82.33h.01a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51h.01a1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 0 2 2 0 010 2.83l-.06.06a1.65 1.65 0 00-.33 1.82v.01a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"/></svg>,
  Tag:     ()=><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M20.59 13.41L11 22l-9-9V2h11z"/><line x1="7" y1="7" x2="7.01" y2="7"/></svg>,
  Plus:    ()=><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>,
};

export const QA_LIBRARY = [
  "QA: Thank you for contacting Textzy. How can I assist you today?",
  "QA: We are open 7 AM to 11 PM, 24x7 support available.",
  "QA: Please share your order ID so I can check details quickly.",
  "QA: I am connecting you with our support specialist now.",
];

export const EMOJI_SET = [
  0x1F600, 0x1F601, 0x1F602, 0x1F923, 0x1F60A, 0x1F60D, 0x1F618, 0x1F60E,
  0x1F91D, 0x1F64F, 0x1F44D, 0x1F44B, 0x1F4AC, 0x1F525, 0x2705, 0x1F389,
].map((cp) => String.fromCodePoint(cp));
