import { LogOut } from "lucide-react";
import { useMe } from "../shared/hooks";
import { apiPost } from "../shared/api";

export function Topbar() {
  const { data: me } = useMe();

  async function logout() {
    try {
      await apiPost("/logout");
    } finally {
      window.location.href = "/account/login";
    }
  }

  return (
    <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
      <div className="text-sm text-slate-500">
        Active tenant: <strong className="text-slate-700">{me?.tenant ?? "-"}</strong>
      </div>
      <div className="flex items-center gap-4">
        <span className="text-sm text-slate-600">{me?.name || me?.email || "…"}</span>
        <button
          type="button"
          onClick={logout}
          className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <LogOut size={15} />
          Sign out
        </button>
      </div>
    </header>
  );
}
