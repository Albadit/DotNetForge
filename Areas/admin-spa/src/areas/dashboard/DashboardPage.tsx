import { Users, FileText, Image, KeyRound, Webhook, Puzzle, ScrollText, Shield } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { KPI } from "@heroui-pro/react";
import { useDashboard } from "../../shared/hooks";
import { PageHeader, Loading, ErrorState, Panel, formatDate } from "../../shared/ui";

type KpiStatus = "success" | "warning" | "danger";

export default function DashboardPage() {
  const { data, isLoading, error } = useDashboard();

  if (isLoading) {
    return (
      <>
        <PageHeader title="Dashboard" />
        <Loading />
      </>
    );
  }
  if (error || !data) {
    return (
      <>
        <PageHeader title="Dashboard" />
        <ErrorState error={error} />
      </>
    );
  }

  const kpis: { label: string; icon: LucideIcon; status: KpiStatus; value: number }[] = [
    { label: "Users", icon: Users, status: "success", value: data.users },
    { label: "Roles", icon: Shield, status: "warning", value: data.roles },
    { label: "Pages", icon: FileText, status: "success", value: data.pages },
    { label: "Media", icon: Image, status: "warning", value: data.media },
    { label: "API tokens", icon: KeyRound, status: "danger", value: data.apiTokens },
    { label: "Webhooks", icon: Webhook, status: "success", value: data.webhooks },
    { label: "Extensions", icon: Puzzle, status: "warning", value: data.extensions },
    { label: "Audit entries", icon: ScrollText, status: "danger", value: data.auditEntries },
  ];

  return (
    <>
      <PageHeader title="Dashboard" description={`Overview of ${data.appName}.`} />

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {kpis.map((kpi) => {
          const Icon = kpi.icon;
          return (
            <KPI key={kpi.label}>
              <KPI.Header>
                <KPI.Icon status={kpi.status}>
                  <Icon className="size-5" />
                </KPI.Icon>
                <KPI.Title>{kpi.label}</KPI.Title>
              </KPI.Header>
              <KPI.Content>
                <KPI.Value maximumFractionDigits={0} value={kpi.value} />
              </KPI.Content>
            </KPI>
          );
        })}
      </div>

      <div className="mt-6">
        <Panel title="System">
          <dl className="grid grid-cols-2 gap-4 text-sm md:grid-cols-3">
            <div>
              <dt className="text-slate-500">CMS version</dt>
              <dd className="font-medium text-slate-800">{data.cmsVersion}</dd>
            </div>
            <div>
              <dt className="text-slate-500">Installed</dt>
              <dd className="font-medium text-slate-800">{formatDate(data.installedAtUtc)}</dd>
            </div>
            <div>
              <dt className="text-slate-500">Operating modes</dt>
              <dd className="font-medium text-slate-800">Traditional · Headless · Hybrid</dd>
            </div>
          </dl>
        </Panel>
      </div>
    </>
  );
}
