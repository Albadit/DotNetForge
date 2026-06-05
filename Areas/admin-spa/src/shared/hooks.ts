import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost, apiPut, apiDelete } from "./api";

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
  metaTitle: string | null;
  metaDescription: string | null;
  seoKeywords: string | null;
  canonicalUrl: string | null;
  published: boolean;
  disabled: boolean;
  displayInMenu: boolean;
  parentPageId: string | null;
  sortOrder: number;
  type: string;
  targetUrl: string | null;
  fileReference: string | null;
  scheduledPublishDate: string | null;
  scheduledUnpublishDate: string | null;
  createdDate: string;
  updatedDate: string;
}

/** The editable shape sent to create/update a page (matches the admin API's PageInput). */
export interface PageInput {
  title: string;
  slug: string;
  metaTitle: string | null;
  metaDescription: string | null;
  seoKeywords: string | null;
  canonicalUrl: string | null;
  published: boolean;
  disabled: boolean;
  displayInMenu: boolean;
  parentPageId: string | null;
  sortOrder: number;
  pageType: string;
  targetUrl: string | null;
  fileReference: string | null;
  scheduledPublishDate: string | null;
  scheduledUnpublishDate: string | null;
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

export function useCreatePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: PageInput) => apiPost<PageRow>("/content/pages", input),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["pages"] }),
  });
}

export function useUpdatePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: PageInput }) =>
      apiPut<PageRow>(`/content/pages/${id}`, input),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["pages"] }),
  });
}

export function useDeletePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete<void>(`/content/pages/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["pages"] }),
  });
}
export const useMedia = () => useQuery({ queryKey: ["media"], queryFn: () => apiGet<MediaRow[]>("/media") });
export const useSettings = () =>
  useQuery({ queryKey: ["settings"], queryFn: () => apiGet<SettingsResponse>("/settings") });
export const useExtensions = () =>
  useQuery({ queryKey: ["extensions"], queryFn: () => apiGet<ExtensionRow[]>("/extensions") });

/** An admin-type extension discovered under extensions/admin/, rendered as a dynamic sidebar tab. */
export interface AdminExtension {
  id: string;
  name: string;
  description: string;
  version: string;
  author: string;
  entryPoint: string;
  routes: string[];
  settings: Record<string, unknown>;
  /** True when the extension folder ships a view.html (served at /admin-api/admin-extensions/{id}/view). */
  hasView: boolean;
}

export const useAdminExtensions = () =>
  useQuery({
    queryKey: ["admin-extensions"],
    queryFn: () => apiGet<AdminExtension[]>("/admin-extensions"),
    staleTime: 0,
  });
