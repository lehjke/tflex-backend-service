const finite = value => {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const number = Number(String(value).replace(",", "."));
  return Number.isFinite(number) ? number : null;
};
const str = value => String(value ?? "");
const bounded = (value, max = 54) => {
  const label = str(value);
  return label.length <= max ? label : `${label.slice(0, max - 1)}…`;
};
const xml = value => str(value).replace(/[&<>"']/gu, ch => ({
  "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;"
}[ch]));
const CEILING_100 = ["ZCL-GN07", "ZCL-SS07", "ZCL-SS10", "ZCL-CN01", "ZCL-GS24"];
const FINISH_TINTS = {
  "ZDT-001": "#f3d9d5", "ZDT-002": "#f3e7c7", "ZDT-003": "#d9dce1",
  "ZDT-004": "#e7d4b2", "ZDT-005": "#8b8e92", "ZDT-006": "#bc9273"
};

export function getCabinUnfoldingGeometry(context) {
  const AA = finite(context?.AA), BB = finite(context?.BB), HL = finite(context?.HL);
  const HH = finite(context?.HH), JJ = finite(context?.JJ);
  const A4 = finite(context?.A4 ?? context?.A4_v);
  const opening = str(context?.$Opening ?? context?.Opening);
  const doorsType = str(context?.$DoorsType ?? context?.DoorsType);
  if ([AA, BB, HL, HH, JJ, A4].some(v => v === null) || [AA, BB, HL, HH, JJ].some(v => v <= 0)
    || HH > HL || JJ > AA || (opening === "CO" && Math.abs(A4) > 150)
    || !["2S", "CO"].includes(opening) || !["1D-1G", "1D-2G"].includes(doorsType)) return null;

  const COP = str(context?.$COP ?? context?.COP);
  const copPlace = str(context?.$CopPlace ?? context?.CopPlace
    ?? context?.$CopPlace_v_1 ?? context?.CopPlace_v_1);
  const ceilingType = str(context?.$CeilingType ?? context?.CeilingType);
  const exposedCeilingHeight = context?.CeilingHeight;
  const ceilingHeight = exposedCeilingHeight === undefined || exposedCeilingHeight === null || String(exposedCeilingHeight).trim() === ""
    ? (CEILING_100.includes(ceilingType) ? 100 : 200) : finite(exposedCeilingHeight);
  if (ceilingHeight === null || ceilingHeight <= 0 || ceilingHeight >= HL) return null;

  const through = doorsType === "1D-2G"; // Template field caption is "Проходная/непроходная".
  const doorCenter = AA / 2 + (opening === "2S" ? -A4 : A4);
  if (doorCenter - JJ / 2 < 0 || doorCenter + JJ / 2 > AA) return null;

  // WB is a native panel width. Detailed COP construction/vertical placement is schematic.
  const COPWidth = finite(context?.WB) ?? (["ZCB-T611", "ZCB-T311", "ZCB-T711", "ZCB-T811", "ZCB-T81H"].includes(COP) ? 360 : 220);
  const COPSidePosition = finite(context?.M);
  // Place the front panel on the available door flank, rather than over the aperture.
  const frontFlank = AA - doorCenter - JJ / 2;
  const COPFrontPosition = AA - frontFlank / 2;
  const frontClearance = finite(context?.F);
  const copCenter = copPlace === "Front" ? COPFrontPosition : COPSidePosition;
  const copExtent = copPlace === "Front" ? AA : BB;
  if (COPWidth <= 0 || (copPlace === "Front" && COPWidth > frontFlank) || !["Front", "Side"].includes(copPlace)
    || (copCenter !== null && (copCenter - COPWidth / 2 < 0 || copCenter + COPWidth / 2 > copExtent))) return null;
  const titanium = str(context?.$TitanimCoatedCode ?? context?.TitanimCoatedCode);
  return {
    AA, BB, HL, HH, JJ, A4, opening, doorsType, through, doorCenter, doorWidth: JJ,
    frontDoor: { wall: "A", center: doorCenter, width: JJ },
    rearDoor: through ? { wall: "C", center: doorCenter, width: JJ } : null,
    cop: COP, copPlace, copWall: copPlace === "Front" ? "A" : "B",
    copWidth: COPWidth, copCenter, frontClearance,
    ceilingType, ceilingHeight, ceilingBandTop: HL - ceilingHeight,
    floorType: str(context?.$FloorType ?? context?.FloorType),
    decorCode: str(context?.$DecorCode ?? context?.DecorCode),
    colorCode: str(context?.$DecorCode ?? context?.DecorCode) === "Painted steel"
      ? str(context?.$ColorCode ?? context?.ColorCode) : "",
    titaniumCode: titanium, tint: FINISH_TINTS[titanium] ?? "#f8fafc"
  };
}

