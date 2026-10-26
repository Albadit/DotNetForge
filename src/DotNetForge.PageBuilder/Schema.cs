using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder
{
    /// <summary>The three breakpoints, desktop first: base applies everywhere, smaller ones override.</summary>
    public enum Breakpoint
    {
        Base,
        Tablet,
        Mobile,
    }

    /// <summary>One style or layout property of schema/style.json.</summary>
    public sealed class PropertyDef
    {
        internal PropertyDef(string key, JObject json)
        {
            Key = key;
            Type = (string)json["type"];
            Css = (string)json["css"];
            Min = Schema.OptionalNumber(json["min"]);
            Max = Schema.OptionalNumber(json["max"]);
            Options = Schema.StringList(json["options"]);
            Modes = Schema.StringList(json["modes"]);
            Fallback = (string)json["fallback"];
        }

        public string Key { get; }

        /// <summary>Selects the validator and the CSS formatter (size, length, enum, color...).</summary>
        public string Type { get; }

        /// <summary>The CSS property it writes, or null.</summary>
        public string Css { get; }

        public double? Min { get; }

        public double? Max { get; }

        /// <summary>The allowed values of an enum, or null.</summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>Layout properties only: the layout modes the property applies to, or null.</summary>
        public IReadOnlyList<string> Modes { get; }

        /// <summary>tracks: the size of a track whose value is missing or invalid.</summary>
        public string Fallback { get; }
    }

    /// <summary>A placement property (grid cell or free position): always an integer range.</summary>
    public sealed class PlaceDef
    {
        internal PlaceDef(string key, double min, double max)
        {
            Key = key;
            Min = min;
            Max = max;
        }

        public string Key { get; }

        public double Min { get; }

        public double Max { get; }
    }

    /// <summary>One prop of a component type (schema/components.json).</summary>
    public sealed class PropDef
    {
        private static readonly HashSet<string> Types = new HashSet<string>(StringComparer.Ordinal)
        {
            "text", "inline", "rich", "url", "image", "enum", "bool", "int", "links", "module", "video", "slug", "icon", "comments",
            "ref", "field", "filters", "columns",
        };

        /// <summary>Prop types a node inside a content list can take from an entry's fields (<see cref="PageNode.Bind"/>).</summary>
        public static readonly IReadOnlyCollection<string> BindableTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "text", "inline", "rich", "url", "image", "video",
        };

        private readonly JToken defaultValue;

        internal PropDef(string key, JObject json)
        {
            Key = key;
            Type = (string)json["type"];
            if (Type == null || !Types.Contains(Type)) throw new ArgumentException("Prop \"" + key + "\" has an unknown type.");
            if (!json.TryGetValue("default", StringComparison.Ordinal, out var def)) throw new ArgumentException("Prop \"" + key + "\" has no default.");
            defaultValue = def.DeepClone();
            Label = (string)json["label"];
            Content = Js.IsTrue(json["content"]);
            Options = Schema.StringList(json["options"]);
            Min = Schema.OptionalNumber(json["min"]);
            Max = Schema.OptionalNumber(json["max"]);
            MaxItems = Schema.OptionalNumber(json["maxItems"]);
        }

        public string Key { get; }

        public string Type { get; }

        public string Label { get; }

        /// <summary>A prop a content editor may change; everything else needs layout rights.</summary>
        public bool Content { get; }

        public IReadOnlyList<string> Options { get; }

        public double? Min { get; }

        public double? Max { get; }

        public double? MaxItems { get; }

        /// <summary>A fresh copy of the default value (callers may keep or change it).</summary>
        public JToken CreateDefault()
        {
            return defaultValue.DeepClone();
        }

        /// <summary><c>String(def.default)</c>.</summary>
        internal string DefaultString => Js.ToJsString(defaultValue);
    }

    /// <summary>A component type: its props, the parents it may be placed in and its defaults.</summary>
    public sealed class ComponentDef
    {
        private readonly Dictionary<string, PropDef> propsByKey;

        internal ComponentDef(string type, JObject json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            Type = type;
            Label = (string)json["label"] ?? type;
            Category = (string)json["category"];
            Container = Js.IsTrue(json["container"]);
            // Only an explicit false hides a type from the insert panel.
            Insertable = !(json["insertable"] is JValue insertable && insertable.Type == JTokenType.Boolean && !(bool)insertable);
            if (!(json["parents"] is JArray parents) || parents.Any(p => !Js.IsString(p)))
            {
                throw new ArgumentException("Component \"" + type + "\" needs a list of parent types.");
            }
            Parents = parents.Select(p => (string)p).ToList();
            var props = json["props"] as JObject ?? new JObject();
            var list = new List<PropDef>();
            foreach (var p in props.Properties())
            {
                if (!(p.Value is JObject propJson)) throw new ArgumentException("Prop \"" + p.Name + "\" of \"" + type + "\" is not an object.");
                list.Add(new PropDef(p.Name, propJson));
            }
            Props = list;
            propsByKey = list.ToDictionary(p => p.Key, StringComparer.Ordinal);
            Layout = json["layout"] as JObject;
            Style = json["style"] as JObject;
            Place = json["place"] as JObject;
        }

        public string Type { get; }

        public string Label { get; }

        public string Category { get; }

        public bool Container { get; }

        public bool Insertable { get; }

        public IReadOnlyList<string> Parents { get; }

        /// <summary>Props in schema order (the order the normalized document lists them in).</summary>
        public IReadOnlyList<PropDef> Props { get; }

        /// <summary>Default layout of a new node (editor use), or null.</summary>
        public JObject Layout { get; }

        public JObject Style { get; }

        public JObject Place { get; }

        public PropDef Prop(string key)
        {
            return key != null && propsByKey.TryGetValue(key, out var def) ? def : null;
        }
    }

    /// <summary>
    /// Typed access to the schema shared with the TypeScript editor
    /// (schema/style.json, schema/components.json, schema/icons.json and schema/base.css, embedded
    /// in this assembly). Loaded once; one source for both validators and both
    /// renderers, so a property added there is handled identically here.
    /// </summary>
    public static class Schema
    {
        private const string ResourcePrefix = "DotNetForge.PageBuilder.Schema.";

        private static readonly Dictionary<string, PropertyDef> StyleByKey;
        private static readonly Dictionary<string, PropertyDef> LayoutByKey;
        private static readonly Dictionary<string, PlaceDef> PlaceByKey;
        private static readonly Dictionary<string, JObject> LayoutDefaultsByMode;
        private static readonly Dictionary<string, ComponentDef> BuiltIns;
        private static readonly ConcurrentDictionary<string, ComponentDef> Extensions = new ConcurrentDictionary<string, ComponentDef>(StringComparer.Ordinal);

        static Schema()
        {
            var style = (JObject)LoadJson("style.json");
            var components = (JObject)LoadJson("components.json");
            BaseCssText = LoadText("base.css");
            PageIcons = ((JObject)LoadJson("icons.json")["icons"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value, StringComparer.Ordinal);

            StyleProperties = ((JObject)style["style"]).Properties().Select(p => new PropertyDef(p.Name, (JObject)p.Value)).ToList();
            StyleByKey = StyleProperties.ToDictionary(p => p.Key, StringComparer.Ordinal);
            LayoutProperties = ((JObject)style["layout"]).Properties().Select(p => new PropertyDef(p.Name, (JObject)p.Value)).ToList();
            LayoutByKey = LayoutProperties.ToDictionary(p => p.Key, StringComparer.Ordinal);
            LayoutDefaultsByMode = ((JObject)style["layoutDefaults"]).Properties().ToDictionary(p => p.Name, p => (JObject)p.Value, StringComparer.Ordinal);
            PlaceProperties = ((JObject)style["place"]).Properties()
                .Select(p => new PlaceDef(p.Name, (double)OptionalNumber(p.Value["min"]), (double)OptionalNumber(p.Value["max"])))
                .ToList();
            PlaceByKey = PlaceProperties.ToDictionary(p => p.Key, StringComparer.Ordinal);
            SizeModes = StringList(style["sizeModes"]);
            Fonts = ((JObject)style["fonts"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value, StringComparer.Ordinal);
            Shadows = ((JObject)style["shadows"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value, StringComparer.Ordinal);

            var limits = style["limits"];
            MaxNodes = (int)limits["nodes"];
            MaxDepth = (int)limits["depth"];
            MaxDocumentBytes = (int)limits["documentBytes"];
            MaxClassName = (int)limits["className"];
            MaxVars = (int)limits["vars"];
            MaxName = (int)limits["name"];

            var propLimits = components["limits"];
            MaxText = (int)propLimits["text"];
            MaxUrl = (int)propLimits["url"];
            MaxRuns = (int)propLimits["runs"];
            MaxBlocks = (int)propLimits["blocks"];
            MaxListItems = (int)propLimits["listItems"];

            var breakpoints = style["breakpoints"];
            DefaultTabletWidth = (int)breakpoints["defaults"]["tablet"];
            DefaultMobileWidth = (int)breakpoints["defaults"]["mobile"];
            MinBreakpoint = (int)breakpoints["limits"]["min"];
            MaxBreakpoint = (int)breakpoints["limits"]["max"];

            BuiltIns = ((JObject)components["types"]).Properties().ToDictionary(p => p.Name, p => new ComponentDef(p.Name, (JObject)p.Value), StringComparer.Ordinal);
            BuiltInTypes = BuiltIns.Keys.ToList();
        }

        /// <summary>Style properties in schema order (the order CSS declarations are written in).</summary>
        public static IReadOnlyList<PropertyDef> StyleProperties { get; }

        public static IReadOnlyList<PropertyDef> LayoutProperties { get; }

        public static IReadOnlyList<PlaceDef> PlaceProperties { get; }

        public static IReadOnlyList<string> SizeModes { get; }

        /// <summary>Font keys (sans, serif...) to their CSS font stacks.</summary>
        public static IReadOnlyDictionary<string, string> Fonts { get; }

        /// <summary>The icons a page may use (schema/icons.json): name to SVG inner markup.</summary>
        public static IReadOnlyDictionary<string, string> PageIcons { get; }

        /// <summary>Shadow presets (sm, md...) to their CSS box-shadow values.</summary>
        public static IReadOnlyDictionary<string, string> Shadows { get; }

        public static int MaxNodes { get; }

        public static int MaxDepth { get; }

        public static int MaxDocumentBytes { get; }

        public static int MaxClassName { get; }

        public static int MaxVars { get; }

        public static int MaxName { get; }

        /// <summary>Default length limit of text props.</summary>
        public static int MaxText { get; }

        public static int MaxUrl { get; }

        public static int MaxRuns { get; }

        public static int MaxBlocks { get; }

        public static int MaxListItems { get; }

        public static int DefaultTabletWidth { get; }

        public static int DefaultMobileWidth { get; }

        public static int MinBreakpoint { get; }

        public static int MaxBreakpoint { get; }

        public static IReadOnlyList<string> BuiltInTypes { get; }

        internal static string BaseCssText { get; }

        public static PropertyDef Style(string key)
        {
            return key != null && StyleByKey.TryGetValue(key, out var def) ? def : null;
        }

        public static PropertyDef Layout(string key)
        {
            return key != null && LayoutByKey.TryGetValue(key, out var def) ? def : null;
        }

        public static PlaceDef Place(string key)
        {
            return key != null && PlaceByKey.TryGetValue(key, out var def) ? def : null;
        }

        /// <summary>The defaults of a layout mode (stack/grid/free). Shared instance: do not modify.</summary>
        internal static JObject LayoutDefaults(string mode)
        {
            return LayoutDefaultsByMode.TryGetValue(mode, out var value) ? value : new JObject();
        }

        /// <summary>The schema of a built-in or registered extension type, or null.</summary>
        public static ComponentDef Component(string type)
        {
            if (type == null) return null;
            if (BuiltIns.TryGetValue(type, out var def)) return def;
            return Extensions.TryGetValue(type, out def) ? def : null;
        }

        public static bool IsBuiltIn(string type)
        {
            return type != null && BuiltIns.ContainsKey(type);
        }

        public static bool IsContainerType(string type)
        {
            return Component(type)?.Container == true;
        }

        /// <summary>Whether a node of <paramref name="childType"/> may be placed in one of <paramref name="parentType"/>.</summary>
        public static bool CanContain(string parentType, string childType)
        {
            var parent = Component(parentType);
            var child = Component(childType);
            if (parent == null || !parent.Container || child == null) return false;
            return child.Parents.Contains(parentType);
        }

        /// <summary>Adds the schema of an extension component. Built-in schemas cannot be replaced.</summary>
        internal static ComponentDef DefineComponent(string type, JObject json)
        {
            if (IsBuiltIn(type)) throw new InvalidOperationException("The built-in type \"" + type + "\" cannot be redefined.");
            var def = new ComponentDef(type, (JObject)json.DeepClone());
            Extensions[type] = def;
            return def;
        }

        internal static double? OptionalNumber(JToken value)
        {
            return Js.TryNumber(value, out var d) ? d : (double?)null;
        }

        internal static IReadOnlyList<string> StringList(JToken value)
        {
            return value is JArray array ? array.Where(Js.IsString).Select(v => (string)v).ToList() : null;
        }

        private static JToken LoadJson(string name)
        {
            using (var reader = new JsonTextReader(new StringReader(LoadText(name))) { DateParseHandling = DateParseHandling.None })
            {
                return JToken.ReadFrom(reader);
            }
        }

        private static string LoadText(string name)
        {
            var assembly = typeof(Schema).Assembly;
            using (var stream = assembly.GetManifestResourceStream(ResourcePrefix + name))
            {
                if (stream == null) throw new InvalidOperationException("The embedded schema file " + name + " is missing.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
