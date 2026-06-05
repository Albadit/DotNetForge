import { Fragment } from "react";
import { useParams } from "react-router-dom";
import { useAdminExtensions } from "../../shared/hooks";
import { PageHeader, Loading, ErrorState, Panel } from "../../shared/ui";

/**
 * Renders an admin extension discovered under extensions/admin/ (no React registration needed). If the
 * extension's manifest declares a `settings.url`, its UI is embedded in an iframe; otherwise the host
 * shows the extension's metadata and settings. One dynamic route (/admin/ext/:id) serves them all.
 */
export default function ExtensionHost() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, error } = useAdminExtensions();

  if (isLoading) {
    return (
      <>
        <PageHeader title="Extension" />
        <Loading />
      </>
    );
  }
  if (error) {
    return (
      <>
        <PageHeader title="Extension" />
        <ErrorState error={error} />
      </>
    );
  }

  const ext = data?.find((e) => e.id === id);
  if (!ext) {
    return <PageHeader title="Extension not found" description={`No admin extension with id "${id}".`} />;
  }

  // The extension ships its own view.html -> render it (host serves it with settings injected).
  if (ext.hasView) {
    return (
      <iframe
        src={`/admin-api/admin-extensions/${encodeURIComponent(ext.id)}/view`}
        title={ext.name}
        className="border-default-200 h-[calc(100vh-3rem)] w-full rounded-xl border"
      />
    );
  }

  const url = typeof ext.settings?.url === "string" ? (ext.settings.url as string) : undefined;
  const settingEntries = Object.entries(ext.settings ?? {});

  return (
    <>
      <PageHeader title={ext.name} description={ext.description} />

      {url ? (
        <iframe
          src={url}
          title={ext.name}
          className="border-default-200 h-[72vh] w-full rounded-xl border bg-white"
        />
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          <Panel title="Extension">
            <dl className="grid grid-cols-[120px_1fr] gap-y-2 text-sm">
              <dt className="text-muted">Id</dt>
              <dd>
                <code className="text-xs">{ext.id}</code>
              </dd>
              <dt className="text-muted">Version</dt>
              <dd>{ext.version}</dd>
              <dt className="text-muted">Author</dt>
              <dd>{ext.author}</dd>
              <dt className="text-muted">Entry point</dt>
              <dd>
                <code className="text-xs">{ext.entryPoint}</code>
              </dd>
              <dt className="text-muted">Routes</dt>
              <dd>{ext.routes.length ? ext.routes.join(", ") : "-"}</dd>
            </dl>
          </Panel>

          <Panel title="Settings">
            {settingEntries.length === 0 ? (
              <p className="text-muted text-sm">No settings declared.</p>
            ) : (
              <dl className="grid grid-cols-[140px_1fr] gap-y-2 text-sm">
                {settingEntries.map(([k, v]) => (
                  <Fragment key={k}>
                    <dt className="text-muted">
                      <code className="text-xs">{k}</code>
                    </dt>
                    <dd>{String(v)}</dd>
                  </Fragment>
                ))}
              </dl>
            )}
            <p className="text-muted mt-4 text-xs">
              Tip: add a <code className="text-xs">"url"</code> to this extension's manifest{" "}
              <code className="text-xs">settings</code> to embed its UI here.
            </p>
          </Panel>
        </div>
      )}
    </>
  );
}