export function renderCabinUnfoldingPreviewSvg(g, { planOnly = false } = {}) {
  if (!g) return "";
  const { AA, BB, HL, HH, JJ, A4, opening, through, doorCenter, cop, copPlace, copWidth, copCenter, frontClearance,
    ceilingHeight, ceilingType, floorType, decorCode, colorCode, titaniumCode, tint } = g;
  const elevationScale = Math.min(164 / Math.max(AA, BB), 72 / HL);
  const wallHeight = HL * elevationScale, frontWidth = AA * elevationScale, sideWidth = BB * elevationScale;
  const doorWidth = JJ * elevationScale, doorHeight = HH * elevationScale;
  const ceilingBand = ceilingHeight * elevationScale;
  const top = 148, rowGap = wallHeight + 28;
  const frontLeft = 32, sideLeft = 224;
  const planScale = Math.min(112 / AA, 68 / BB);
  const planWidth = AA * planScale, planDepth = BB * planScale;
  const planDoorWidth = JJ * planScale;
  const planX = 32, planY = 37;
  const doorLeft = (doorCenter - JJ / 2) * planScale;
  const panelFill = tint;
  const wallViews = [
    { label: "A · фронт", x: frontLeft, width: frontWidth, hasDoor: true, hasCop: copPlace === "Front", length: AA },
    { label: "B · бок", x: sideLeft, width: sideWidth, hasDoor: false, hasCop: copPlace === "Side", length: BB },
    { label: "C · зад", x: frontLeft, width: frontWidth, hasDoor: through, hasCop: false, length: AA },
    { label: "D · бок", x: sideLeft, width: sideWidth, hasDoor: false, hasCop: false, length: BB }
  ];
  const views = wallViews.map((wall, i) => {
    const y = top + Math.floor(i / 2) * rowGap;
    const apertureLeft = (doorCenter - JJ / 2) * elevationScale;
    const aperture = wall.hasDoor
      ? `<rect data-door-wall="${wall.label[0]}" x="${wall.x + apertureLeft}" y="${y + wallHeight - doorHeight}" width="${doorWidth}" height="${doorHeight}" fill="white" stroke="#465360" stroke-width="1.5"/>`
      : "";
    const panelCenter = wall.hasCop && copCenter !== null ? copCenter * elevationScale : wall.width / 2;
    const panelWidth = copWidth * elevationScale;
    const panel = wall.hasCop
      ? `<rect data-cop-wall="${wall.label[0]}" x="${wall.x + panelCenter - panelWidth / 2}" y="${y + wallHeight * .42}" width="${panelWidth}" height="${wallHeight * .26}" fill="#dce3e9" stroke="#465360" stroke-width="1.2"/><text x="${wall.x + panelCenter}" y="${y + wallHeight * .58}" class="shaft-preview-svg__label">КОП</text>`
      : "";
    return `<text x="${wall.x}" y="${y - 5}" class="shaft-preview-svg__label shaft-preview-svg__label--start">${wall.label}</text><rect x="${wall.x}" y="${y}" width="${wall.width}" height="${wallHeight}" fill="${panelFill}" stroke="#465360" stroke-width="1.5"/><rect x="${wall.x}" y="${y}" width="${wall.width}" height="${ceilingBand}" fill="#e8edf2" stroke="none"/>${aperture}${panel}`;
  }).join("");
  const planAperture = `<line x1="${planX + doorLeft}" y1="${planY + planDepth}" x2="${planX + doorLeft + planDoorWidth}" y2="${planY + planDepth}" stroke="white" stroke-width="3"/>`;
  const rearAperture = through
    ? `<line x1="${planX + doorLeft}" y1="${planY}" x2="${planX + doorLeft + planDoorWidth}" y2="${planY}" stroke="white" stroke-width="3"/>`
    : "";
  const frontMotion = opening === "CO"
    ? `<path d="M${planX + doorLeft + planDoorWidth / 2},${planY + planDepth + 3} l-6,6 m6,-6 l6,6" fill="none" stroke="#64748b" stroke-width="1.3"/>`
    : `<path d="M${planX + doorLeft + planDoorWidth - 1},${planY + planDepth + 5} h-12" fill="none" stroke="#64748b" stroke-width="1.3"/>`;
  const rearMotion = opening === "CO"
    ? `<path d="M${planX + doorLeft + planDoorWidth / 2},${planY - 3} l-6,-6 m6,6 l6,-6" fill="none" stroke="#64748b" stroke-width="1.3"/>`
    : `<path d="M${planX + doorLeft + 1},${planY - 5} h12" fill="none" stroke="#64748b" stroke-width="1.3"/>`;
  if (planOnly) return `<svg class="shaft-preview-svg" viewBox="0 0 180 130" role="img" aria-label="Схематичный план развертки кабины LEHY"><text x="12" y="18" class="shaft-preview-svg__label shaft-preview-svg__label--start">План · AA ${AA} × BB ${BB} мм</text><rect x="${planX}" y="${planY}" width="${planWidth}" height="${planDepth}" fill="${panelFill}" stroke="#465360" stroke-width="1.5"/>${planAperture}${rearAperture}${frontMotion}${through ? rearMotion : ""}</svg>`;
  const labels = [
    `HL ${HL} · HH ${HH} · JJ ${JJ} · A4 ${A4 >= 0 ? "+" : ""}${A4} мм`,
    `КОП ${cop} ${copPlace} WB${copWidth}${copCenter === null ? "" : ` @${copCenter}`}${frontClearance === null ? "" : ` F${frontClearance}`}`,
    `Потолок ${ceilingType} · пояс ${ceilingHeight} мм · пол ${floorType}`,
    `Отделка ${decorCode}${colorCode ? ` · цвет ${colorCode}` : ""}${titaniumCode && titaniumCode !== "Regular" ? ` · ${titaniumCode}` : ""}`
  ];
  return `<svg class="shaft-preview-svg" viewBox="0 0 420 460" role="img" aria-label="Схема развертки кабины LEHY, пропорциональный эскиз">
    <text x="12" y="18" class="shaft-preview-svg__label shaft-preview-svg__label--start">План · AA ${AA} × BB ${BB} мм</text>
    <rect x="${planX}" y="${planY}" width="${planWidth}" height="${planDepth}" fill="${panelFill}" stroke="#465360" stroke-width="1.5"/>${planAperture}${rearAperture}${frontMotion}${through ? rearMotion : ""}
    <text x="${planX + planWidth + 6}" y="${planY + planDepth / 2}" class="shaft-preview-svg__label">${through ? "A / C" : "A"}</text>
    ${views}
    ${labels.map((label, i) => `<text x="12" y="${404 + i * 14}" class="shaft-preview-svg__label shaft-preview-svg__label--start">${xml(bounded(label))}</text>`).join("")}
  </svg>`;
}
