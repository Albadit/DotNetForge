import { Users, FileText, Image, KeyRound, Webhook, Puzzle, ScrollText, Shield } from "lucide-react";
import { useDashboard } from "../../shared/hooks";
import { PageHeader, Loading, ErrorState, StatCard, Panel, formatDate } from "../../shared/ui";

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

  return (
    <>
      <PageHeader title="Dashboard" description={`Overview of ${data.appName}.`} />

      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <StatCard icon={Users} label="Users" value={data.users} />
        <StatCard icon={Shield} label="Roles" value={data.roles} />
        <StatCard icon={FileText} label="Pages" value={data.pages} />
        <StatCard icon={Image} label="Media" value={data.media} />
        <StatCard icon={KeyRound} label="API tokens" value={data.apiTokens} />
        <StatCard icon={Webhook} label="Webhooks" value={data.webhooks} />
        <StatCard icon={Puzzle} label="Extensions" value={data.extensions} />
        <StatCard icon={ScrollText} label="Audit entries" value={data.auditEntries} />
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
