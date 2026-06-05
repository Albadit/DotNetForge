import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@heroui/react";
import { useSettings, type SettingRow } from "../../shared/hooks";
import { apiPost } from "../../shared/api";
import { PageHeader, Loading, ErrorState, Panel, DataTable, type Column } from "../../shared/ui";

const columns: Column<SettingRow>[] = [
  { key: "key", header: "Key", render: (s) => <code className="text-xs text-slate-600">{s.key}</code> },
  { key: "value", header: "Value", render: (s) => s.value ?? "-" },
];

export default function SettingsPage() {
  const { data, isLoading, error } = useSettings();
  const queryClient = useQueryClient();
  const [key, setKey] = useState("");
  const [value, setValue] = useState("");

  const save = useMutation({
    mutationFn: () => apiPost("/settings", { key, value }),
    onSuccess: () => {
      setKey("");
      setValue("");
      queryClient.invalidateQueries({ queryKey: ["settings"] });
    },
  });

  if (isLoading) {
    return (
      <>
        <PageHeader title="Settings" />
        <Loading />
      </>
    );
  }
  if (error || !data) {
    return (
      <>
        <PageHeader title="Settings" />
        <ErrorState error={error} />
      </>
    );
  }

  return (
    <>
      <PageHeader title="Settings" description={`Global settings for ${data.appName}.`} />

      <div className="grid gap-6 lg:grid-cols-2">
        <Panel title="Stored settings">
          <DataTable columns={columns} rows={data.settings} empty="No settings stored yet." />
        </Panel>

        <Panel title="Add or update a setting">
          <p className="mb-3 text-sm text-slate-500">
            This write goes through the cookie-authenticated admin API with antiforgery (X-CSRF-TOKEN) protection.
          </p>
          <div className="space-y-3">
            <div>
              <label className="text-sm font-medium text-slate-700">Key</label>
              <input
                value={key}
                onChange={(e) => setKey(e.target.value)}
                placeholder="site.tagline"
                className="mt-1 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="text-sm font-medium text-slate-700">Value</label>
              <input
                value={value}
                onChange={(e) => setValue(e.target.value)}
                placeholder="A modular hybrid CMS"
                className="mt-1 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
              />
            </div>
            <Button isDisabled={!key || save.isPending} onPress={() => save.mutate()}>
              {save.isPending ? "Saving…" : "Save setting"}
            </Button>
            {save.isError && <ErrorState error={save.error} />}
            {save.isSuccess && <p className="text-sm text-emerald-600">Saved.</p>}
          </div>
        </Panel>
      </div>
    </>
  );
}
