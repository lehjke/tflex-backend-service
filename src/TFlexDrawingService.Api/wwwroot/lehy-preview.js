const LEHY_TEMPLATE_IDS = new Set([
  "lehy_l_pro_320_1050",
  "lehy_l_pro_1050_2500",
  "lehy_pro_side_cwt",
  "lehy_pro_rear_cwt"
]);

const finite = value => {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const parsed = Number(String(value).replace(",", "."));
  return Number.isFinite(parsed) ? parsed : null;
};

const positive = value => value !== null && value > 0;

function mirrorX(rect, width) {
  return rect ? { ...rect, x: width - rect.x - rect.width } : null;
}

export function isLehyPreviewTemplate(template) {
  return LEHY_TEMPLATE_IDS.has(template?.id);
}

export function getLehyPreviewGeometry(template, context) {
  if (!isLehyPreviewTemplate(template)) return null;
  const id = template.id;
  const lpro = id.startsWith("lehy_l_pro_");
  const rearCwt = id === "lehy_pro_rear_cwt";
  const width = finite(context?.AH);
  const depth = finite(context?.BH);
  const cabinWidth = finite(context?.AA);
  const cabinDepth = finite(context?.BB);
  const ah = finite(context?.AH);
  const a3 = finite(context?.A3);
  const cb = finite(context?.CB);
  const cabinAxisX = lpro
    ? ah !== null && a3 !== null ? ah - a3 : null
    : ah !== null && cb !== null ? ah - cb : null;
  const a8 = finite(context?.A8);
  const cwtAxisX = lpro ? finite(context?.A1) : rearCwt
    ? ah !== null && a8 !== null ? ah - a8 : null
    : finite(context?.BW);
  const cabinOuterWidth = finite(context?.AS) ?? (lpro ? cabinWidth + 62 : cabinWidth + 2 * 31);
  const openingWidth = finite(context?.JJ);
  const kk = finite(context?.KK);
  const carDoorDepth = finite(context?.DK) !== null && kk !== null ? finite(context?.DK) - kk : null;
  const landingDoorDepth = finite(context?.B1);
  const ww = finite(context?.WW);
  const wg = finite(context?.WG);
  const ee = finite(context?.EE);
  const centerOpening = /^(ЦО|CO|CLD)$/i.test(String(context?.$door_type ?? context?.door_type ?? "").trim());
  const doorWidth = positive(openingWidth)
    ? centerOpening ? openingWidth * 2 : openingWidth * 1.5 + 175
    : null;
  const mirrorXEnabled = !rearCwt && /^(справа|направо|right|1)$/i.test(String(context?.$s ?? context?.s ?? "").trim());
  const entrances = finite(context?.NE);
  if (![1, 2].includes(entrances)) return null;

  if ([width, depth, cabinWidth, cabinDepth, cabinAxisX, cabinOuterWidth, carDoorDepth, landingDoorDepth, doorWidth]
    .some(value => value === null)
    || [width, depth, cabinWidth, cabinDepth, cabinOuterWidth, carDoorDepth, landingDoorDepth, doorWidth]
      .some(value => !positive(value))) return null;

  const cabinInnerRect = {
    x: cabinAxisX - cabinWidth / 2,
    y: null,
    width: cabinWidth,
    height: cabinDepth
  };
  // Vertical cabin and counterweight placement remains schematic until the native depth-axis
  // relationship is verified. Keep the existing preview's front/rear depth placement.
  const cabinRearWall = rearCwt && entrances > 1 ? finite(context?.DK) ?? 141 : finite(context?.BB_CLEARANCE) ?? 30;
  const cabinFrontWall = finite(context?.KK) ?? 45;
  const cabinOuterHeight = rearCwt && positive(finite(context?.BS))
    ? finite(context?.BS)
    : cabinDepth + cabinRearWall + cabinFrontWall;
  const cc = finite(context?.CC);
  const hasCc = cc !== null && cc !== -1;
  const cabinOuterY = rearCwt && hasCc
    ? cc - cabinDepth / 2 - cabinRearWall
    : depth - landingDoorDepth - 30 - carDoorDepth - cabinOuterHeight;
  const cabinOuterRect = {
    x: cabinAxisX - cabinOuterWidth / 2,
    y: cabinOuterY,
    width: cabinOuterWidth,
    height: cabinOuterHeight
  };
  cabinInnerRect.y = cabinOuterY + cabinRearWall;

  const centerOpeningOffset = finite(rearCwt ? context?.CJ : context?.A4) ?? 0;
  const rearOpensRight = rearCwt && /^(направо|right|1)$/i.test(String(context?.$s_1 ?? ""));
  const doorX = centerOpening
    ? cabinInnerRect.x + cabinInnerRect.width / 2 + centerOpeningOffset - doorWidth / 2
    : rearOpensRight ? cabinOuterRect.x - 25 : cabinOuterRect.x + cabinOuterRect.width + 25 - doorWidth;
  const doorPairs = Array.from({ length: Math.min(2, entrances) }, (_, index) => {
    const rear = index === 1;
    const carDoorY = rearCwt
      ? (rear ? cabinOuterRect.y : cabinOuterRect.y + cabinOuterRect.height - carDoorDepth)
      : (rear ? cabinOuterRect.y - carDoorDepth : cabinOuterRect.y + cabinOuterRect.height);
    const landingDoorY = rear
      ? carDoorY - 30 - landingDoorDepth
      : carDoorY + carDoorDepth + 30;
    return {
      side: rear ? "rear" : "front",
      carDoor: { x: doorX, y: carDoorY, width: doorWidth, height: carDoorDepth },
      landingDoor: { x: doorX, y: landingDoorY, width: doorWidth, height: landingDoorDepth }
    };
  });

  const cwtRect = positive(ww) && positive(wg) && cwtAxisX !== null
    ? rearCwt
      ? {
        x: cwtAxisX - wg / 2,
        y: hasCc
          ? cc - cabinDepth / 2 - cabinRearWall - ww - 30
          : 30,
        width: wg,
        height: ww
      }
      : { x: cwtAxisX - ww / 2, y: positive(ee) ? depth - (ee + 150) - wg : (depth - wg) / 2, width: ww, height: wg }
    : null;
  const source = { cabinInnerRect, cabinOuterRect, cwtRect, doorPairs };
  const mirrored = mirrorXEnabled
    ? {
      cabinInnerRect: mirrorX(cabinInnerRect, width),
      cabinOuterRect: mirrorX(cabinOuterRect, width),
      cwtRect: mirrorX(cwtRect, width),
      doorPairs: doorPairs.map(pair => ({ ...pair, carDoor: mirrorX(pair.carDoor, width), landingDoor: mirrorX(pair.landingDoor, width) }))
    }
    : source;

  return {
    width, depth, cabinWidth, cabinDepth, cabinAxisX, cwtAxisX, mirrorX: mirrorXEnabled, rearCwt,
    counterweightWidth: positive(ww) ? ww : null,
    counterweightDepth: positive(wg) ? wg : null,
    doorWidth, carDoorDepth, landingDoorDepth, centerOpening, entrances,
    source, ...mirrored
  };
}
