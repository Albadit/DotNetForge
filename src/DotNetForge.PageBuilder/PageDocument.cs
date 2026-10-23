using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder
{
    /// <summary>
    /// A normalized page document: the single source of truth for a custom page
    /// layout, the same shape the editor works with (core/types.ts). Nodes are
    /// stored flat (id -> node) with parent/children links.
    ///
    /// Instances only come out of <see cref="DocumentValidator"/>, so every value
    /// in them has been checked; the renderers still re-check what they print,
    /// exactly as the TypeScript renderers do.
    /// </summary>
    public sealed class PageDocument
    {
        private readonly NodeMap nodes;

        internal PageDocument(string root, NodeMap nodes, int tabletWidth, int mobileWidth, IReadOnlyList<int> removedModules = null)
        {
            Root = root;
            this.nodes = nodes;
            TabletWidth = tabletWidth;
            MobileWidth = mobileWidth;
            RemovedModules = removedModules ?? new int[0];
        }

        public string Root { get; }

        /// <summary>The nodes, in the order the editor's normalized document lists them.</summary>
        public IReadOnlyDictionary<string, PageNode> Nodes => nodes;

        /// <summary>Below this width (px) the tablet values apply.</summary>
        public int TabletWidth { get; }

        /// <summary>Below this width (px) the mobile values apply.</summary>
        public int MobileWidth { get; }

        /// <summary>
        /// DNN modules removed from this layout in the editor (ascending, none placed in the layout).
        /// They stay on the DNN page, but the layout does not show them, not even after its content.
        /// </summary>
        public IReadOnlyList<int> RemovedModules { get; }

        public PageNode Node(string id)
        {
            return id != null && nodes.TryGetValue(id, out var node) ? node : null;
        }

        /// <summary>The document as JSON, deep-equal to the editor's normalized document.</summary>
        public JObject ToJson()
        {
            var nodeJson = new JObject();
            foreach (var node in nodes.Values) nodeJson[node.Id] = node.ToJson();
            return new JObject
            {
                ["schema"] = 1,
                ["root"] = Root,
                ["nodes"] = nodeJson,
                ["settings"] = Settings(),
            };
        }

        private JObject Settings()
        {
            var settings = new JObject { ["breakpoints"] = new JObject { ["tablet"] = TabletWidth, ["mobile"] = MobileWidth } };
            if (RemovedModules.Count > 0) settings["removedModules"] = new JArray(RemovedModules);
            return settings;
        }

        public string ToJsonString()
        {
            return ToJson().ToString(Formatting.None);
        }

        /// <summary>Every node reachable from the root, depth first, in document order.</summary>
        public IEnumerable<string> Walk()
        {
            return Subtree(Root);
        }

        /// <summary>The node and all its descendants, depth first, in children order.</summary>
        public IEnumerable<string> Subtree(string id)
        {
            var output = new List<string>();
            var stack = new Stack<string>();
            stack.Push(id);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                var node = Node(current);
                if (node == null) continue;
                output.Add(current);
                var kids = node.Children;
                if (kids != null) for (var i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
            }
            return output;
        }
    }

    /// <summary>One normalized node. Treat the JSON objects as read-only: they are the document's own.</summary>
    public sealed class PageNode
    {
        private static readonly IReadOnlyList<KeyValuePair<string, string>> NoVars = new KeyValuePair<string, string>[0];
        private static readonly IReadOnlyList<int> NoRoles = new int[0];

        internal PageNode(string id, string type, string parent)
        {
            Id = id;
            Type = type;
            Parent = parent;
            Props = new JObject();
            Vars = NoVars;
            EditRoles = NoRoles;
            Visibility = Visibility.Everyone;
        }

        public string Id { get; }

        public string Type { get; }

        /// <summary>A display name the author gave the node, or null.</summary>
        public string Name { get; internal set; }

        /// <summary>The parent's id; null for the root.</summary>
        public string Parent { get; internal set; }

        /// <summary>Child ids in order; null for types that are not containers.</summary>
        public IReadOnlyList<string> Children { get; internal set; }

        /// <summary>Every prop of the type's schema, in schema order.</summary>
        public JObject Props { get; }

        /// <summary>Container layout per breakpoint (base/tablet/mobile), or null.</summary>
        public JObject Layout { get; internal set; }

        /// <summary>Placement in the parent per breakpoint, or null.</summary>
        public JObject Place { get; internal set; }

        /// <summary>Style per breakpoint, or null.</summary>
        public JObject Style { get; internal set; }

        public bool Hidden { get; internal set; }

        public bool Locked { get; internal set; }

        public string ClassName { get; internal set; }

        /// <summary>Custom properties in the author's order (the order they are written to CSS); empty when none.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Vars { get; internal set; }

        /// <summary>Who sees the node; never null (<see cref="Visibility.Everyone"/> when unrestricted).</summary>
        public Visibility Visibility { get; internal set; }

        /// <summary>Roles that may edit this node besides page editors; empty when none.</summary>
        public IReadOnlyList<int> EditRoles { get; internal set; }

        public JObject ToJson()
        {
            var o = new JObject
            {
                ["id"] = Id,
                ["type"] = Type,
                ["parent"] = Parent != null ? new JValue(Parent) : JValue.CreateNull(),
                ["props"] = Props.DeepClone(),
            };
            if (Children != null) o["children"] = new JArray(Children);
            if (Name != null) o["name"] = Name;
            if (Layout != null) o["layout"] = Layout.DeepClone();
            if (Place != null) o["place"] = Place.DeepClone();
            if (Style != null) o["style"] = Style.DeepClone();
            if (Hidden) o["hidden"] = true;
            if (Locked) o["locked"] = true;
            if (!Visibility.IsEveryone) o["visibility"] = Visibility.ToJson();
            if (ClassName != null) o["className"] = ClassName;
            if (Vars.Count > 0)
            {
                var vars = new JObject();
                foreach (var v in Vars) vars[v.Key] = v.Value;
                o["vars"] = vars;
            }
            if (EditRoles.Count > 0) o["permissions"] = new JObject { ["editRoles"] = new JArray(EditRoles) };
            return o;
        }
    }

    /// <summary>Who a node is shown to. The device part is applied with CSS; audience and roles by the host.</summary>
    public sealed class Visibility
    {
        public static readonly Visibility Everyone = new Visibility(null, "all", new int[0]);

        internal Visibility(IReadOnlyList<string> devices, string audience, IReadOnlyList<int> roles)
        {
            Devices = devices;
            Audience = audience;
            Roles = roles;
        }

        /// <summary>Devices (desktop, tablet, mobile) the node is shown on; null = all. An empty list hides it everywhere.</summary>
        public IReadOnlyList<string> Devices { get; }

        /// <summary>"all", "anonymous" or "authenticated".</summary>
        public string Audience { get; }

        /// <summary>Role ids; the viewer must be in at least one. Empty = no role condition.</summary>
        public IReadOnlyList<int> Roles { get; }

        public bool IsEveryone => Devices == null && Audience == "all" && Roles.Count == 0;

        public JObject ToJson()
        {
            var o = new JObject();
            if (Devices != null) o["devices"] = new JArray(Devices);
            if (Audience != "all") o["audience"] = Audience;
            if (Roles.Count > 0) o["roles"] = new JArray(Roles);
            return o;
        }
    }

    /// <summary>An id -> node map that keeps insertion order (JavaScript objects do; Dictionary does not promise it).</summary>
    internal sealed class NodeMap : IReadOnlyDictionary<string, PageNode>
    {
        private readonly Dictionary<string, PageNode> byId = new Dictionary<string, PageNode>(System.StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();

        public int Count => byId.Count;

        public IEnumerable<string> Keys => order;

        public IEnumerable<PageNode> Values => order.Select(id => byId[id]);

        public PageNode this[string key] => byId[key];

        public void Add(PageNode node)
        {
            byId.Add(node.Id, node);
            order.Add(node.Id);
        }

        /// <summary>Keeps only the given ids, in their current order.</summary>
        public void RetainOnly(ICollection<string> keep)
        {
            for (var i = order.Count - 1; i >= 0; i--)
            {
                if (keep.Contains(order[i])) continue;
                byId.Remove(order[i]);
                order.RemoveAt(i);
            }
        }

        public bool ContainsKey(string key)
        {
            return byId.ContainsKey(key);
        }

        public bool TryGetValue(string key, out PageNode value)
        {
            return byId.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, PageNode>> GetEnumerator()
        {
            return order.Select(id => new KeyValuePair<string, PageNode>(id, byId[id])).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
