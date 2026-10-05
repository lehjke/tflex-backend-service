const XIZI_TEMPLATE_IDS = new Set(["un_victor_mrl", "un_victor_mrl_t", "un_victor_r"]);

export function isXiziPreviewTemplate(template) {
  return XIZI_TEMPLATE_IDS.has(template?.id);
}

const finite = value => {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const parsed = Number(String(value).replace(",", "."));
  return Number.isFinite(parsed) ? parsed : null;
};

// W_CWT from each native Hoistway.grb Dimensions table. Width is invariant across
// the table's NBENT/DOOR/OP/SM variants for each CARTYPE+CWT pair.
// These widths do not replace the main document's x_cwt/y_cwt/DBG overrides.
function getNativeCounterweightThickness(id, context) {
  const type = context?.$CARTYPE;
  const safety = context?.$CWT;
  const rTypes = ["05W", "05WS", "08D", "08DR", "08X", "L08", "13D", "13W", "13X"];
  if (id === "un_victor_r") {
    if (!rTypes.includes(type) || !["WSAFE", "WOSAF"].includes(safety)) return null;
    return safety === "WSAFE" ? 156 : ({"05W":148, "05WS":110, "08X":150, "13X":150}[type] ?? 152);
  }
  const mrlTypes = ["05W", "06D", "08D", "08X", "13D", "13W", "13X"];
  if (!mrlTypes.includes(type) || !["WSAFE", "WOSAF"].includes(safety)
    || (type === "06D" && safety === "WSAFE")) return null;
  return safety === "WSAFE" ? 152 : ({"05W":127, "06D":148}[type] ?? 152);
}

export function getXiziPreviewGeometry(template, context) {
  if (!isXiziPreviewTemplate(template)) return null;
  const width = finite(context?.HW);
  const depth = finite(context?.HD);
  const cabinWidth = finite(context?.CW);
  const cabinDepth = finite(context?.CD);
  const cabinAxisX = finite(context?.HW1);
  const doorAxisX = finite(context?.HL6);
  const doorOffset = finite(context?.DOP);
  const doorWidth = finite(context?.OP);
  const entrances = finite(context?.NBENT);
  const place = String(context?.$CWTLOC ?? context?.CWTLOC ?? "");
  const hand = String(context?.$HAND ?? "").toUpperCase();
  if ([width, depth, cabinWidth, cabinDepth, cabinAxisX, doorAxisX, doorOffset, doorWidth, entrances].some(value => value === null)
    || [width, depth, cabinWidth, cabinDepth, doorWidth].some(value => value <= 0)
    || cabinAxisX === -1 || doorAxisX === -1
    || ![1, 2].includes(entrances) || !["12", "13", "24"].includes(place)
    || !["RIGHT", "LEFT", "CENTR"].includes(hand)) return null;

  const cwtSize = finite(context?.cwt_dbg ?? context?.CWTDBG);
  if (cwtSize === null || cwtSize <= 0) return null;
  const rear = place === "12";
  const centerOpening = hand === "CENTR" || context?.$DOOR === "CLD";
  const ldt = finite(context?.ldt);
  const cdt = finite(context?.cdt);
  const tolerance = finite(context?.tol);
  const xCwt = finite(context?.x_cwt);
  const yCwt = finite(context?.y_cwt);
  // These source dimensions refer to the rail axis, rather than a shaft-wall inset.
  const df = finite(context?.df);
  if (template.id === "un_victor_r" && df === null) return null;
  const railFromFront = template.id === "un_victor_r"
    ? ldt + 30 + cdt + tolerance + df
    : finite(context?.l_hd1);
  if ([ldt, cdt, tolerance, xCwt, yCwt, railFromFront].some(value => value === null || !Number.isFinite(value))
    || ldt <= 0 || cdt <= 0 || tolerance < 0 || xCwt < 0 || yCwt < 0 || railFromFront < 0
    || (template.id === "un_victor_r" && df < 0)) return null;
  const nativeThickness = getNativeCounterweightThickness(template.id, context);
  const cwtThickness = nativeThickness ?? 160;
  const sideSign = place === "24" ? 1 : -1;
  const handSign = finite(context?.s) ?? (hand === "LEFT" ? -1 : 1);
  const cwtAxisX = cabinAxisX + (rear ? -handSign : sideSign) * yCwt;
  const cwtAxisY = depth - railFromFront - xCwt;
  const cwtRect = rear
    ? {x: cwtAxisX - cwtSize / 2, y: cwtAxisY - cwtThickness / 2, width: cwtSize, height: cwtThickness}
    : {x: cwtAxisX - cwtThickness / 2, y: cwtAxisY - cwtSize / 2, width: cwtThickness, height: cwtSize};
  const doorStack = ldt + 30 + cdt + tolerance;
  const cabin = {x: cabinAxisX - cabinWidth / 2, y: depth - doorStack - cabinDepth, width: cabinWidth, height: cabinDepth};
  const doors = Array.from({length: entrances}, (_, index) => ({
    x: doorAxisX - doorWidth / 2, y: index === 0 ? depth - tolerance - ldt : tolerance,
    width: doorWidth, height: ldt
  }));
  const carDoors = Array.from({length: entrances}, (_, index) => ({
    x: doorAxisX - doorWidth / 2,
    y: index === 0 ? cabin.y + cabin.height : cabin.y - cdt,
    width: doorWidth, height: cdt
  }));
  return {
    width, depth, cabinWidth, cabinDepth, cabinAxisX, doorAxisX, doorOffset, doorWidth,
    entrances, place, hand, centerOpening, cabin, doors, carDoors, cwtRect,
    cwtAxisX, cwtAxisY, cwtThickness, nativeThickness, railFromFront, ldt, cdt, tolerance,
    cwtSize, doorStack,
    // Cabin shell and detailed profiles remain schematic; DBG is the counterweight guide span.
  };
}
