using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder
{
    /// <summary>A document whose structure is broken (bad ids, a broken tree, unknown types...). The message is safe to show.</summary>
    public sealed class DocumentException : Exception
    {
        public DocumentException(string message)
            : base(message)
        {
        }

        public DocumentException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Normalizes an untrusted document with the rules of the editor's
    /// core/validate.ts: structure is checked strictly (a broken tree is an
    /// error), values are cleaned (unknown keys dropped, invalid values replaced
    /// by defaults). The editor uses its copy for pasted and imported content;
    /// this one is the authority for everything that is saved.
    /// </summary>
    public static class DocumentValidator
    {
        private static readonly string[] BreakpointKeys = { "base", "tablet", "mobile" };
        private static readonly string[] Devices = { "desktop", "tablet", "mobile" };
        private static readonly string[] Fonts = { "inherit", "sans", "serif", "mono" };
        private static readonly string[] ShadowPresets = { "none", "sm", "md", "lg", "xl" };

        /// <summary>
        /// JSON text to a normalized document. Parsed the way JSON.parse reads it:
        /// date-looking strings stay strings, no type names, bounded nesting. Text
        /// over the schema's size limit and malformed JSON are refused.
        /// </summary>
        public static PageDocument Parse(string json)
        {
            if (json == null) throw new DocumentException("The document is empty.");
            if (Encoding.UTF8.GetByteCount(json) > Schema.MaxDocumentBytes) throw new DocumentException("The document is too large.");
            JToken token;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    MaxDepth = 64,
                })
                {
                    token = JToken.ReadFrom(reader, new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    });
                    if (reader.Read()) throw new JsonReaderException("Unexpected content after the document.");
                }
            }
            catch (JsonException e)
            {
                throw new DocumentException("The document is not valid JSON.", e);
            }
            return Normalize(token);
        }

        /// <summary>Validates and normalizes a document. Throws <see cref="DocumentException"/> for structural problems.</summary>
        public static PageDocument Normalize(JToken input)
        {
            if (!(input is JObject doc)) throw new DocumentException("The document is not an object.");
            if (!Js.TryNumber(doc["schema"], out var schema) || schema != 1) throw new DocumentException("Unsupported document schema.");
            var root = Values.NodeId(doc["root"]);
            if (root == null) throw new DocumentException("The document has no valid root.");
            if (!(doc["nodes"] is JObject rawNodes)) throw new DocumentException("The document has no nodes.");
            var entries = Js.Entries(rawNodes);
            if (entries.Count > Schema.MaxNodes) throw new DocumentException("A page can have at most " + Schema.MaxNodes + " elements.");

            var nodes = new NodeMap();
            foreach (var entry in entries)
            {
                var key = entry.Name;
                var id = Values.NodeId(key);
                var raw = entry.Value as JObject;
                if (id == null || raw == null || !Js.TryString(raw["id"], out var rawId) || rawId != key)
                {
                    throw new DocumentException("Invalid node id \"" + key + "\".");
                }
                nodes.Add(NormalizeNode(raw, id));
            }
            if (!nodes.TryGetValue(root, out var rootNode) || rootNode.Type != "page") throw new DocumentException("The root must be a page.");
            rootNode.Parent = null;

            // Walk from the root: every child must exist, point back, appear once and be allowed there.
            var seen = new HashSet<string>(StringComparer.Ordinal) { root };
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(root, 0));
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                if (item.Value > Schema.MaxDepth) throw new DocumentException("The page is nested too deeply.");
                var node = nodes[item.Key];
                if (node.Children == null) continue;
                foreach (var child in node.Children)
                {
                    if (!nodes.TryGetValue(child, out var c)) throw new DocumentException("Node " + item.Key + " lists a missing child " + child + ".");
                    if (seen.Contains(child)) throw new DocumentException("Node " + child + " appears more than once.");
                    if (c.Parent != item.Key) throw new DocumentException("Node " + child + " does not point back to its parent.");
                    if (!Schema.CanContain(node.Type, c.Type)) throw new DocumentException("A " + c.Type + " cannot be placed in a " + node.Type + ".");
                    seen.Add(child);
                    stack.Push(new KeyValuePair<string, int>(child, item.Value + 1));
                }
            }

            // Nodes that are not reachable from the root are dropped.
            nodes.RetainOnly(seen);

            var bp = doc["settings"] is JObject settings && settings["breakpoints"] is JObject b ? b : new JObject();
            var tablet = Values.Int(bp["tablet"], Schema.MinBreakpoint, Schema.MaxBreakpoint) ?? Schema.DefaultTabletWidth;
            var mobile = Values.Int(bp["mobile"], Schema.MinBreakpoint, Schema.MaxBreakpoint) ?? Schema.DefaultMobileWidth;
            if (mobile >= tablet)
            {
                tablet = Schema.DefaultTabletWidth;
                mobile = Schema.DefaultMobileWidth;
            }
            return new PageDocument(root, nodes, (int)tablet, (int)mobile, RemovedModules(doc["settings"] as JObject, nodes));
        }

        /// <summary>Most module ids the removed list keeps.</summary>
        public const int MaxRemovedModules = 1000;

        /// <summary>
        /// settings.removedModules: distinct positive module ids, ascending, without the ones the layout
        /// places (putting a module back undoes its removal). Mirrors removedModulesOf in core/validate.ts.
        /// </summary>
        private static IReadOnlyList<int> RemovedModules(JObject settings, NodeMap nodes)
        {
            if (!(settings?["removedModules"] is JArray list)) return null;
            var placed = new HashSet<int>();
            foreach (var n in nodes.Values)
            {
                if (n.Type == "module" && n.Props["moduleId"] is JValue v && v.Type == JTokenType.Integer) placed.Add((int)(long)v);
            }
            var ids = new SortedSet<int>();
            foreach (var item in list)
            {
                var id = Values.Int(item, 1, int.MaxValue);
                if (id.HasValue && !placed.Contains((int)id.Value)) ids.Add((int)id.Value);
                if (ids.Count >= MaxRemovedModules) break;
            }
            return ids.ToList();
        }

        /// <summary>A prop value cleaned for its definition; the schema default when it is not acceptable.</summary>
        public static JToken NormalizeProp(PropDef def, JToken value)
        {
            switch (def.Type)
            {
                case "text":
                    return Str(Values.Text(value, MaxOf(def, Schema.MaxText))) ?? def.CreateDefault();
                case "inline":
                    {
                        var runs = RichText.NormalizeRuns(value, Schema.MaxRuns);
                        return runs.Sum(r => r.Text.Length) > MaxOf(def, Schema.MaxText) ? def.CreateDefault() : RichText.ToJson(runs);
                    }
                case "rich":
                    {
                        var blocks = RichText.NormalizeBlocks(value, Schema.MaxBlocks);
                        return RichText.TextLength(blocks) > MaxOf(def, 20000) ? def.CreateDefault() : RichText.ToJson(blocks);
                    }
                case "url":
                    return Str(Values.LinkUrl(value)) ?? def.CreateDefault();
                case "image":
                    return Str(Values.ImageUrl(value)) ?? def.CreateDefault();
                case "video":
                    {
                        // The trimmed address is kept (not the embed URL), so the editor shows what the author typed.
                        return Js.TryString(value, out var s) && (s.Length == 0 || Values.VideoEmbed(value) != null) ? new JValue(Js.Trim(s)) : def.CreateDefault();
                    }
                case "slug":
                    return Str(Values.Slug(value, MaxOf(def, 60))) ?? def.CreateDefault();
                case "enum":
                    return Js.TryString(value, out var option) && def.Options != null && def.Options.Contains(option) ? new JValue(option) : def.CreateDefault();
                case "bool":
                    return Js.IsBool(value) ? new JValue((bool)value) : def.CreateDefault();
                case "int":
                    return Int(Values.Int(value, def.Min ?? 0, def.Max ?? 2147483647)) ?? def.CreateDefault();
                case "module":
                    return Int(Values.Int(value, 0, 2147483647)) ?? def.CreateDefault();
                case "links":
                    {
                        if (!(value is JArray items)) return def.CreateDefault();
                        var output = new JArray();
                        foreach (var item in items.Take((int)Math.Min(def.MaxItems ?? 50, int.MaxValue)).OfType<JObject>())
                        {
                            var link = new JObject
                            {
                                ["label"] = Values.Text(item["label"], 200) ?? string.Empty,
                                ["href"] = Values.LinkUrl(item["href"]) ?? "#",
                                ["newTab"] = Js.IsTrue(item["newTab"]),
                            };
                            var icon = Values.IconName(item["icon"]);
                            if (!string.IsNullOrEmpty(icon)) link["icon"] = icon;
                            output.Add(link);
                        }
                        return output;
                    }
                case "icon":
                    return Str(Values.IconName(value)) ?? def.CreateDefault();
                case "ref":
                    return Js.TryString(value, out var reference) && (reference.Length == 0 || Values.Reference(reference) != null)
                        ? new JValue(reference.Length == 0 ? string.Empty : Values.Reference(reference))
                        : def.CreateDefault();
                case "field":
                    return Str(Values.FieldPath(value)) ?? def.CreateDefault();
                case "filters":
                    {
                        if (!(value is JArray items)) return def.CreateDefault();
                        var output = new JArray();
                        foreach (var item in items.Take((int)Math.Min(def.MaxItems ?? 10, int.MaxValue)).OfType<JObject>())
                        {
                            var field = Values.FieldPath(item["field"]);
                            if (string.IsNullOrEmpty(field)) continue;
                            output.Add(new JObject
                            {
                                ["field"] = field,
                                ["op"] = Js.TryString(item["op"], out var op) && FilterOperators.Contains(op) ? op : "eq",
                                ["value"] = Values.Text(item["value"], 200) ?? string.Empty,
                            });
                        }
                        return output;
                    }
                case "columns":
                    {
                        if (!(value is JArray items)) return def.CreateDefault();
                        var output = new JArray();
                        foreach (var item in items.Take((int)Math.Min(def.MaxItems ?? 12, int.MaxValue)).OfType<JObject>())
                        {
                            var field = Values.FieldPath(item["field"]);
                            if (string.IsNullOrEmpty(field)) continue;
                            output.Add(new JObject { ["field"] = field, ["label"] = Values.Text(item["label"], 100) ?? string.Empty });
                        }
                        return output;
                    }
                case "comments":
                    {
                        if (!(value is JArray items)) return def.CreateDefault();
                        var output = new JArray();
                        foreach (var item in items.Take((int)Math.Min(def.MaxItems ?? 100, int.MaxValue)).OfType<JObject>())
                        {
                            output.Add(new JObject
                            {
                                ["author"] = Values.Text(item["author"], 120) ?? string.Empty,
                                ["avatar"] = Values.ImageUrl(item["avatar"]) ?? string.Empty,
                                ["meta"] = Values.Text(item["meta"], 60) ?? string.Empty,
                                ["text"] = Values.Text(item["text"], 2000) ?? string.Empty,
                            });
                        }
                        return output;
                    }
                default:
                    return def.CreateDefault();
            }
        }

        /// <summary>The conditions a content list filter may use (mirrors FILTER_OPS in core/validate.js).</summary>
        public static readonly IReadOnlyList<string> FilterOperators = new[] { "eq", "neq", "contains", "gt", "gte", "lt", "lte", "empty", "notEmpty" };

        /// <summary>Longest binding template of one prop.</summary>
        public const int MaxBinding = 300;

        private static int MaxOf(PropDef def, int fallback)
        {
            return def.Max.HasValue ? (int)Math.Min(def.Max.Value, int.MaxValue) : fallback;
        }

        private static JToken Str(string value)
        {
            return value == null ? null : new JValue(value);
        }

        private static JToken Int(long? value)
        {
            return value.HasValue ? new JValue(value.Value) : null;
        }

        private static JToken Num(double? value)
        {
            return value.HasValue ? Js.Number(value.Value) : null;
        }

        /// <summary>A style value cleaned for its property, or null (dropped).</summary>
        private static JToken NormalizeStyleValue(string key, JToken value)
        {
            var def = Schema.Style(key);
            if (def == null) return null;
            var min = def.Min ?? -10000;
            var max = def.Max ?? 10000;
            switch (def.Type)
            {
                case "size":
                    {
                        if (!(value is JObject size) || !Js.TryString(size["mode"], out var mode) || !Schema.SizeModes.Contains(mode)) return null;
                        if (mode == "fixed" || mode == "percent")
                        {
                            var n = Values.Num(size["value"], 0, mode == "percent" ? 100 : 10000);
                            return n == null ? null : new JObject { ["mode"] = mode, ["value"] = Js.Number(n.Value) };
                        }
                        return new JObject { ["mode"] = mode };
                    }
                case "length":
                case "number":
                case "angle":
                    return Num(Values.Num(value, min, max));
                case "int":
                    return Int(Values.Int(value, min, max));
                case "enum":
                    return Js.TryString(value, out var option) && def.Options != null && def.Options.Contains(option) ? new JValue(option) : null;
                case "box":
                case "corners":
                    {
                        if (!(value is JArray list) || list.Count != 4) return null;
                        var output = new JArray();
                        foreach (var v in list)
                        {
                            var n = Values.Num(v, min, max);
                            if (n == null) return null;
                            output.Add(Js.Number(n.Value));
                        }
                        return output;
                    }
                case "color":
                    return Str(Values.Color(value));
                case "image":
                    {
                        var url = Values.ImageUrl(value);
                        return string.IsNullOrEmpty(url) ? null : new JValue(url);
                    }
                case "font":
                    return Js.TryString(value, out var font) && Fonts.Contains(font) ? new JValue(font) : null;
                case "shadow":
                    {
                        if (Js.TryString(value, out var preset)) return ShadowPresets.Contains(preset) ? new JValue(preset) : null;
                        if (!(value is JObject s)) return null;
                        var x = Values.Num(s["x"], -200, 200);
                        var y = Values.Num(s["y"], -200, 200);
                        var blur = Values.Num(s["blur"], 0, 400);
                        var spread = Values.Num(s["spread"], -200, 200);
                        var color = Values.Color(s["color"]);
                        if (x == null || y == null || blur == null || spread == null || string.IsNullOrEmpty(color)) return null;
                        var shadow = new JObject
                        {
                            ["x"] = Js.Number(x.Value),
                            ["y"] = Js.Number(y.Value),
                            ["blur"] = Js.Number(blur.Value),
                            ["spread"] = Js.Number(spread.Value),
                            ["color"] = color,
                        };
                        if (Js.IsTrue(s["inset"])) shadow["inset"] = true;
                        return shadow;
                    }
                default:
                    return null;
            }
        }

        private static JToken NormalizeLayoutValue(string key, JToken value)
        {
            var def = Schema.Layout(key);
            if (def == null) return null;
            switch (def.Type)
            {
                case "enum":
                    return Js.TryString(value, out var option) && def.Options != null && def.Options.Contains(option) ? new JValue(option) : null;
                case "bool":
                    return Js.IsBool(value) ? new JValue((bool)value) : null;
                case "int":
                    return Int(Values.Int(value, def.Min ?? 0, def.Max ?? 10000));
                case "length":
                    return Num(Values.Num(value, def.Min ?? 0, def.Max ?? 10000));
                case "tracks":
                    {
                        if (!(value is JArray list) || list.Count == 0) return null;
                        var tracks = new JArray();
                        foreach (var item in list.Take((int)(def.Max ?? 100))) tracks.Add(Values.Track(item) ?? def.Fallback ?? "auto");
                        return tracks;
                    }
                default:
                    return null;
            }
        }

        private static JToken NormalizeItemPlaceValue(string key, JToken value)
        {
            return key == "colSpan" || key == "rowSpan" || key == "w" || key == "h" ? NormalizePlaceValue(key, value) : null;
        }

        private static JToken NormalizePlaceValue(string key, JToken value)
        {
            var def = Schema.Place(key);
            return def == null ? null : Int(Values.Int(value, def.Min, def.Max));
        }

        /// <summary>base/tablet/mobile objects with every value cleaned; null when nothing is left.</summary>
        private static JObject NormalizeResponsive(JToken value, Func<string, JToken, JToken> clean)
        {
            if (!(value is JObject source)) return null;
            var output = new JObject();
            foreach (var bp in BreakpointKeys)
            {
                if (!(source[bp] is JObject src)) continue;
                var values = new JObject();
                foreach (var p in Js.Entries(src))
                {
                    var n = clean(p.Name, p.Value);
                    if (n != null) values[p.Name] = n;
                }
                if (values.Count > 0) output[bp] = values;
            }
            return output.Count > 0 ? output : null;
        }

        /// <summary>Distinct whole numbers in [-1, 2147483647] (role ids; -1 is "all users"), at most <paramref name="max"/>.</summary>
        private static List<int> IntList(JToken value, int max)
        {
            if (!(value is JArray list)) return null;
            // Same result as the TypeScript (first `max` distinct values), in linear time and
            // stopping at `max`: a hostile list of hundreds of thousands of ids must stay cheap.
            var output = new List<int>();
            var seen = new HashSet<int>();
            foreach (var v in list)
            {
                if (output.Count >= max) break;
                if (!Js.TryNumber(v, out var d) || !Js.IsInteger(d) || d < -1 || d > 2147483647) continue;
                var n = (int)d;
                if (seen.Add(n)) output.Add(n);
            }
            return output;
        }

        private static PageNode NormalizeNode(JObject raw, string id)
        {
            if (!Js.TryString(raw["type"], out var type)) throw new DocumentException("Node " + id + " has no type.");
            var def = Schema.Component(type);
            if (def == null) throw new DocumentException("Node " + id + " has an unknown type \"" + type + "\".");
            Js.TryString(raw["parent"], out var parent);
            var node = new PageNode(id, type, parent);

            var rawChildren = raw.Property("children", StringComparison.Ordinal)?.Value;
            if (def.Container)
            {
                if (rawChildren != null && !(rawChildren is JArray)) throw new DocumentException("Node " + id + ": children must be a list.");
                var children = new List<string>();
                if (rawChildren is JArray list)
                {
                    foreach (var c in list)
                    {
                        var cid = Values.NodeId(c);
                        if (cid == null) throw new DocumentException("Node " + id + " has an invalid child id.");
                        children.Add(cid);
                    }
                }
                node.Children = children;
            }
            else if (rawChildren is JArray list && list.Count > 0)
            {
                throw new DocumentException("Node " + id + " (" + type + ") cannot have children.");
            }

            var props = raw["props"] as JObject ?? new JObject();
            foreach (var pdef in def.Props)
            {
                var present = props.Property(pdef.Key, StringComparison.Ordinal);
                node.Props[pdef.Key] = present != null ? NormalizeProp(pdef, present.Value) : pdef.CreateDefault();
            }

            if (Js.IsString(raw["name"]))
            {
                var name = Values.Text(raw["name"], Schema.MaxName);
                if (!string.IsNullOrEmpty(name)) node.Name = name;
            }
            if (def.Container) node.Layout = NormalizeResponsive(raw["layout"], NormalizeLayoutValue);
            // A content list's item template is repeated, so it is never pinned to a cell or position.
            node.Place = NormalizeResponsive(raw["place"], type == "item" ? (Func<string, JToken, JToken>)NormalizeItemPlaceValue : NormalizePlaceValue);
            node.Style = NormalizeResponsive(raw["style"], NormalizeStyleValue);
            node.Hidden = Js.IsTrue(raw["hidden"]);
            node.Locked = Js.IsTrue(raw["locked"]);

            if (raw["visibility"] is JObject visibility)
            {
                List<string> devices = null;
                if (visibility["devices"] is JArray shown)
                {
                    devices = Devices.Where(d => shown.Any(s => Js.TryString(s, out var v) && v == d)).ToList();
                }
                Js.TryString(visibility["audience"], out var audience);
                if (audience != "anonymous" && audience != "authenticated") audience = "all";
                var roles = IntList(visibility["roles"], 50) ?? new List<int>();
                node.Visibility = new Visibility(devices, audience, roles);
            }
            if (Js.IsString(raw["className"]))
            {
                var c = Values.ClassNames(raw["className"], Schema.MaxClassName);
                if (!string.IsNullOrEmpty(c)) node.ClassName = c;
            }
            if (raw["vars"] is JObject rawVars)
            {
                var vars = new List<KeyValuePair<string, string>>();
                foreach (var p in Js.Entries(rawVars).Take(Schema.MaxVars))
                {
                    var name = Values.VarName(p.Name);
                    var value = Values.VarValue(p.Value);
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value)) vars.Add(new KeyValuePair<string, string>(name, value));
                }
                if (vars.Count > 0) node.Vars = vars;
            }
            if (raw["bind"] is JObject bind)
            {
                // Bindings in schema prop order: the order JSON.stringify writes them in the editor.
                var bindings = new List<KeyValuePair<string, string>>();
                foreach (var pdef in def.Props)
                {
                    if (!PropDef.BindableTypes.Contains(pdef.Type)) continue;
                    var template = Values.Text(bind.Property(pdef.Key, StringComparison.Ordinal)?.Value, MaxBinding);
                    if (!string.IsNullOrEmpty(template) && template.Contains('{')) bindings.Add(new KeyValuePair<string, string>(pdef.Key, template));
                }
                if (bindings.Count > 0) node.Bind = bindings;
            }
            if (raw["permissions"] is JObject permissions)
            {
                var roles = IntList(permissions["editRoles"], 50);
                if (roles != null && roles.Count > 0) node.EditRoles = roles;
            }
            return node;
        }
    }
}
