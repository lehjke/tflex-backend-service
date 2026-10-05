const finite = value => typeof value === "number" && Number.isFinite(value);
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
const escapeXml = value => String(value).replace(/[&<>"']/gu, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&apos;" })[char]);

function read(context, key, fallback = null) {
  const raw = context?.[key];
  const value = raw && typeof raw === "object" && "value" in raw ? raw.value : raw;
  if (value === undefined || value === null || value === "") return fallback;
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

/** Converts K-II-TYPE source dimensions (millimetres) into a compact profile model. */
export function getEscalatorPreviewGeometry(context = {}) {
  const TK = read(context, "TK"), TJ = read(context, "TJ"), HE = read(context, "HE");
  const alpha = read(context, "alpha"), TG = read(context, "TG"), LL = read(context, "LL", TG);
  const FC = read(context, "FC", 918), FJ = read(context, "FJ", 1170), FK = read(context, "FK", 1020);
  const HH = read(context, "HH", 1005), CH = read(context, "CH", 1000);
  const Lpit = read(context, "Lpit", read(context, "Lpit_v")), Dpit = read(context, "Dpit", read(context, "Dpit_v"));
  const PKD = read(context, "PKD", Dpit), Wpit = read(context, "Wpit"), N = read(context, "N", 1);
  const SUP1 = read(context, "SUP1"), SUP2 = read(context, "SUP2");
  const HSUP1 = read(context, "HSUP1"), HSUP2 = read(context, "HSUP2");
  const HM1 = read(context, "HM1", 0), HM2 = read(context, "HM2", 0);
  const balustradeMaterial = context?.["$Balustrade_material"] ?? context?.balustradeMaterial ?? "Стекло";
  const pitType = context?.["$pit_v"] ?? context?.pitType ?? "Приямок";
  if (![TK, TJ, HE, alpha, TG, LL, FC, FJ, FK, HH, CH].every(finite) ||
      TK <= 0 || TJ <= 0 || HE <= 0 || TG <= TK + TJ || LL <= 0 || ![30, 35].includes(alpha) ||
      FC <= 0 || CH <= 0 || (N !== null && N <= 0)) return null;
  const run = TG - TK - TJ;
  const lower = { x: TK, y: 0 };
  const upper = { x: TG - TJ, y: HE };
  const length = Math.hypot(run, HE);
  if (!finite(length) || length <= 0) return null;
  const ux = run / length, uy = HE / length;
  const nx = uy, ny = -ux;
  const maxDetail = 80;
  const stepCount = clamp(Math.ceil(length / 320), 1, maxDetail);
  const steps = Array.from({ length: stepCount + 1 }, (_, i) => {
    const t = i / stepCount;
    return { x: lower.x + run * t, y: lower.y + HE * t };
  });
  const supportDefinitions = [
    { visible: read(context, "s1") === 0, x: SUP1, height: HSUP1, clearance: HM1, code: "SUP1" },
    { visible: read(context, "s2") === 0, x: SUP2, height: HSUP2, clearance: HM2, code: "SUP2" }
  ];
  const validSupport = support => finite(support.x) && support.x >= 0 && finite(support.height) && support.height > 0 && finite(support.clearance) && support.clearance >= 0;
  const unavailableSupports = supportDefinitions.filter(support => support.visible && !validSupport(support)).map(support => support.code);
  const supports = supportDefinitions.filter(support => support.visible && validSupport(support));
  const pitConfigured = context?.["$pit_v"] !== undefined || context?.pitType !== undefined;
  const hasPit = finite(Lpit) && Lpit > 0 && finite(Dpit) && Dpit > 0 &&
    (!pitConfigured || /приям|pit/iu.test(String(pitType)));
  return {
    TK, TJ, HE, alpha, TG, LL, run, inclineLength: length, lower, upper, ux, uy, nx, ny,
    FC, FJ, FK, HH, CH, Lpit, Dpit, PKD, Wpit, N, hasPit,
    supports, unavailableSupports, steps, maxDetail, floor: { y: 0 }, balustradeMaterial, pitType,
    truss: [lower, upper, { x: upper.x + nx * FC, y: upper.y + ny * FC }, { x: lower.x + nx * FC, y: lower.y + ny * FC }]
  };
}

/** Renders the geometry as a source-proportional schematic SVG. */
export function renderEscalatorPreviewSvg(g) {
  if (!g) return "";
  const W = 420, H = 300, pad = 28;
  const points = [...g.truss, { x: 0, y: -Math.max(g.Dpit || 0, g.FK) }, { x: Math.max(g.LL, g.Lpit || 0), y: g.HE + g.CH + 300 }, { x: g.LL, y: g.HE - g.FJ }, ...g.supports.flatMap(s => [{x: s.x, y: s.clearance}, {x: s.x, y: s.height + s.clearance}])];
  const minX = Math.min(...points.map(p => p.x), 0), maxX = Math.max(...points.map(p => p.x), g.TG);
  const minY = Math.min(...points.map(p => p.y), -(g.Dpit || 0)), maxY = Math.max(...points.map(p => p.y));
  const scale = Math.min((W - 2 * pad) / Math.max(maxX - minX, 1), (H - 2 * pad) / Math.max(maxY - minY, 1));
  const X = x => pad + (x - minX) * scale;
  const Y = y => H - pad - (y - minY) * scale;
  const line = (a, b, cls) => `<line class="${cls}" x1="${X(a.x).toFixed(2)}" y1="${Y(a.y).toFixed(2)}" x2="${X(b.x).toFixed(2)}" y2="${Y(b.y).toFixed(2)}" />`;
  const poly = (ps, cls) => `<polyline fill="none" class="${cls}" points="${ps.map(p => `${X(p.x).toFixed(2)},${Y(p.y).toFixed(2)}`).join(" ")}" />`;
  const landingLow = line({x: 0,y: 0}, {x: g.TK,y: 0}, "escalator-preview-svg__landing");
  const landingHigh = line({x: g.TG-g.TJ,y: g.HE}, {x: g.TG,y: g.HE}, "escalator-preview-svg__landing");
  const railClass = String(g.balustradeMaterial).toLowerCase().includes("стек")
    ? "escalator-preview-svg__glass-line" : "escalator-preview-svg__balustrade";
  // Transition curves and individual steps are schematic; the source anchors remain fixed.
  const rails = `<path fill="none" class="${railClass}" d="M ${X(0)} ${Y(0)} Q ${X(0)} ${Y(g.CH)} ${X(Math.min(g.TK / 3, g.CH))} ${Y(g.CH)} L ${X(g.TK)} ${Y(g.CH)} L ${X(g.upper.x)} ${Y(g.HE + g.CH)} L ${X(g.TG - Math.min(g.TJ / 3, g.CH))} ${Y(g.HE + g.CH)} Q ${X(g.TG)} ${Y(g.HE + g.CH)} ${X(g.TG)} ${Y(g.HE)}" />`;
  const stepLines = g.steps.slice(0, -1).map((p, i) => {
    const next = g.steps[i + 1];
    return poly([p, {x: next.x, y: p.y}, next], "escalator-preview-svg__step");
  }).join("");
  const body = poly([{x:0,y:0}, g.lower, g.upper, {x:g.TG,y:g.HE},
    {x:g.TG,y:g.HE-g.FJ}, g.truss[2], g.truss[3], {x:0,y:-g.FK}, {x:0,y:0}], "escalator-preview-svg__pdf-line");
  const supports = g.supports.map(s => {
    const foot = s.clearance;
    const top = s.clearance + s.height;
    return `<line data-support="${s.code}" class="escalator-preview-svg__support" x1="${X(s.x)}" y1="${Y(top)}" x2="${X(s.x)}" y2="${Y(foot)}" /><line class="escalator-preview-svg__support-pad" x1="${X(s.x-120)}" y1="${Y(foot)}" x2="${X(s.x+120)}" y2="${Y(foot)}" />`;
  }).join("");
  const pit = g.hasPit ? `<path class="escalator-preview-svg__pit-outline" d="M ${X(0)} ${Y(0)} V ${Y(-g.Dpit)} H ${X(g.Lpit)} V ${Y(0)}" />` : "";
  const opening = !g.hasPit && finite(g.Dpit) && g.Dpit > 0
    ? `<path class="escalator-preview-svg__opening-edge" d="M ${X(0)} ${Y(0)} V ${Y(-g.Dpit)} H ${X(g.Lpit || g.TK)} V ${Y(0)}" />` : "";
  const dims = `<text class="escalator-preview-svg__tiny-label" x="210" y="16">TK ${g.TK} · TJ ${g.TJ} · HE ${g.HE} · ${g.alpha}° · LL ${g.LL}</text><text class="escalator-preview-svg__tiny-label" x="210" y="29">FC ${g.FC} · CH ${g.CH} · HH ${g.HH} · FJ ${g.FJ} · FK ${g.FK} · PKD ${g.PKD}</text><text class="escalator-preview-svg__tiny-label" x="210" y="42">${g.hasPit ? `${escapeXml(g.pitType)} ${g.Lpit}×${g.Dpit} · ` : ""}${finite(g.Wpit) && g.Wpit !== -1 ? `Wpit ${g.Wpit} · ` : ""}${escapeXml(g.balustradeMaterial)}</text>`;
  return `<svg class="shaft-preview-svg escalator-preview-svg" viewBox="0 0 ${W} ${H}" role="img" aria-label="Параметрический профиль эскалатора"><g>${line({x:0,y:0},{x:g.LL,y:0},"escalator-preview-svg__floor")}${landingLow}${landingHigh}${body}${rails}${stepLines}${supports}${pit}${opening}</g>${dims}</svg>`;
}
