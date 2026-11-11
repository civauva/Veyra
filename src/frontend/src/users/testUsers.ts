import type { Account, UserDetail, UserSummary } from "../api/types";

/*
 * People for tests and the e2e run, matching the VEY-20 mockups: two Admins, three Editors, one locked, and Viewers,
 * one switched off and one who never signed in.
 */

const base = {
  disabled: false,
  createdAt: "2026-06-23T09:00:00Z",
  lastSignInAt: "2026-11-10T07:12:00Z",
  team: null,
  lockedUntil: null,
  secondFactorSince: "2026-10-03T10:00:00Z",
  activeSessions: 1,
  lastActiveAt: "2026-11-10T07:14:00Z",
} satisfies Partial<UserSummary>;

/** The signed-in Admin of the tests. */
export const dana: UserSummary = {
  ...base,
  id: "dana",
  email: "dana.kim@example.com",
  displayName: "Dana Kim",
  role: "Admin",
  team: "Platform",
};

/** The locked Editor the mockups select. */
export const tom: UserSummary = {
  ...base,
  id: "tom",
  email: "tom.okafor@example.com",
  displayName: "Tom Okafor",
  role: "Editor",
  team: "Payments",
  lockedUntil: "2026-11-10T07:52:00Z",
  activeSessions: 2,
};

/** Everyone on the Users screen. */
export const people: UserSummary[] = [
  dana,
  {
    ...base,
    id: "priya",
    email: "priya.raman@example.com",
    displayName: "Priya Raman",
    role: "Admin",
    team: "Platform",
  },
  {
    ...base,
    id: "lena",
    email: "lena.hoffmann@example.com",
    displayName: "Lena Hoffmann",
    role: "Editor",
    team: "Checkout",
  },
  {
    ...base,
    id: "marco",
    email: "marco.rossi@example.com",
    displayName: "Marco Rossi",
    role: "Editor",
    team: "Payments",
    secondFactorSince: null,
    lastSignInAt: "2026-11-07T09:00:00Z",
  },
  tom,
  { ...base, id: "ana", email: "ana.silva@example.com", displayName: "Ana Silva", role: "Viewer", team: "Support" },
  {
    ...base,
    id: "mei",
    email: "mei.chen@example.com",
    displayName: "Mei Chen",
    role: "Viewer",
    team: "Data",
    secondFactorSince: null,
    lastSignInAt: null,
    activeSessions: 0,
    lastActiveAt: null,
  },
  {
    ...base,
    id: "sam",
    email: "sam.taylor@example.com",
    displayName: "Sam Taylor",
    role: "Viewer",
    team: "Finance",
    disabled: true,
    secondFactorSince: null,
    lastSignInAt: "2026-10-02T09:00:00Z",
    activeSessions: 0,
  },
];

/** Tom in full: locked, two sessions, sign-ins with a new address, and his history. */
export const tomDetail: UserDetail = {
  user: tom,
  addedBy: "Dana Kim",
  secondFactor: { since: "2026-10-03T10:00:00Z", recoveryCodesLeft: 8 },
  secondFactorRequired: false,
  sessions: [
    {
      id: "s1",
      browser: "Firefox on Ubuntu",
      address: "198.51.100.7",
      startedAt: "2026-11-10T07:12:00Z",
      lastActiveAt: "2026-11-10T07:14:00Z",
      method: "Code",
      current: false,
    },
    {
      id: "s2",
      browser: "Safari on iPhone",
      address: "198.51.100.23",
      startedAt: "2026-11-08T09:02:00Z",
      lastActiveAt: "2026-11-09T21:05:00Z",
      method: "RecoveryCode",
      current: false,
    },
  ],
  signIns: [
    {
      at: "2026-11-10T07:37:00Z",
      outcome: "WrongPassword",
      address: "203.0.113.48",
      browser: "Chrome on Windows",
      newAddress: true,
    },
    {
      at: "2026-11-10T07:12:00Z",
      outcome: "SignedInWithCode",
      address: "198.51.100.7",
      browser: "Firefox on Ubuntu",
      newAddress: false,
    },
    {
      at: "2026-11-08T09:02:00Z",
      outcome: "SignedInWithRecoveryCode",
      address: "198.51.100.23",
      browser: "Safari on iPhone",
      newAddress: false,
    },
  ],
  signedInLast30Days: 19,
  failedLast30Days: 5,
  events: [
    { at: "2026-10-03T10:00:00Z", kind: "SecondFactorOn", by: "Tom Okafor", detail: null },
    { at: "2026-07-02T10:00:00Z", kind: "RoleChanged", by: "Dana Kim", detail: "Viewer Editor" },
    { at: "2026-06-23T09:00:00Z", kind: "Added", by: "Dana Kim", detail: "Viewer" },
  ],
};

/** Lena's own account, as the Account page shows it. */
export const lenaAccount: Account = {
  id: "lena",
  email: "lena.hoffmann@example.com",
  displayName: "Lena Hoffmann",
  role: "Editor",
  team: "Checkout",
  createdAt: "2026-09-14T09:00:00Z",
  lastSignInAt: "2026-11-10T07:41:00Z",
  hasPassword: true,
  secondFactor: { since: "2026-10-12T09:00:00Z", recoveryCodesLeft: 9 },
  secondFactorRequired: false,
  sessions: [
    {
      id: "here",
      browser: "Edge on Windows",
      address: "198.51.100.40",
      startedAt: "2026-11-10T07:41:00Z",
      lastActiveAt: "2026-11-10T07:59:00Z",
      method: "Code",
      current: true,
    },
    {
      id: "phone",
      browser: "Chrome on Android",
      address: "198.51.100.61",
      startedAt: "2026-11-09T08:15:00Z",
      lastActiveAt: "2026-11-09T19:22:00Z",
      method: "Code",
      current: false,
    },
  ],
  signIns: [
    {
      at: "2026-11-10T07:41:00Z",
      outcome: "SignedInWithCode",
      address: "198.51.100.40",
      browser: "Edge on Windows",
      newAddress: false,
    },
    {
      at: "2026-11-09T08:14:00Z",
      outcome: "WrongPassword",
      address: "198.51.100.61",
      browser: "Chrome on Android",
      newAddress: false,
    },
  ],
};
