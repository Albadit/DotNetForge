import { useQuery } from "@tanstack/react-query";
import { apiGet } from "./api";

export interface Me {
  name: string;
  email: string;
  roles: string[];
  tenant: string;
}

export interface DashboardStats {
  appName: string;
  users: number;
  roles: number;
  pages: number;
  media: number;
  apiTokens: number;
  webhooks: number;
  extensions: number;
  auditEntries: number;
  cmsVersion: string;
  installedAtUtc: string | null;
}

export interface UserRow {
  email: string;
  name: string;
  status: string;
  lastLogin: string | null;
  roles: string[];
}

export interface PageRow {
  id: string;
  slug: string;
  title: string;
  published: boolean;
  displayInMenu: boolean;
  type: string;
  updatedDate: string;
}

export interface MediaRow {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  isPublic: boolean;
  uploadedDate: string;
}

export interface SettingRow {
  key: string;
  value: string | null;
}

export interface SettingsResponse {
  appName: string;
  settings: SettingRow[];
}

export interface ExtensionRow {
  id: string;
  name: string;
  version: string;
  type: string;
  status: string;
  author: string;
  validManifest: boolean;
  source: string;
}

export const useMe = () => useQuery({ queryKey: ["me"], queryFn: () => apiGet<Me>("/me") });
export const useDashboard = () =>
  useQuery({ queryKey: ["dashboard"], queryFn: () => apiGet<DashboardStats>("/dashboard") });
export const useUsers = () => useQuery({ queryKey: ["users"], queryFn: () => apiGet<UserRow[]>("/users") });
export const usePages = () => useQuery({ queryKey: ["pages"], queryFn: () => apiGet<PageRow[]>("/content/pages") });
export const useMedia = () => useQuery({ queryKey: ["media"], queryFn: () => apiGet<MediaRow[]>("/media") });
export const useSettings = () =>
  useQuery({ queryKey: ["settings"], queryFn: () => apiGet<SettingsResponse>("/settings") });
export const useExtensions = () =>
  useQuery({ queryKey: ["extensions"], queryFn: () => apiGet<ExtensionRow[]>("/extensions") });
