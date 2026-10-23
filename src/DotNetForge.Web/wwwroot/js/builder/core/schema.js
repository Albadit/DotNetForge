import { styleSchema, componentSchema } from "./schema-data.js";
const STYLE = styleSchema.style;
const STYLE_KEYS = Object.keys(STYLE);
const LAYOUT = styleSchema.layout;
const LAYOUT_KEYS = Object.keys(LAYOUT);
const LAYOUT_DEFAULTS = styleSchema.layoutDefaults;
const PLACE = styleSchema.place;
const SIZE_MODES = styleSchema.sizeModes;
const FONTS = styleSchema.fonts;
const SHADOWS = styleSchema.shadows;
const LIMITS = styleSchema.limits;
const PROP_LIMITS = componentSchema.limits;
const DEFAULT_BREAKPOINTS = styleSchema.breakpoints.defaults;
const BREAKPOINT_LIMITS = styleSchema.breakpoints.limits;
const COMPONENTS = { ...componentSchema.types };
const BUILTIN_TYPES = new Set(Object.keys(componentSchema.types));
function componentDef(type) {
  return Object.prototype.hasOwnProperty.call(COMPONENTS, type) ? COMPONENTS[type] : void 0;
}
function defineComponentSchema(type, def) {
  if (BUILTIN_TYPES.has(type)) return;
  COMPONENTS[type] = def;
}
function isContainerType(type) {
  return componentDef(type)?.container === true;
}
function canContain(parentType, childType) {
  const parent = componentDef(parentType);
  const child = componentDef(childType);
  if (!parent?.container || !child) return false;
  return child.parents.includes(parentType);
}
const BREAKPOINT_LABELS = { base: "Desktop", tablet: "Tablet", mobile: "Mobile" };
export {
  BREAKPOINT_LABELS,
  BREAKPOINT_LIMITS,
  BUILTIN_TYPES,
  COMPONENTS,
  DEFAULT_BREAKPOINTS,
  FONTS,
  LAYOUT,
  LAYOUT_DEFAULTS,
  LAYOUT_KEYS,
  LIMITS,
  PLACE,
  PROP_LIMITS,
  SHADOWS,
  SIZE_MODES,
  STYLE,
  STYLE_KEYS,
  canContain,
  componentDef,
  defineComponentSchema,
  isContainerType
};
